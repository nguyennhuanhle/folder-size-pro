using FolderSizePro.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;

namespace FolderSizePro.Scan;

internal readonly record struct DirTask(int Node, ulong Serial);

internal readonly record struct HardLinkCand(ulong Serial, ulong IdLo, ulong IdHi, int Node, long Logical, long Allocated, long Resident, ushort Links);

/// <summary>
/// Một phiên quét thư mục theo chế độ "Exact" (plan 3.1): liệt kê bằng NtQueryDirectoryFile, rồi MỞ TỪNG FILE ở mức
/// FILE_READ_ATTRIBUTES để lấy kích thước thật (Std) + ADS. Không bao giờ đọc dữ liệu file, không đi theo reparse point,
/// không liệt kê thư mục đám mây chưa nạp (Q3, Q4, Q10; KT-05).
/// </summary>
public sealed unsafe class ScanJob : IScanJob
{
    readonly ScanOptions _opt;
    readonly string[] _rootPaths;
    readonly ScanTree _tree;
    int _rescanNode = -1;
    readonly ScanResult _result;
    readonly CancellationTokenSource _cts = new();
    readonly ManualResetEventSlim _gate = new(true);
    readonly BlockingCollection<DirTask> _queue = new(new ConcurrentQueue<DirTask>());
    readonly ConcurrentQueue<ScanIssue> _issues = new();
    readonly ConcurrentQueue<HardLinkCand[]> _candBatches = new();
    readonly object _mergeLock = new();
    readonly Dictionary<string, TypeAcc> _types = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(int Node, long Logical, long Allocated)> _topCands = new();
    readonly HashSet<string> _excluded;
    readonly Stopwatch _sw = new();
    readonly bool _remote;

    int _pending;
    long _files, _dirs, _logical, _allocated, _problems;
    volatile string _current = "";
    volatile bool _compact, _finishing;
    bool _volumeRootAny;
    Worker[] _workers = Array.Empty<Worker>();
    Thread? _watchdog;

    public ScanTree Tree => _tree;
    public ScanResult Result => _result;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool IsPaused => !_gate.IsSet;
    public bool CompactMode => _compact;

    sealed class TypeAcc { public long Count, Logical, Allocated; }

    ScanJob(IEnumerable<string> roots, ScanOptions opt)
    {
        _opt = opt;
        _rootPaths = roots.ToArray();
        _tree = new ScanTree();
        _result = new ScanResult { Tree = _tree, Options = opt };
        _excluded = new HashSet<string>(opt.ExcludedPaths.Select(NormalizeExcluded), StringComparer.OrdinalIgnoreCase);
        _remote = _rootPaths.Any(p => VolumeInfo.For(p).Kind == VolumeKind.Network);
    }

    ScanJob(ScanResult existing, int node)
    {
        _opt = existing.Options; _rootPaths = Array.Empty<string>(); _tree = existing.Tree; _result = existing; _rescanNode = node;
        _excluded = new HashSet<string>(_opt.ExcludedPaths.Select(NormalizeExcluded), StringComparer.OrdinalIgnoreCase);
        _remote = existing.Roots.Any(r => r.Volume.Kind == VolumeKind.Network);
    }

    /// <summary>UC-17: quét lại riêng một nhánh (thư mục) trong kết quả đã có; tổng các thư mục cha được cập nhật theo.</summary>
    public static ScanJob StartRescan(ScanResult existing, int node)
    {
        var job = new ScanJob(existing, node);
        job.Completion = Task.Factory.StartNew(job.RunRescan, TaskCreationOptions.LongRunning);
        return job;
    }

    static string NormalizeExcluded(string p)
    {
        try { p = Path.GetFullPath(p); } catch { }
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }

    /// <summary>Khởi chạy phiên quét. Roots đã được PathValidator kiểm tra; ở đây kiểm lại phòng lỗi.</summary>
    public static ScanJob Start(IEnumerable<string> roots, ScanOptions? options = null)
    {
        var job = new ScanJob(roots, options ?? new ScanOptions());
        job.Completion = Task.Factory.StartNew(job.RunAll, TaskCreationOptions.LongRunning);
        return job;
    }

    public static ScanResult Run(IEnumerable<string> roots, ScanOptions? options = null)
    {
        var job = Start(roots, options);
        job.Completion.Wait();
        return job.Result;
    }

    public void Pause() => _gate.Reset();
    public void Resume() => _gate.Set();
    public void Cancel() { _cts.Cancel(); _gate.Set(); }

