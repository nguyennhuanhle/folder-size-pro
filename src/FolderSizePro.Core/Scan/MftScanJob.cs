using System.Diagnostics;
using System.Runtime.InteropServices;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;
using Microsoft.Win32.SafeHandles;

namespace FolderSizePro.Scan;

/// <summary>Số liệu riêng của chế độ MFT cho bảng Đối chiếu (Q9): đối chiếu $Bitmap ↔ tổng cluster của mọi attribute.</summary>
public sealed class MftExtras
{
    public long BitmapUsedBytes { get; set; }
    public long AttributeBytes { get; set; }
    public int OrphanRecords { get; set; }
    public long OrphanBytes { get; set; }
    public int CorruptRecords { get; set; }
    public long Records { get; set; }
    public long MftBytes { get; set; }
    /// <summary>Cluster của luồng nội bộ Cloud Files (đã nằm trong Trên đĩa của từng file; chế độ thường không thấy vì cldflt giấu).</summary>
    public long CloudInternalBytes { get; set; }
    public long CloudInternalFiles { get; set; }
    /// <summary>Cluster của attribute phi cư trú ngoài $DATA / $INDEX_ALLOCATION (ngoài metafile) — có trong $Bitmap, không thuộc nút nào trong cây.</summary>
    public long OtherAttributeBytes { get; set; }
}

/// <summary>
/// Chế độ quét MFT (admin, chỉ NTFS, gốc ổ — plan 3.2): đọc thẳng $MFT bằng I/O tuần tự lớn, parse từng bản ghi (fixup, tên, luồng,
/// run list), dựng cây rồi dùng chung HardLinkResolver / Rollup / TypeStats / Reconcile của chế độ thường để hai chế độ cùng
/// MỘT định nghĩa số liệu. Chỉ đọc metadata (Q10): dữ liệu thường trú bị bỏ ngay sau khi lấy độ dài (KT-22).
/// </summary>
public static unsafe class MftScanJobFactory
{
    public static IScanJob? TryStart(IReadOnlyList<string> roots, ScanOptions opt, List<string> notes, out string reason)
    {
        var job = new MftScanJob(roots, opt);
        if (!job.Preflight(out reason)) { job.DisposeHandles(); return null; }
        job.Begin();
        return job;
    }
}

internal sealed unsafe class MftScanJob : IScanJob
{
    sealed class Vol
    {
        public RootInfoLite Root = null!; public SafeFileHandle Handle = null!; public VolumeInfo Info = null!;
        public int BytesPerSector, Cluster, Frs; public long MftValidLen, TotalClusters, FreeClusters;
        public List<(long Vcn, long Lcn, long Len)> MftRuns = new();
    }
    sealed record RootInfoLite(string Path);

    readonly IReadOnlyList<string> _roots; readonly ScanOptions _opt;
    readonly ScanTree _tree = new(); readonly ScanResult _result;
    readonly List<Vol> _vols = new();
    readonly CancellationTokenSource _cts = new(); readonly ManualResetEventSlim _gate = new(true);
    readonly Stopwatch _sw = new();
    long _files, _dirs, _logical, _allocated, _records, _totalRecords; volatile string _current = ""; volatile bool _finishing;

    public MftScanJob(IReadOnlyList<string> roots, ScanOptions opt)
    {
        _roots = roots; _opt = opt; _result = new ScanResult { Tree = _tree, Options = opt, ModeUsed = "Mft" };
    }

    public ScanTree Tree => _tree;
    public ScanResult Result => _result;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool IsPaused => !_gate.IsSet;
    public bool CompactMode => false;
    public void Pause() => _gate.Reset();
    public void Resume() => _gate.Set();
    public void Cancel() { _cts.Cancel(); _gate.Set(); }

    public ScanProgress Snapshot()
    {
        var el = _sw.Elapsed; long f = Interlocked.Read(ref _files);
        return new ScanProgress(f, Interlocked.Read(ref _dirs), Interlocked.Read(ref _logical), Interlocked.Read(ref _allocated), 0, _current, el,
            el.TotalSeconds > 0 ? Interlocked.Read(ref _records) / el.TotalSeconds : 0, IsPaused, false, _finishing);
    }

    public void DisposeHandles() { foreach (var v in _vols) try { v.Handle?.Dispose(); } catch { } _vols.Clear(); }