    public ScanProgress Snapshot()
    {
        var el = _sw.Elapsed;
        long f = Interlocked.Read(ref _files);
        return new ScanProgress(f, Interlocked.Read(ref _dirs), Interlocked.Read(ref _logical), Interlocked.Read(ref _allocated),
            Interlocked.Read(ref _problems), _current, el, el.TotalSeconds > 0 ? f / el.TotalSeconds : 0, IsPaused, _compact, _finishing);
    }

    // ===================================================================================== chạy
    void RunAll()
    {
        _result.StartedUtc = DateTime.UtcNow;
        if (Analysis.Reconciler.IsAdmin()) BackupPrivilege.TryEnable();          // UC-31: đọc được cả thư mục chỉ SYSTEM vào
        _sw.Start();
        try
        {
            SetupRoots();
            RunWorkers();
            if (_cts.IsCancellationRequested) { _result.Completeness = Completeness.Partial; _result.Notes.Add(L.T("Phiên quét bị huỷ — kết quả dở dang.", "Scan cancelled — result is partial.")); }
        }
        catch (Exception ex)
        {
            _result.Completeness = Completeness.Partial;
            _result.Notes.Add("Lỗi không lường trước khi quét: " + ex.Message);
        }
        _finishing = true;
        foreach (var iss in _issues) _result.Issues.Add(iss);
        _result.CompactModeUsed = _compact;
        var cands = new List<HardLinkCand>();
        foreach (var b in _candBatches) cands.AddRange(b);
        Finalizer.Run(_result, cands, _types.Select(kv => (kv.Key, kv.Value.Count, kv.Value.Logical, kv.Value.Allocated)).ToList(),
            _topCands, Interlocked.Read(ref _logical), Interlocked.Read(ref _allocated));
        if (_result.Issues.Any(i => i.Kind is IssueKind.VolumeLost or IssueKind.NetworkTimeout)) _result.Completeness = Completeness.Partial;
        if (_opt.Approximate) _result.Notes.Add(L.T("Quét nhanh: kích thước lấy từ danh sách thư mục, bỏ ADS — KÉM CHÍNH XÁC.", "Fast scan: sizes from directory listing, ADS skipped — LESS ACCURATE."));
        _sw.Stop();
        _result.CompletedUtc = DateTime.UtcNow;
    }