    // ================================================================================== preflight (ER-20)
    public bool Preflight(out string reason)
    {
        reason = "";
        foreach (var r in _roots)
        {
            var info = VolumeInfo.For(r);
            string letter = info.DriveLetter;
            if (!info.IsNtfs || letter == "") { reason = $"{r} không phải ổ NTFS có ký tự ổ"; return false; }
            var h = NtApi.CreateFileW(@"\\." + "\\" + letter, NtApi.GENERIC_READ, 7, IntPtr.Zero, NtApi.OPEN_EXISTING, NtApi.FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);
            if (h == NtApi.INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                reason = err == 5 ? $"không mở được ổ {letter} ở chế độ thô (Win32 5 — thiếu quyền Administrator, hoặc phần mềm bảo mật chặn)"
                       : err == 32 ? $"ổ {letter} đang bị khoá độc quyền (Win32 32)"
                       : $"không mở được ổ {letter} ở chế độ thô (Win32 {err} — có thể ổ đang BitLocker khoá)";
                return false;
            }
            var v = new Vol { Info = info, Root = new RootInfoLite(r), Handle = new SafeFileHandle(h, true) };
            byte* data = stackalloc byte[128];
            if (!NtApi.DeviceIoControl(h, 0x90064, null, 0, data, 128, out var ret, IntPtr.Zero) || ret < 96)
            { reason = $"FSCTL_GET_NTFS_VOLUME_DATA thất bại (Win32 {Marshal.GetLastWin32Error()}) trên {letter}"; v.Handle.Dispose(); return false; }
            v.TotalClusters = *(long*)(data + 16); v.FreeClusters = *(long*)(data + 24);
            v.BytesPerSector = *(int*)(data + 40); v.Cluster = *(int*)(data + 44); v.Frs = *(int*)(data + 48); v.MftValidLen = *(long*)(data + 56);
            long mftLcn = *(long*)(data + 64);
            if (v.Frs is < 512 or > 16384 || v.Cluster < 512 || v.BytesPerSector < 512 || v.MftValidLen <= 0) { reason = "tham số NTFS bất thường"; v.Handle.Dispose(); return false; }

            // đọc bản ghi 0 ($MFT) → run list của chính $MFT
            int read = (int)((v.Frs + v.BytesPerSector - 1) / v.BytesPerSector * v.BytesPerSector);
            byte* buf = (byte*)NativeMemory.AlignedAlloc((nuint)read, 4096);
            try
            {
                int got = RandomAccess.Read(v.Handle, new Span<byte>(buf, read), mftLcn * v.Cluster);
                if (got < v.Frs || !MftParser.ApplyFixups(buf, v.Frs)) { reason = "bản ghi $MFT (0) không hợp lệ"; v.Handle.Dispose(); return false; }
                var runs = MftParser.FindRuns(buf, v.Frs, MftParser.AttrData, out long dataSize);
                if (runs == null || runs.Count == 0) { reason = "không đọc được run list của $MFT"; v.Handle.Dispose(); return false; }
                long covered = runs.Sum(x => x.Len) * v.Cluster;
                if (covered < v.MftValidLen) { reason = "run list của $MFT nằm ở bản ghi mở rộng (MFT phân mảnh nặng) — chưa hỗ trợ"; v.Handle.Dispose(); return false; }
                v.MftRuns = runs;
            }
            finally { NativeMemory.AlignedFree(buf); }
            _vols.Add(v);
        }
        return true;
    }

    public void Begin()
    {
        Completion = Task.Factory.StartNew(Run, TaskCreationOptions.LongRunning);
    }

    // ================================================================================== chạy
    void Run()
    {
        _result.StartedUtc = DateTime.UtcNow; _sw.Start();
        var cands = new List<HardLinkCand>();
        var all = new List<(RootInfo Info, MftExtras Extras)>();
        try
        {
            int firstRoot = _tree.AllocRange(_vols.Count);
            _tree[0].FirstChild = firstRoot; _tree[0].ChildCount = _vols.Count;
            var nw = new NameWriter(_tree);
            for (int vi = 0; vi < _vols.Count; vi++)
            {
                var v = _vols[vi]; _totalRecords += v.MftValidLen / v.Frs;
                string path = v.Root.Path.TrimEnd('\\') + "\\";
                ref var rn = ref _tree[firstRoot + vi];
                rn.Parent = 0; rn.Flags = NodeFlags.Dir; rn.NameOffset = nw.Write(path); rn.NameLen = (ushort)path.Length;
                var ri = new RootInfo { Node = firstRoot + vi, Path = path, Volume = v.Info, IsVolumeRoot = true };
                _result.Roots.Add(ri);
                var extras = ProcessVolume(v, firstRoot + vi, cands, nw);
                all.Add((ri, extras));
            }
        }
        catch (OperationCanceledException) { _result.Completeness = Completeness.Partial; _result.Notes.Add("Phiên quét MFT bị huỷ — kết quả dở dang."); }
        catch (Exception ex)
        {
            _result.Completeness = Completeness.Partial;
            _result.Notes.Add("Quét MFT gặp lỗi: " + ex.Message + " — kết quả dở dang; hãy chạy lại bằng chế độ thường.");
            Storage.Log.Error("MFT lỗi", ex);
        }
        finally { DisposeHandles(); }
        _finishing = true;
        _result.HardLinkCands = cands;
        foreach (var (ri, ex) in all) _result.MftExtras[ri.Path.TrimEnd('\\')] = ex;
        Finalizer.RunRescan(_result, new List<HardLinkCand>());
        foreach (var (_, ex) in all)
            if (ex.OrphanRecords > 0 || ex.CorruptRecords > 0)
                _result.Notes.Add($"MFT: {ex.OrphanRecords:N0} bản ghi mồ côi và {ex.CorruptRecords:N0} bản ghi hỏng không dựng được đường dẫn — tính vào 'Chưa giải thích' (ER-21).");
        if (CloudNote() is { Length: > 0 } cn) _result.Notes.Add(cn);
        if (_reparseReads + _reparseHeuristic > 0) _result.Notes.Add($"MFT: đọc thẻ reparse phi cư trú của {_reparseReads:N0} thư mục" + (_reparseHeuristic > 0 ? $", {_reparseHeuristic:N0} thư mục suy từ cờ đám mây" : "") + ".");
        _result.Notes.Add("Chế độ MFT đọc ảnh chụp trên đĩa của $MFT; thay đổi mới nhất chưa được NTFS ghi xuống có thể chưa thấy (Q8).");
        _sw.Stop(); _result.CompletedUtc = DateTime.UtcNow;
    }