    void RunWorkers()
    {
        if (_pending <= 0) return;
        int threads = ChooseThreads();
        _workers = new Worker[threads];
        var ts = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            var w = _workers[i] = new Worker(this);
            ts[i] = new Thread(w.Loop) { IsBackground = true, Name = "fsp-scan-" + i };
            ts[i].Start();
        }
        if (_remote) { _watchdog = new Thread(Watchdog) { IsBackground = true, Name = "fsp-watchdog" }; _watchdog.Start(); }
        foreach (var t in ts) t.Join();
    }

    void RunRescan()
    {
        _sw.Start();
        string branch = _tree.FullPath(_rescanNode);
        try
        {
            PrepareRescan(branch);
            RunWorkers();
            if (_cts.IsCancellationRequested) { _result.Completeness = Completeness.Partial; _result.Notes.Add("Quét lại nhánh " + branch + " bị huỷ — nhánh này dở dang."); }
        }
        catch (Exception ex)
        {
            _result.Completeness = Completeness.Partial;
            _result.Notes.Add("Lỗi khi quét lại nhánh: " + ex.Message);
        }
        _finishing = true;
        foreach (var iss in _issues) _result.Issues.Add(iss);
        var cands = new List<HardLinkCand>();
        foreach (var b in _candBatches) cands.AddRange(b);
        Finalizer.RunRescan(_result, cands);
        _result.Notes.Add($"Đã quét lại nhánh {branch} lúc {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
        _sw.Stop();
        _result.CompletedUtc = DateTime.UtcNow;
    }

    void PrepareRescan(string branch)
    {
        int node = _rescanNode;
        // vô hiệu hoá toàn bộ con cháu cũ
        var st = new Stack<int>(); st.Push(node);
        while (st.Count > 0)
        {
            int cur = st.Pop(); ref var cn = ref _tree[cur];
            if (!cn.IsDir) continue;
            for (int k = 0; k < cn.ChildCount; k++) { int c = cn.FirstChild + k; if (_tree[c].Parent == -2) continue; st.Push(c); _tree[c].Parent = -2; }
        }
        ref var n = ref _tree[node];
        long l = n.Logical, a = n.Allocated, r = n.Resident; int f = n.FileCount, d = n.DirCount;
        for (int i = n.Parent; i >= 0; i = _tree[i].Parent)
        {
            ref var x = ref _tree[i];
            Interlocked.Add(ref x.Logical, -l); Interlocked.Add(ref x.Allocated, -a); Interlocked.Add(ref x.Resident, -r);
            Interlocked.Add(ref x.FileCount, -f); Interlocked.Add(ref x.DirCount, -d);
        }
        n.Logical = n.Allocated = n.Resident = n.OwnLogical = n.OwnAllocated = n.OwnResident = 0;
        n.FileCount = n.DirCount = 0; n.ChildCount = 0; n.FirstChild = 0;
        n.Flags &= ~(NodeFlags.AccessDenied | NodeFlags.Changed | NodeFlags.Lost | NodeFlags.TimedOut | NodeFlags.Unknown);
        _result.HardLinkCands.RemoveAll(c => _tree[c.Node].Parent == -2);
        string prefix = branch.TrimEnd('\\') + "\\";
        _result.Issues.RemoveAll(i => i.Path.Equals(branch, StringComparison.OrdinalIgnoreCase) || i.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        int top = node; while (_tree[top].Parent > 0) top = _tree[top].Parent;
        ulong serial = _result.Roots.First(x => x.Node == top).Volume.Serial;
        _pending = 1; _queue.Add(new DirTask(node, serial));
    }

    int ChooseThreads()
    {
        if (_opt.MaxThreads > 0) return _opt.MaxThreads;
        var vols = _result.Roots.Select(r => r.Volume).ToList();
        if (vols.Any(v => v.Kind == VolumeKind.Network)) return 4;
        if (vols.Any(v => v.IsSsd == false)) return 2;          // HDD
        return Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
    }

    void SetupRoots()
    {
        // loại gốc trùng / lồng nhau (KT-07 trong phạm vi một phiên)
        var list = new List<string>();
        foreach (var p in _rootPaths.Select(p => Path.GetFullPath(p)).OrderBy(p => p.Length))
        {
            var np = p.Length > 3 ? p.TrimEnd('\\') : p;
            if (list.Any(x => np.Equals(x, StringComparison.OrdinalIgnoreCase) || np.StartsWith(x.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            { _result.Notes.Add($"Bỏ qua '{np}' vì đã nằm trong/trùng với một gốc quét khác."); continue; }
            list.Add(np);
        }
        int first = _tree.AllocRange(list.Count);
        var nw = new NameWriter(_tree);
        for (int i = 0; i < list.Count; i++)
        {
            string path = list[i];
            var vol = VolumeInfo.For(path);
            bool isVolRoot = path.TrimEnd('\\').Equals(vol.RootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            _volumeRootAny |= isVolRoot;
            ref var n = ref _tree[first + i];
            n.Parent = 0; n.Flags = NodeFlags.Dir; n.NameOffset = nw.Write(path); n.NameLen = (ushort)path.Length;
            _result.Roots.Add(new RootInfo { Node = first + i, Path = path, Volume = vol, IsVolumeRoot = isVolRoot });
            Interlocked.Increment(ref _pending);
            _queue.Add(new DirTask(first + i, vol.Serial));
        }
        ref var root = ref _tree[0];
        root.FirstChild = first; root.ChildCount = list.Count;
        if (list.Count == 0) _queue.CompleteAdding();
        _pending = Math.Max(_pending, 0);
        if (_pending == 0) _queue.CompleteAdding();
    }

    void Watchdog()
    {
        long limit = (long)_opt.NetworkTimeoutSec * Stopwatch.Frequency;
        while (!_queue.IsCompleted && !_cts.IsCancellationRequested)
        {
            Thread.Sleep(1000);
            long now = Stopwatch.GetTimestamp();
            foreach (var w in _workers)
            {
                long st = Volatile.Read(ref w.OpStart); var h = w.OpHandle;
                if (st != 0 && h != IntPtr.Zero && now - st > limit) { w.TimedOut = true; NtApi.CancelIoEx(h, IntPtr.Zero); }
            }
        }
    }

    // ===================================================================================== worker
    sealed class Worker
    {
        readonly ScanJob _j; readonly ScanTree _t; readonly NameWriter _nw;
        readonly byte* _listBuf; readonly byte* _infoBuf; readonly int _infoLen = 64 * 1024;
        byte* _bigBuf; int _bigLen;
        const int ListLen = 256 * 1024;
        readonly List<Ent> _ents = new();
        readonly List<HardLinkCand> _cands = new();
        readonly Dictionary<string, TypeAcc> _types = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TypeAcc>.AlternateLookup<ReadOnlySpan<char>> _typeLookup;
        readonly PriorityQueue<(int Node, long Logical), long> _top = new();
        readonly int _topKeep;
        public long OpStart; public IntPtr OpHandle; public volatile bool TimedOut;

        public Worker(ScanJob j)
        {
            _j = j; _t = j._tree; _nw = new NameWriter(_t);
            _listBuf = (byte*)NativeMemory.Alloc(ListLen);
            _infoBuf = (byte*)NativeMemory.Alloc((nuint)_infoLen);
            _typeLookup = _types.GetAlternateLookup<ReadOnlySpan<char>>();
            _topKeep = Math.Max(1000, j._opt.TopN * 10);
        }

        struct Ent
        {
            public int NameOff; public ushort NameLen;
            public uint Attr, Tag; public long Eof, Alloc, MTime; public ulong IdLo, IdHi;
            public long Logical, Allocated, Resident; public ushort Links;
            public NodeFlags Flags; public bool IsDir, Descend, Skip; public int FileCnt;
        }

        public void Loop()
        {
            try
            {
                foreach (var task in _j._queue.GetConsumingEnumerable(_j._cts.Token))
                {
                    try { _j._gate.Wait(_j._cts.Token); ProcessDir(task); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { _j.AddIssue(_t.FullPath(task.Node), IssueKind.Other, 0, ex.Message, task.Node, 0); }
                    finally
                    {
                        Interlocked.And(ref _t[task.Node].FlagsRaw, ~(uint)NodeFlags.Pending);
                        if (Interlocked.Decrement(ref _j._pending) == 0) _j._queue.CompleteAdding();
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_j._mergeLock)
                {
                    foreach (var kv in _types)
                    {
                        if (!_j._types.TryGetValue(kv.Key, out var a)) _j._types[kv.Key] = a = new TypeAcc();
                        a.Count += kv.Value.Count; a.Logical += kv.Value.Logical; a.Allocated += kv.Value.Allocated;
                    }
                    foreach (var (el, pr) in _top.UnorderedItems) _j._topCands.Add((el.Node, el.Logical, pr));
                }
                _j._candBatches.Enqueue(_cands.ToArray());
                NativeMemory.Free(_listBuf); NativeMemory.Free(_infoBuf); if (_bigBuf != null) NativeMemory.Free(_bigBuf);
            }
        }

        void Begin(IntPtr h) { if (_j._remote) { OpHandle = h; Volatile.Write(ref OpStart, Stopwatch.GetTimestamp()); } }
        void End() { if (_j._remote) { Volatile.Write(ref OpStart, 0); OpHandle = IntPtr.Zero; } }

        void ProcessDir(DirTask task)
        {
            string path = _t.FullPath(task.Node);
            _j._current = path;
            var vol = VolumeFor(task.Node);
            bool ntfs = vol.IsNtfs; long cluster = vol.ClusterSize;
            int st = NtApi.Open(NtApi.ToNtPath(NtApi.ToExtendedPath(path)), IntPtr.Zero, NtApi.FILE_LIST_DIRECTORY | NtApi.FILE_READ_ATTRIBUTES | NtApi.SYNCHRONIZE,
                NtApi.FILE_DIRECTORY_FILE | NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var h);
            if (st < 0) { _j.DirFailure(task.Node, path, st, TimedOut); TimedOut = false; return; }

            long dirAlloc = 0, dirRes = 0;
            _ents.Clear();
            try
            {
                // phần index của chính thư mục (Std)
                NtApi.FILE_STANDARD_INFORMATION dsi;
                if (NtApi.NtQueryInformationFile(h, out _, &dsi, (uint)sizeof(NtApi.FILE_STANDARD_INFORMATION), NtApi.FileStandardInformation) >= 0)
                    SplitResident(dsi.AllocationSize, ntfs, cluster, out dirAlloc, out dirRes);

                // liệt kê
                bool restart = true;
                while (true)
                {
                    _j._gate.Wait(_j._cts.Token);
                    Begin(h);
                    st = NtApi.NtQueryDirectoryFile(h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _, _listBuf, ListLen, NtApi.FileIdExtdDirectoryInformation, 0, null, (byte)(restart ? 1 : 0));
                    End();
                    restart = false;
                    if (st == NtApi.STATUS_NO_MORE_FILES) break;
                    if (st < 0) { _j.DirFailure(task.Node, path, st, TimedOut, partial: true); TimedOut = false; break; }
                    for (byte* p = _listBuf; ;)
                    {
                        var e = (NtApi.FILE_ID_EXTD_DIR_INFORMATION*)p;
                        var nm = new ReadOnlySpan<char>(p + NtApi.ExtdDirHeaderSize, (int)e->FileNameLength / 2);
                        if (!(nm.Length == 1 && nm[0] == '.') && !(nm.Length == 2 && nm[0] == '.' && nm[1] == '.'))
                        {
                            var ent = new Ent
                            {
                                NameOff = _nw.Write(nm), NameLen = (ushort)Math.Min(nm.Length, ushort.MaxValue),
                                Attr = e->FileAttributes, Tag = e->ReparsePointTag, Eof = e->EndOfFile, Alloc = e->AllocationSize,
                                MTime = e->LastWriteTime, IdLo = e->FileIdLow, IdHi = e->FileIdHigh,
                            };
                            _ents.Add(ent);
                        }
                        if (e->NextEntryOffset == 0) break;
                        p += e->NextEntryOffset;
                    }
                }

                // đo từng mục
                var span = CollectionsMarshal.AsSpan(_ents);
                for (int i = 0; i < span.Length; i++)
                {
                    if ((i & 127) == 0) _j._gate.Wait(_j._cts.Token);              // tạm dừng có hiệu lực ngay cả giữa một thư mục lớn
                    Measure(ref span[i], h, path, vol, task);
                }
            }
            finally { NtApi.NtClose(h); }

            Commit(task, path, dirAlloc, dirRes);
        }

        VolumeInfo VolumeFor(int node)
        {
            int n = node; while (_t[n].Parent > 0) n = _t[n].Parent;
            foreach (var r in _j._result.Roots) if (r.Node == n) return r.Volume;
            return _j._result.Roots[0].Volume;
        }

        static void SplitResident(long alloc, bool ntfs, long cluster, out long clusters, out long resident)
        {
            if (ntfs && cluster > 0 && alloc > 0 && alloc < cluster) { clusters = 0; resident = alloc; }
            else { clusters = alloc; resident = 0; }
        }

        void Measure(ref Ent e, IntPtr dirHandle, string dirPath, VolumeInfo vol, DirTask task)
        {
            uint a = e.Attr; var f = NodeFlags.None;
            if ((a & NtApi.ATTR_HIDDEN) != 0) f |= NodeFlags.Hidden;
            if ((a & NtApi.ATTR_SYSTEM) != 0) f |= NodeFlags.System;
            if ((a & NtApi.ATTR_COMPRESSED) != 0) f |= NodeFlags.Compressed;
            if ((a & NtApi.ATTR_SPARSE) != 0) f |= NodeFlags.Sparse;
            if ((a & NtApi.ATTR_ENCRYPTED) != 0) f |= NodeFlags.Encrypted;
            bool reparse = (a & NtApi.ATTR_REPARSE_POINT) != 0;
            bool cloudAttr = (a & (NtApi.ATTR_RECALL_ON_DATA_ACCESS | NtApi.ATTR_RECALL_ON_OPEN)) != 0;
            bool cloudTag = reparse && NtApi.IsCloudTag(e.Tag);
            if (cloudAttr || cloudTag) f |= NodeFlags.Cloud;
            if (reparse)
            {
                f |= NodeFlags.Reparse;
                if (e.Tag == NtApi.IO_REPARSE_TAG_MOUNT_POINT) f |= NodeFlags.MountPoint;
                else if (e.Tag == NtApi.IO_REPARSE_TAG_SYMLINK) f |= NodeFlags.Symlink;
                else if (e.Tag == NtApi.IO_REPARSE_TAG_WOF) f |= NodeFlags.Wof;
                else if (e.Tag == NtApi.IO_REPARSE_TAG_DEDUP) f |= NodeFlags.Dedup;
                else if (!cloudTag) f |= NodeFlags.OtherReparse;
            }

            if ((a & NtApi.ATTR_DIRECTORY) != 0)
            {
                e.IsDir = true; f |= NodeFlags.Dir;
                // Q3: junction / symlink / mount point / reparse lạ → không đi theo. Thư mục đám mây (reparse tag cloud) vẫn đi vào, trừ RECALL_ON_OPEN (Q4).
                bool follow = !reparse || cloudTag;
                if ((a & NtApi.ATTR_RECALL_ON_OPEN) != 0)
                {
                    f |= NodeFlags.CloudNotListed | NodeFlags.Unknown; follow = false;
                    _j.AddIssue(ChildPath(dirPath, e), IssueKind.CloudNotListed, 0, L.T("Thư mục đám mây chưa nạp danh sách — không liệt kê để tránh kích hoạt tải.", "Cloud folder has no downloaded listing — not listed to avoid triggering a download."), 0, 0, noNode: true);
                }
                if (follow && _j._excluded.Count > 0 && _j._excluded.Contains(ChildPath(dirPath, e)))
                {
                    f |= NodeFlags.Excluded; follow = false;
                    _j.AddIssue(ChildPath(dirPath, e), IssueKind.Excluded, 0, L.T("Đã loại trừ theo cài đặt.", "Excluded by settings."), 0, 0, noNode: true);
                }
                e.Descend = follow; e.Flags = f;
                return;
            }

            // ---- file ----
            long eof = e.Eof, alloc = e.Alloc, adsLogical = 0, adsAlloc = 0, adsRes = 0; ushort links = 1;
            bool ntfs = vol.IsNtfs; long cluster = vol.ClusterSize;
            if (!_j._opt.FastMode)
            {
                int st = NtApi.OpenRelative(dirHandle, new ReadOnlySpan<char>(_t.NameChunk(e.NameOff >> 20), e.NameOff & 0xFFFFF, e.NameLen),
                    NtApi.FILE_READ_ATTRIBUTES | NtApi.SYNCHRONIZE, NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var fh);
                if (st >= 0)
                {
                    try
                    {
                        Begin(fh);
                        NtApi.FILE_STANDARD_INFORMATION si;
                        int s2 = NtApi.NtQueryInformationFile(fh, out _, &si, (uint)sizeof(NtApi.FILE_STANDARD_INFORMATION), NtApi.FileStandardInformation);
                        if (s2 >= 0)
                        {
                            if (si.DeletePending != 0) { e.Skip = true; return; }
                            eof = si.EndOfFile; alloc = si.AllocationSize; links = (ushort)Math.Min(si.NumberOfLinks, ushort.MaxValue);
                            if (_j._opt.IncludeAds) QueryAds(fh, ntfs, cluster, ref adsLogical, ref adsAlloc, ref adsRes, ref f);
                        }
                        else f |= NodeFlags.Estimated;
                        End();
                    }
                    finally { NtApi.NtClose(fh); }
                }
                else if (st == NtApi.STATUS_OBJECT_NAME_NOT_FOUND || st == NtApi.STATUS_OBJECT_PATH_NOT_FOUND || st == NtApi.STATUS_DELETE_PENDING || st == NtApi.STATUS_NO_SUCH_FILE)
                {
                    e.Skip = true;
                    _j.AddIssue(ChildPath(dirPath, e), IssueKind.Changed, st, L.T("Mục biến mất khi đang quét.", "Item disappeared during the scan."), 0, 0, noNode: true);
                    return;
                }
                else
                {
                    // file hệ thống (pagefile.sys…) hoặc bị từ chối: dùng số trong danh sách thư mục, gắn nhãn ước lượng
                    f |= NodeFlags.Estimated;
                    if (st == NtApi.STATUS_ACCESS_DENIED || st == NtApi.STATUS_SHARING_VIOLATION) { }
                    else _j.AddIssue(ChildPath(dirPath, e), IssueKind.EstimatedFromListing, st, NtApi.StatusText(st), 0, 0, noNode: true);
                }
            }
            SplitResident(alloc, ntfs, cluster, out long mainClusters, out long mainRes);
            e.Logical = eof + adsLogical;
            e.Allocated = mainClusters + adsAlloc;
            e.Resident = mainRes + adsRes;
            if (mainRes > 0 || adsRes > 0) f |= NodeFlags.Resident;
            e.Links = links; if (links > 1) f |= NodeFlags.MultiLink;
            if (cloudAttr && e.Allocated == 0 && e.Resident == 0 && e.Logical > 0) f |= NodeFlags.CloudOnly;
            e.Flags = f;
        }

        string ChildPath(string dirPath, in Ent e)
        {
            var nm = new ReadOnlySpan<char>(_t.NameChunk(e.NameOff >> 20), e.NameOff & 0xFFFFF, e.NameLen);
            return dirPath.EndsWith('\\') ? dirPath + nm.ToString() : dirPath + "\\" + nm.ToString();
        }

        void QueryAds(IntPtr fh, bool ntfs, long cluster, ref long logical, ref long alloc, ref long res, ref NodeFlags f)
        {
            byte* buf = _infoBuf; int len = _infoLen;
            int st = NtApi.NtQueryInformationFile(fh, out _, buf, (uint)len, NtApi.FileStreamInformation);
            while (st == NtApi.STATUS_BUFFER_OVERFLOW && len < 8 * 1024 * 1024)
            {
                len *= 4;
                if (_bigBuf != null) NativeMemory.Free(_bigBuf);
                _bigBuf = (byte*)NativeMemory.Alloc((nuint)len); _bigLen = len; buf = _bigBuf;
                st = NtApi.NtQueryInformationFile(fh, out _, buf, (uint)len, NtApi.FileStreamInformation);
            }
            if (st < 0) return;
            for (byte* p = buf; ;)
            {
                uint next = *(uint*)p, nameLen = *(uint*)(p + 4); long ssz = *(long*)(p + 8), salloc = *(long*)(p + 16);
                var sn = new ReadOnlySpan<char>(p + 24, (int)nameLen / 2);
                if (!sn.SequenceEqual("::$DATA"))
                {
                    logical += ssz;
                    SplitResident(salloc, ntfs, cluster, out long c, out long r);
                    alloc += c; res += r; f |= NodeFlags.HasAds;
                }
                if (next == 0) break; p += next;
            }
        }

        void Commit(DirTask task, string path, long dirAlloc, long dirRes)
        {
            var span = CollectionsMarshal.AsSpan(_ents);
            long sumL = 0, sumA = 0, sumR = 0; int files = 0, dirs = 0;

            // thống kê loại + top (trên mọi file đo được, kể cả sau này bị gom trong Chế độ gọn)
            int kept = 0;
            for (int i = 0; i < span.Length; i++)
            {
                ref var e = ref span[i];
                if (e.Skip) continue;
                if (e.IsDir) { dirs++; kept++; continue; }
                files++; kept++;
                sumL += e.Logical; sumA += e.Allocated; sumR += e.Resident;
                AddType(ref e);
            }

            // Chế độ gọn (ER-07): giữ K file lớn nhất mỗi thư mục, gom phần còn lại vào một nút ảo — tổng vẫn đúng.
            bool compact = _j._compact;
            if (!compact && _t.ApproxBytes > _j._opt.MemoryLimitBytes) { _j._compact = compact = true; }
            int agg = -1;
            Ent aggEnt = default;
            if (compact && files > _j._opt.CompactKeepPerDir)
            {
                var idx = new List<(long Alloc, int I)>(files);
                for (int i = 0; i < span.Length; i++) if (!span[i].Skip && !span[i].IsDir && span[i].Links <= 1) idx.Add((span[i].Allocated, i));
                if (idx.Count > _j._opt.CompactKeepPerDir)
                {
                    idx.Sort((x, y) => y.Alloc.CompareTo(x.Alloc));
                    long aL = 0, aA = 0, aR = 0; int aN = 0;
                    for (int k = _j._opt.CompactKeepPerDir; k < idx.Count; k++)
                    {
                        ref var e = ref span[idx[k].I];
                        aL += e.Logical; aA += e.Allocated; aR += e.Resident; aN++; e.Skip = true;
                    }
                    var label = $"<{aN:N0} file khác>";
                    aggEnt = new Ent { NameOff = _nw.Write(label), NameLen = (ushort)label.Length, Logical = aL, Allocated = aA, Resident = aR, FileCnt = aN, Flags = NodeFlags.Aggregate };
                    agg = 1; kept = kept - aN + 1;
                }
            }

            int n = kept;
            int first = n > 0 ? _t.AllocRange(n) : 0;
            int cur = first;
            var childTasks = new List<int>();
            for (int i = 0; i < span.Length; i++)
            {
                ref var e = ref span[i];
                if (e.Skip) continue;
                WriteNode(ref _t[cur], ref e, task.Node);
                if (e.IsDir) { if (e.Descend) { childTasks.Add(cur); _t[cur].Flags |= NodeFlags.Pending; } }
                else
                {
                    if (e.Links > 1) _cands.Add(new HardLinkCand(task.Serial, e.IdLo, e.IdHi, cur, e.Logical, e.Allocated, e.Resident, e.Links));
                    ConsiderTop(cur, e.Logical, e.Allocated);
                }
                cur++;
            }
            if (agg == 1) { WriteNode(ref _t[cur], ref aggEnt, task.Node); _t[cur].FileCount = aggEnt.FileCnt; cur++; }

            ref var dn = ref _t[task.Node];
            dn.OwnAllocated = dirAlloc; dn.OwnResident = dirRes;
            dn.FirstChild = first; Volatile.Write(ref dn.ChildCount, n);
            AddUp(task.Node, sumL, sumA + dirAlloc, sumR + dirRes, files, dirs);
            Interlocked.Add(ref _j._files, files); Interlocked.Add(ref _j._dirs, dirs);
            Interlocked.Add(ref _j._logical, sumL); Interlocked.Add(ref _j._allocated, sumA + dirAlloc);

            foreach (var c in childTasks)
            {
                Interlocked.Increment(ref _j._pending);
                _j._queue.Add(new DirTask(c, task.Serial));
            }
        }

        void WriteNode(ref Node nd, ref Ent e, int parent)
        {
            nd.Parent = parent; nd.NameOffset = e.NameOff; nd.NameLen = e.NameLen; nd.Flags = e.Flags; nd.LinkCount = e.Links;
            nd.MTime = e.MTime;
            if (!e.IsDir)
            {
                nd.OwnLogical = nd.Logical = e.Logical; nd.OwnAllocated = nd.Allocated = e.Allocated; nd.OwnResident = nd.Resident = e.Resident;
            }
        }

        void AddUp(int node, long l, long a, long r, int files, int dirs)
        {
            for (int i = node; i >= 0; i = _t[i].Parent)
            {
                ref var n = ref _t[i];
                Interlocked.Add(ref n.Logical, l); Interlocked.Add(ref n.Allocated, a); Interlocked.Add(ref n.Resident, r);
                Interlocked.Add(ref n.FileCount, files); Interlocked.Add(ref n.DirCount, dirs);
            }
        }

        void ConsiderTop(int node, long logical, long alloc)
        {
            if (_top.Count >= _topKeep)
            {
                if (!_top.TryPeek(out _, out var min) || alloc <= min) return;
                _top.Dequeue();
            }
            _top.Enqueue((node, logical), alloc);
        }

        void AddType(ref Ent e)
        {
            var name = new ReadOnlySpan<char>(_t.NameChunk(e.NameOff >> 20), e.NameOff & 0xFFFFF, e.NameLen);
            int dot = name.LastIndexOf('.');
            var ext = dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : default;
            if (ext.Length > 24) ext = ext[..24];
            if (!_typeLookup.TryGetValue(ext, out var acc)) { acc = new TypeAcc(); _typeLookup[ext] = acc; }
            acc.Count++; acc.Logical += e.Logical; acc.Allocated += e.Allocated;
        }
    }

    // ===================================================================================== lỗi
    void DirFailure(int node, string path, int status, bool timedOut, bool partial = false)
    {
        NodeFlags flag; IssueKind kind; string msg = NtApi.StatusText(status);
        if (timedOut || status == NtApi.STATUS_IO_TIMEOUT || status == NtApi.STATUS_CANCELLED) { flag = NodeFlags.TimedOut; kind = IssueKind.NetworkTimeout; msg = $"Hết thời gian chờ ({_opt.NetworkTimeoutSec}s). " + msg; }
        else if (status == NtApi.STATUS_ACCESS_DENIED) { flag = NodeFlags.AccessDenied; kind = IssueKind.AccessDenied; }
        else if (status is NtApi.STATUS_OBJECT_NAME_NOT_FOUND or NtApi.STATUS_OBJECT_PATH_NOT_FOUND or NtApi.STATUS_NO_SUCH_FILE or NtApi.STATUS_DELETE_PENDING or NtApi.STATUS_NOT_A_DIRECTORY)
        { flag = NodeFlags.Changed; kind = IssueKind.Changed; }
        else if (status is NtApi.STATUS_NO_SUCH_DEVICE or NtApi.STATUS_VOLUME_DISMOUNTED or NtApi.STATUS_DEVICE_NOT_CONNECTED or NtApi.STATUS_NETWORK_UNREACHABLE or NtApi.STATUS_BAD_NETWORK_PATH or NtApi.STATUS_NETWORK_NAME_DELETED)
        { flag = NodeFlags.Lost; kind = IssueKind.VolumeLost; }
        else { flag = NodeFlags.Unknown; kind = IssueKind.Other; }
        AddIssue(path, kind, status, msg + (partial ? " (liệt kê dở dang)" : ""), node, flag);
    }

    void AddIssue(string path, IssueKind kind, int status, string msg, int node, NodeFlags flag, bool noNode = false)
    {
        _issues.Enqueue(new ScanIssue(path, kind, status, msg));
        if (!noNode && node > 0) Interlocked.Or(ref _tree[node].FlagsRaw, (uint)flag);
        if (kind is IssueKind.AccessDenied or IssueKind.Changed or IssueKind.VolumeLost or IssueKind.NetworkTimeout or IssueKind.Other)
            Interlocked.Increment(ref _problems);
    }
}