    // ----------------------------------------------------------------------------- đọc + parse $MFT của một ổ
    MftExtras ProcessVolume(Vol v, int rootNode, List<HardLinkCand> cands, NameWriter nw)
    {
        int frs = v.Frs; long totalRecords = v.MftValidLen / frs;
        var recs = new MftRec[totalRecords];
        const int UnitRecords = 1024;
        int unitCount = (int)((totalRecords + UnitRecords - 1) / UnitRecords);
        if (unitCount > ushort.MaxValue) throw new NotSupportedException("MFT quá lớn (> 67 triệu bản ghi)");
        var units = new MftChunkOut[unitCount];
        _current = $"Đọc $MFT của {v.Info.DriveLetter} …";

        // đọc tuần tự theo từng run; mỗi lần ~8 MiB, parse song song, đọc trước khối kế tiếp (double buffer)
        const int ReadSize = 8 * 1024 * 1024;
        int readSize = ReadSize / v.BytesPerSector * v.BytesPerSector;
        var bufA = (byte*)NativeMemory.AlignedAlloc((nuint)readSize, 4096); var bufB = (byte*)NativeMemory.AlignedAlloc((nuint)readSize, 4096);
        byte* carry = (byte*)NativeMemory.AlignedAlloc((nuint)frs, 4096); int carryLen = 0;
        long recordNo = 0; long bytesDone = 0;
        try
        {
            // dựng danh sách đoạn đọc (offset, length) bao phủ đúng MftValidDataLength
            var segs = new List<(long Off, long Len)>(); long remain = v.MftValidLen;
            foreach (var run in v.MftRuns)
            {
                if (remain <= 0) break;
                long len = Math.Min(run.Len * v.Cluster, remain);
                if (run.Lcn >= 0) segs.Add((run.Lcn * v.Cluster, len));
                else segs.Add((-1, len));                                   // run thưa trong $MFT: coi như bản ghi trống
                remain -= len;
            }
            var pending = new Queue<(long Off, long Len)>();
            foreach (var (off, len) in segs)
                for (long p = 0; p < len; p += readSize) pending.Enqueue((off < 0 ? -1 : off + p, Math.Min(readSize, len - p)));

            Task<int>? prefetch = null; byte* cur = bufA, next = bufB; (long Off, long Len) curSeg = default;
            int ReadSeg((long Off, long Len) s, byte* dst)
            {
                if (s.Off < 0) { new Span<byte>(dst, (int)s.Len).Clear(); return (int)s.Len; }
                int want = (int)((s.Len + v.BytesPerSector - 1) / v.BytesPerSector * v.BytesPerSector);
                int got = RandomAccess.Read(v.Handle, new Span<byte>(dst, want), s.Off);
                return Math.Min(got, (int)s.Len);
            }
            if (pending.Count > 0) { curSeg = pending.Dequeue(); }
            int curGot = pending.Count >= 0 && curSeg.Len > 0 ? ReadSeg(curSeg, cur) : 0;
            while (curSeg.Len > 0)
            {
                _cts.Token.ThrowIfCancellationRequested(); _gate.Wait(_cts.Token);
                // khởi động đọc khối kế
                (long Off, long Len) nextSeg = pending.Count > 0 ? pending.Dequeue() : default;
                byte* nb = next; var ns = nextSeg;
                prefetch = ns.Len > 0 ? Task.Run(() => ReadSeg(ns, nb)) : null;

                // ghép phần dư của khối trước + khối hiện tại thành các bản ghi nguyên
                byte* work = cur; int workLen = curGot;
                if (carryLen > 0)
                {
                    int need = frs - carryLen;
                    if (workLen >= need)
                    {
                        Buffer.MemoryCopy(work, carry + carryLen, need, need);
                        ParseOne(carry, recordNo++, recs, units, UnitRecords, frs, v.Cluster);
                        work += need; workLen -= need; carryLen = 0;
                    }
                }
                int whole = workLen / frs;
                long firstRec = recordNo;
                // parse song song theo "đơn vị" 1024 bản ghi để mỗi đơn vị có ChunkOut riêng
                if (whole > 0)
                {
                    int firstUnit = (int)(firstRec / UnitRecords), lastUnit = (int)((firstRec + whole - 1) / UnitRecords);
                    byte* w = work;
                    Parallel.For(firstUnit, lastUnit + 1, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount), CancellationToken = _cts.Token }, u =>
                    {
                        long uStart = Math.Max(firstRec, (long)u * UnitRecords), uEnd = Math.Min(firstRec + whole, (long)(u + 1) * UnitRecords);
                        var co = LockedUnit(units, u);
                        lock (co)
                        {
                            for (long rn = uStart; rn < uEnd; rn++)
                            {
                                byte* rp = w + (rn - firstRec) * frs;
                                ParseInto(rp, rn, recs, co, (ushort)u, frs, v.Cluster);
                            }
                        }
                    });
                    recordNo += whole; work += (long)whole * frs; workLen -= whole * frs;
                }
                if (workLen > 0) { Buffer.MemoryCopy(work, carry + carryLen, workLen, workLen); carryLen += workLen; }
                bytesDone += curSeg.Len; Interlocked.Exchange(ref _records, recordNo);
                _current = $"Đọc $MFT của {v.Info.DriveLetter}: {recordNo:N0} / {totalRecords:N0} bản ghi";

                if (prefetch != null) { curGot = prefetch.Result; byte* swap = cur; cur = next; next = swap; curSeg = nextSeg; } else curSeg = default;
            }
        }
        finally { NativeMemory.AlignedFree(bufA); NativeMemory.AlignedFree(bufB); NativeMemory.AlignedFree(carry); }

        return BuildTree(v, rootNode, recs, units, recordNo, cands, nw);
    }

    static MftChunkOut LockedUnit(MftChunkOut[] units, int u)
    {
        var co = Volatile.Read(ref units[u]);
        if (co != null) return co;
        var fresh = new MftChunkOut();
        return Interlocked.CompareExchange(ref units[u], fresh, null) ?? fresh;
    }

    void ParseOne(byte* rp, long rn, MftRec[] recs, MftChunkOut[] units, int unitRecords, int frs, int cluster)
    {
        int u = (int)(rn / unitRecords); var co = LockedUnit(units, u);
        lock (co) ParseInto(rp, rn, recs, co, (ushort)u, frs, cluster);
    }

    static void ParseInto(byte* rp, long rn, MftRec[] recs, MftChunkOut co, ushort unit, int frs, int cluster)
    {
        // bản ghi toàn 0 (chưa dùng) → bỏ qua nhanh
        if (*(uint*)rp == 0) { recs[rn] = default; recs[rn].NextExt = -1; return; }
        if (!MftParser.ApplyFixups(rp, frs)) { recs[rn] = default; recs[rn].NextExt = -1; recs[rn].Corrupt = (*(uint*)rp == 0x454C4946); return; }
        MftParser.Parse(rp, frs, cluster, co, unit, ref recs[rn]);
    }

    // ----------------------------------------------------------------------------- dựng cây
    MftExtras BuildTree(Vol v, int rootNode, MftRec[] recs, MftChunkOut[] units, long nRecs, List<HardLinkCand> cands, NameWriter nw)
    {
        _current = $"Dựng cây thư mục của {v.Info.DriveLetter}…";
        var extras = new MftExtras { Records = nRecs, MftBytes = v.MftValidLen };
        // 1) nối bản ghi mở rộng vào bản ghi gốc
        long attrBytes = 0;
        for (long i = 0; i < nRecs; i++)
        {
            ref var r = ref recs[i];
            if (r.Corrupt) extras.CorruptRecords++;
            if (r.State == 0) continue;
            attrBytes += r.IndexClusters + r.OtherClusters;
            if (r.State == 2)
            {
                long b = (long)(r.BaseRef & 0xFFFFFFFFFFFFUL); ushort bseq = (ushort)(r.BaseRef >> 48);
                if (b < nRecs && recs[b].State == 1 && recs[b].Seq == bseq)
                {
                    r.NextExt = recs[b].NextExt; recs[b].NextExt = (int)i;
                    if (recs[b].ReparseTag == 0 && r.ReparseTag != 0) recs[b].ReparseTag = r.ReparseTag;
                }
                else { r.State = 0; }
            }
        }
        // cộng cluster của mọi luồng $DATA (mọi bản ghi) vào tổng attribute
        foreach (var co in units) if (co != null) foreach (var s in co.Streams) attrBytes += s.Clusters;
        extras.AttributeBytes = attrBytes;
        // cluster của attribute phi cư trú ngoài luồng dữ liệu / index ($REPARSE_POINT của placeholder, $ATTRIBUTE_LIST, $BITMAP thư mục, $EA…):
        // có thật trong $Bitmap nhưng không thuộc file/thư mục nào trong cây (trừ metafile 0–11, đã tính vào chính chúng)
        long otherAttr = 0;
        for (long i = 0; i < nRecs; i++)
        {
            ref var r = ref recs[i]; if (r.State == 0 || r.OtherClusters == 0) continue;
            long b = r.State == 1 ? i : (long)(r.BaseRef & 0xFFFFFFFFFFFFUL);
            if (b >= 12 || b == 5) otherAttr += r.OtherClusters;     // gốc (5) chỉ được tính index, không tính attribute khác
        }
        extras.OtherAttributeBytes = otherAttr;

        // 2) bảng liên kết cha→con
        var childCount = new int[nRecs + 1];
        int totalLinks = 0;
        var nameCount = new ushort[nRecs];
        for (long i = 0; i < nRecs; i++)
        {
            ref var r = ref recs[i]; if (r.State != 1) continue;
            ForEachName(recs, units, i, (ref MftName n, MftChunkOut co, ushort unit) =>
            {
                if (nameCount[i] < ushort.MaxValue) nameCount[i]++;
                long p = (long)(n.ParentRef & 0xFFFFFFFFFFFFUL);
                if (p >= nRecs || (i == 5 && p == 5)) return;
                childCount[p]++; totalLinks++;
            });
            // LinkCount trong header đếm cả tên DOS 8.3 riêng → dùng số tên Win32/POSIX thật (giống NumberOfLinks của chế độ thường)
            r.Links = nameCount[i];
        }
        var start = new int[nRecs + 2];
        for (long i = 0; i <= nRecs; i++) start[i + 1] = start[i] + childCount[i];
        var fill = new int[nRecs + 1];
        var linkChild = new int[totalLinks]; var linkNameUnit = new ushort[totalLinks]; var linkNameOff = new int[totalLinks]; var linkNameLen = new byte[totalLinks]; var linkParentSeq = new ushort[totalLinks];
        for (long i = 0; i < nRecs; i++)
        {
            ref var r = ref recs[i]; if (r.State != 1) continue;
            ForEachName(recs, units, i, (ref MftName n, MftChunkOut co, ushort unit) =>
            {
                long p = (long)(n.ParentRef & 0xFFFFFFFFFFFFUL);
                if (p >= nRecs || (i == 5 && p == 5)) return;
                int k = start[p] + fill[p]++;
                linkChild[k] = (int)i; linkNameUnit[k] = unit; linkNameOff[k] = n.PoolOffset; linkNameLen[k] = n.Len; linkParentSeq[k] = (ushort)(n.ParentRef >> 48);
            });
        }

        // 2b) thư mục reparse có $REPARSE_POINT phi cư trú (placeholder đám mây lớn): đọc thẻ thật từ cluster đầu của nó
        ResolveNonResidentReparseTags(v, recs, nRecs);

        // 3) BFS từ bản ghi 5 (thư mục gốc)
        var visited = new BitArrayLite(nRecs);
        var queue = new Queue<(long Rec, int Node)>(); queue.Enqueue((5, rootNode)); visited.Set(5);
        // thông tin gốc
        ref var rootRec = ref recs[5]; ref var rn0 = ref _tree[rootNode];
        rn0.MTime = rootRec.MTime; rn0.OwnAllocated = IndexOf(recs, 5); rn0.Allocated = rn0.OwnAllocated;
        long placed = 0, orphanBytes = 0;
        bool anyExcl = _opt.ExcludedPaths.Count > 0;
        var excl = new HashSet<string>(_opt.ExcludedPaths.Select(p => { try { p = Path.GetFullPath(p); } catch { } return p.Length > 3 ? p.TrimEnd('\\') : p; }), StringComparer.OrdinalIgnoreCase);
        var serial = v.Info.Serial;
        int sinceCheck = 0;

        while (queue.Count > 0)
        {
            if (++sinceCheck > 2048) { sinceCheck = 0; _cts.Token.ThrowIfCancellationRequested(); _gate.Wait(_cts.Token); }
            var (rec, node) = queue.Dequeue();
            ref var dr = ref recs[rec];
            int s0 = start[rec], s1 = start[rec + 1];
            // chỉ giữ liên kết có cha hợp lệ (sequence khớp)
            int n = 0; for (int k = s0; k < s1; k++) { var c = linkChild[k]; if (linkParentSeq[k] == dr.Seq || rec == 5) n++; }
            long sumL = 0, sumA = 0, sumR = 0; int files = 0, dirs = 0;
            if (n == 0) { _tree[node].ChildCount = 0; continue; }
            int first = _tree.AllocRange(n); int idx = 0;
            for (int k = s0; k < s1; k++)
            {
                if (!(linkParentSeq[k] == dr.Seq || rec == 5)) continue;
                int c = linkChild[k]; ref var cr = ref recs[c];
                ref var nd = ref _tree[first + idx]; idx++;
                var co = units[linkNameUnit[k]];
                nd.Parent = node; nd.NameOffset = nw.Write(new ReadOnlySpan<char>(co.Pool, linkNameOff[k], linkNameLen[k])); nd.NameLen = linkNameLen[k];
                nd.MTime = cr.MTime;
                var f = FlagsFor(cr);
                bool isDir = (cr.Flags & 1) != 0;
                if (isDir)
                {
                    f |= NodeFlags.Dir; dirs++;
                    nd.OwnAllocated = IndexOf(recs, c) + (c < 12 ? OtherOf(recs, c) : 0);
                    sumA += nd.OwnAllocated;
                    bool reparseDir = (cr.FileAttr & NtApi.ATTR_REPARSE_POINT) != 0 && !NtApi.IsCloudTag(cr.ReparseTag);
                    bool seen = visited.Get(c); visited.Set(c);
                    bool follow = !reparseDir && !seen;
                    if (reparseDir) f |= NodeFlags.Reparse | (cr.ReparseTag == NtApi.IO_REPARSE_TAG_MOUNT_POINT ? NodeFlags.MountPoint : cr.ReparseTag == NtApi.IO_REPARSE_TAG_SYMLINK ? NodeFlags.Symlink : NodeFlags.OtherReparse);
                    if (follow && anyExcl) { string full = PathOf(node, nw, co.Pool, linkNameOff[k], linkNameLen[k]); if (excl.Contains(full)) { f |= NodeFlags.Excluded; follow = false; } }
                    nd.Flags = f; nd.OwnAllocated = nd.OwnAllocated; nd.Allocated = nd.OwnAllocated;
                    if (follow) queue.Enqueue((c, first + idx - 1));
                    placed++;
                }
                else
                {
                    files++;
                    Assemble(recs, units, c, ref cr, ref nd, c < 12, out long lg, out long al, out long rs, ref f, collect: true);
                    nd.Flags = f; nd.LinkCount = cr.Links; nd.OwnLogical = nd.Logical = lg; nd.OwnAllocated = nd.Allocated = al; nd.OwnResident = nd.Resident = rs;
                    sumL += lg; sumA += al; sumR += rs;
                    if (cr.Links > 1) cands.Add(new HardLinkCand(serial, ((ulong)cr.Seq << 48) | (uint)c, 0, first + idx - 1, lg, al, rs, cr.Links));
                    visited.Set(c);
                    placed++;
                }
            }
            ref var dn = ref _tree[node];
            dn.FirstChild = first; dn.ChildCount = n;
            Interlocked.Add(ref _files, files); Interlocked.Add(ref _dirs, dirs); Interlocked.Add(ref _logical, sumL); Interlocked.Add(ref _allocated, sumA);
        }
        // 4) mồ côi: bản ghi gốc đang dùng, có tên (hoặc có cluster) nhưng chưa được đặt vào cây
        for (long i = 24; i < nRecs; i++)
        {
            ref var r = ref recs[i]; if (r.State != 1 || visited.Get(i)) continue;
            if (r.NameCount == 0 && r.StreamCount == 0 && r.IndexClusters == 0) continue;
            extras.OrphanRecords++;
            var tmp = default(Node); var ff = NodeFlags.None; var recCopy = r;
            Assemble(recs, units, (int)i, ref recCopy, ref tmp, false, out _, out long al, out _, ref ff);
            orphanBytes += IndexOf(recs, i) + al;
        }
        extras.OrphanBytes = orphanBytes;
        extras.CloudInternalBytes = _cfInternalBytes; extras.CloudInternalFiles = _cfInternalFiles;

        // 5) $Bitmap: số cluster đánh dấu đã cấp phát
        try { extras.BitmapUsedBytes = CountBitmap(v, recs, units) * v.Cluster; } catch (Exception ex) { _result.Notes.Add("Không đọc được $Bitmap: " + ex.Message); }
        return extras;
    }

    string PathOf(int parentNode, NameWriter nw, char[] pool, int off, int len)
    {
        string parent = _tree.FullPath(parentNode);
        string name = new string(pool, off, len);
        return parent.EndsWith('\\') ? parent + name : parent + "\\" + name;
    }

    /// <summary>$INDEX_ALLOCATION của một thư mục lớn có thể nằm rải trên nhiều bản ghi mở rộng ($ATTRIBUTE_LIST) — phải cộng cả chuỗi.</summary>
    int _reparseReads, _reparseHeuristic;

    void ResolveNonResidentReparseTags(Vol v, MftRec[] recs, long nRecs)
    {
        int sector = v.BytesPerSector;
        byte* buf = (byte*)NativeMemory.AlignedAlloc((nuint)Math.Max(sector, 4096), 4096);
        try
        {
            for (long i = 0; i < nRecs; i++)
            {
                ref var r = ref recs[i];
                if (r.State != 1 || (r.Flags & 1) == 0 || (r.FileAttr & NtApi.ATTR_REPARSE_POINT) == 0) continue;
                uint tag = r.ReparseTag;
                if (NtApi.IsCloudTag(tag) || tag == NtApi.IO_REPARSE_TAG_MOUNT_POINT || tag == NtApi.IO_REPARSE_TAG_SYMLINK) continue;
                long lcn = r.ReparseLcn;
                for (int e = r.NextExt; lcn < 0 && e >= 0; e = recs[e].NextExt) lcn = recs[e].ReparseLcn;
                if (lcn >= 0)
                {
                    try
                    {
                        if (RandomAccess.Read(v.Handle, new Span<byte>(buf, sector), lcn * v.Cluster) >= 4) { r.ReparseTag = *(uint*)buf; _reparseReads++; continue; }
                    }
                    catch { }
                }
                // không đọc được: thư mục mang cờ đám mây (RECALL_*/PINNED/UNPINNED) thì coi là thư mục đám mây để vẫn đi vào như chế độ thường
                if ((r.FileAttr & (NtApi.ATTR_RECALL_ON_DATA_ACCESS | NtApi.ATTR_RECALL_ON_OPEN | NtApi.ATTR_PINNED | NtApi.ATTR_UNPINNED)) != 0)
                { r.ReparseTag = NtApi.IO_REPARSE_TAG_CLOUD_MASK; _reparseHeuristic++; }
            }
        }
        finally { NativeMemory.AlignedFree(buf); }
    }

    static long IndexOf(MftRec[] recs, long i)
    {
        long s = recs[i].IndexClusters;
        for (int e = recs[i].NextExt; e >= 0; e = recs[e].NextExt) s += recs[e].IndexClusters;
        return s;
    }

    static long OtherOf(MftRec[] recs, long i)
    {
        long s = recs[i].OtherClusters;
        for (int e = recs[i].NextExt; e >= 0; e = recs[e].NextExt) s += recs[e].OtherClusters;
        return s;
    }

    delegate void NameVisitor(ref MftName n, MftChunkOut co, ushort unit);

    static void ForEachName(MftRec[] recs, MftChunkOut[] units, long i, NameVisitor f)
    {
        ref var r = ref recs[i];
        var co = units[r.Unit]; var arr = co.Names;
        for (int k = 0; k < r.NameCount; k++) { var n = arr[r.NameStart + k]; f(ref n, co, r.Unit); }
        for (int e = r.NextExt; e >= 0; e = recs[e].NextExt)
        {
            var co2 = units[recs[e].Unit]; var arr2 = co2.Names;
            for (int k = 0; k < recs[e].NameCount; k++) { var n = arr2[recs[e].NameStart + k]; f(ref n, co2, recs[e].Unit); }
        }
    }

    static NodeFlags FlagsFor(in MftRec r)
    {
        var f = NodeFlags.None; uint a = r.FileAttr;
        if ((a & NtApi.ATTR_HIDDEN) != 0) f |= NodeFlags.Hidden;
        if ((a & NtApi.ATTR_SYSTEM) != 0) f |= NodeFlags.System;
        if ((a & NtApi.ATTR_COMPRESSED) != 0) f |= NodeFlags.Compressed;
        if ((a & NtApi.ATTR_SPARSE) != 0) f |= NodeFlags.Sparse;
        if ((a & NtApi.ATTR_ENCRYPTED) != 0) f |= NodeFlags.Encrypted;
        if ((a & (NtApi.ATTR_RECALL_ON_DATA_ACCESS | NtApi.ATTR_RECALL_ON_OPEN)) != 0 || NtApi.IsCloudTag(r.ReparseTag)) f |= NodeFlags.Cloud;
        if ((a & NtApi.ATTR_REPARSE_POINT) != 0)
        {
            f |= NodeFlags.Reparse;
            if (r.ReparseTag == NtApi.IO_REPARSE_TAG_WOF) f |= NodeFlags.Wof;
            else if (r.ReparseTag == NtApi.IO_REPARSE_TAG_DEDUP) f |= NodeFlags.Dedup;
            else if (r.ReparseTag == NtApi.IO_REPARSE_TAG_SYMLINK) f |= NodeFlags.Symlink;
            else if (r.ReparseTag == NtApi.IO_REPARSE_TAG_MOUNT_POINT) f |= NodeFlags.MountPoint;
            else if (!NtApi.IsCloudTag(r.ReparseTag)) f |= NodeFlags.OtherReparse;
        }
        return f;
    }

    /// <summary>Gộp luồng của bản ghi gốc + các bản ghi mở rộng: Kích thước, Trên đĩa (cluster thật), Thường trú — cùng quy ước Q1/Q5 với chế độ thường.</summary>
    // Chẩn đoán file đám mây (OneDrive…): bộ lọc Cloud Files báo AllocationSize = 0 và giấu luồng phụ của nó khỏi FileStreamInformation,
    // nên chế độ thường không thấy phần này; ghi lại để đối chiếu hai chế độ (chỉ BFS, đơn luồng).
    long _cloudFiles, _cloudMainCl, _cloudAdsCl, _cloudAdsLg, _cloudReparseCl, _cfInternalBytes, _cfInternalFiles;
    /// <summary>Tiền tố luồng nội bộ của cldflt.sys, ví dụ "${3D0CE612-FDEE-43f7-8ACA-957BEC0CCBA0}.Metadata" (đo trên ổ C: thật, 2026-10-03).</summary>
    internal const string CloudFilesStreamPrefix = "${3D0CE612-FDEE-43f7-8ACA-957BEC0CCBA0}";
    readonly Dictionary<string, (int N, long Lg, long Cl)> _cloudAds = new(StringComparer.Ordinal);

    string CloudNote()
    {
        if (_cloudFiles == 0) return "";
        static string B(long x) => FolderSizePro.Text.ByteFormatter.Format(x);
        var top = _cloudAds.OrderByDescending(p => p.Value.Cl + p.Value.Lg).Take(5)
            .Select(p => $"'{p.Key}' ×{p.Value.N:N0} ({B(p.Value.Lg)} / {B(p.Value.Cl)} trên đĩa)");
        return $"MFT: {_cloudFiles:N0} file đám mây — cluster luồng chính {B(_cloudMainCl)}; luồng phụ {B(_cloudAdsLg)} / {B(_cloudAdsCl)} trên đĩa"
             + (_cloudAds.Count > 0 ? ": " + string.Join(", ", top) : "")
             + $"; $REPARSE_POINT phi cư trú {B(_cloudReparseCl)}.";
    }

    void Assemble(MftRec[] recs, MftChunkOut[] units, int i, ref MftRec r, ref Node nd, bool metafile, out long logical, out long alloc, out long resident, ref NodeFlags flags, bool collect = false)
    {
        bool cloudStats = collect && !metafile && ((r.FileAttr & (NtApi.ATTR_RECALL_ON_DATA_ACCESS | NtApi.ATTR_RECALL_ON_OPEN)) != 0 || NtApi.IsCloudTag(r.ReparseTag));
        long mainSize = 0, mainRes = 0, mainClusters = 0; bool anyAds = false;
        Dictionary<string, (long size, long clusters, long res, bool has)>? ads = null;
        long adsLogical = 0, adsAlloc = 0, adsRes = 0;
        bool includeAds = _opt.IncludeAds;
        void Visit(MftRec rr)
        {
            var co = units[rr.Unit];
            for (int k = 0; k < rr.StreamCount; k++)
            {
                var s = co.Streams[rr.StreamStart + k];
                if (s.NameLen == 0)
                {
                    if (s.HasSize) { mainSize = s.Size; mainRes = Math.Max(mainRes, s.Resident); }
                    mainClusters += s.Clusters;
                }
                else
                {
                    string nm = new string(co.Pool, s.NameOffset, s.NameLen);
                    ads ??= new(StringComparer.Ordinal);
                    ads.TryGetValue(nm, out var cur);
                    ads[nm] = (s.HasSize ? s.Size : cur.size, cur.clusters + s.Clusters, Math.Max(cur.res, s.Resident), cur.has || s.HasSize);
                }
            }
        }
        Visit(r);
        for (int e = r.NextExt; e >= 0; e = recs[e].NextExt) Visit(recs[e]);
        if (cloudStats)
        {
            _cloudFiles++; _cloudMainCl += mainClusters; _cloudReparseCl += OtherOf(recs, i);
            if (ads != null)
                foreach (var (name, v) in ads)
                {
                    _cloudAdsCl += v.clusters; _cloudAdsLg += v.size;
                    _cloudAds.TryGetValue(name, out var cur); _cloudAds[name] = (cur.N + 1, cur.Lg + v.size, cur.Cl + v.clusters);
                }
        }
        if (ads != null)
            foreach (var (name, v) in ads)
            {
                bool wof = name.Equals("WofCompressedData", StringComparison.OrdinalIgnoreCase);
                bool bad = metafile && name == "$Bad";
                // luồng nội bộ của bộ lọc Cloud Files (OneDrive…): như WOF — chiếm cluster thật nên luôn tính Trên đĩa,
                // nhưng không phải dữ liệu người dùng nên không cộng Kích thước, không bật cờ ADS
                bool cfInternal = name.StartsWith(CloudFilesStreamPrefix, StringComparison.OrdinalIgnoreCase);
                if (cfInternal && collect) { _cfInternalBytes += v.clusters; if (v.clusters > 0) _cfInternalFiles++; }
                if (!wof && !cfInternal && !includeAds) continue;     // WOF luôn tính Trên đĩa; ADS khác theo tuỳ chọn
                if (!wof && !bad && !cfInternal) { adsLogical += v.size; anyAds = true; }
                adsAlloc += v.clusters; adsRes += v.res;
            }
        logical = mainSize + adsLogical;
        alloc = mainClusters + adsAlloc + (metafile ? OtherOf(recs, i) + IndexOf(recs, i) : 0);
        resident = mainRes + adsRes;
        if (anyAds) flags |= NodeFlags.HasAds;
        if (resident > 0) flags |= NodeFlags.Resident;
        if (r.Links > 1) flags |= NodeFlags.MultiLink;
        if ((r.FileAttr & (NtApi.ATTR_RECALL_ON_DATA_ACCESS | NtApi.ATTR_RECALL_ON_OPEN)) != 0 && alloc == 0 && resident == 0 && logical > 0) flags |= NodeFlags.CloudOnly;
    }

    // ----------------------------------------------------------------------------- $Bitmap
    long CountBitmap(Vol v, MftRec[] recs, MftChunkOut[] units)
    {
        // đọc lại bản ghi 6 ($Bitmap) từ chính $MFT để lấy run list
        long bitmapBytes = (v.TotalClusters + 7) / 8;
        long recOff = 6L * v.Frs; long diskOff = MapMft(v, recOff);
        int read = (v.Frs + v.BytesPerSector - 1) / v.BytesPerSector * v.BytesPerSector;
        byte* buf = (byte*)NativeMemory.AlignedAlloc((nuint)Math.Max(read, 4096), 4096);
        try
        {
            if (RandomAccess.Read(v.Handle, new Span<byte>(buf, read), diskOff / v.BytesPerSector * v.BytesPerSector) < v.Frs) return 0;
            byte* rec = buf + (diskOff % v.BytesPerSector);
            if (!MftParser.ApplyFixups(rec, v.Frs)) return 0;
            var runs = MftParser.FindRuns(rec, v.Frs, MftParser.AttrData, out long size);
            if (runs == null) return 0;
            long setBits = 0, remainingBits = v.TotalClusters;
            int chunk = 4 * 1024 * 1024; byte* cb = (byte*)NativeMemory.AlignedAlloc((nuint)chunk, 4096);
            try
            {
                foreach (var run in runs)
                {
                    if (run.Lcn < 0) continue;
                    long runBytes = Math.Min(run.Len * v.Cluster, (remainingBits + 7) / 8);
                    for (long p = 0; p < runBytes && remainingBits > 0; p += chunk)
                    {
                        int n = (int)Math.Min(chunk, runBytes - p); int want = (n + v.BytesPerSector - 1) / v.BytesPerSector * v.BytesPerSector;
                        int got = RandomAccess.Read(v.Handle, new Span<byte>(cb, want), run.Lcn * v.Cluster + p);
                        n = Math.Min(n, got);
                        for (int i = 0; i < n && remainingBits > 0; i++)
                        {
                            byte b = cb[i];
                            if (remainingBits >= 8) { setBits += System.Numerics.BitOperations.PopCount(b); remainingBits -= 8; }
                            else { setBits += System.Numerics.BitOperations.PopCount((uint)(b & ((1 << (int)remainingBits) - 1))); remainingBits = 0; }
                        }
                    }
                }
            }
            finally { NativeMemory.AlignedFree(cb); }
            return setBits;
        }
        finally { NativeMemory.AlignedFree(buf); }
    }

    static long MapMft(Vol v, long byteInMft)
    {
        long cl = byteInMft / v.Cluster, within = byteInMft % v.Cluster;
        foreach (var run in v.MftRuns)
            if (cl >= run.Vcn && cl < run.Vcn + run.Len && run.Lcn >= 0) return (run.Lcn + (cl - run.Vcn)) * v.Cluster + within;
        throw new InvalidDataException("vị trí nằm ngoài $MFT");
    }

    sealed class BitArrayLite
    {
        readonly ulong[] _w;
        public BitArrayLite(long n) => _w = new ulong[(n + 63) / 64 + 1];
        public bool Get(long i) => (_w[i >> 6] & (1UL << (int)(i & 63))) != 0;
        public void Set(long i) => _w[i >> 6] |= 1UL << (int)(i & 63);
    }
}
