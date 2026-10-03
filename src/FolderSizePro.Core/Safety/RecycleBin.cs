using FolderSizePro.Text;
using System.Runtime.InteropServices;
using System.Text;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;
using Microsoft.Win32;

namespace FolderSizePro.Safety;

public enum DeleteOutcome { Recycled, NotFound, Locked, Refused, Failed }

public sealed record DeleteResult(string Path, DeleteOutcome Outcome, string Detail, IReadOnlyList<string> Processes);

/// <param name="FreedAfterEmpty">Dung lượng thật sự được giải phóng SAU KHI dọn Thùng rác (đã trừ file có liên kết cứng còn ở ngoài vùng chọn).</param>
/// <param name="FreedNow">Giải phóng ngay: 0 vì Thùng rác nằm trên cùng ổ (UC-13).</param>
public sealed record FreedEstimate(long SelectedLogical, long SelectedAllocated, long FreedAfterEmpty, long FreedNow, int Files, int Dirs, int SharedHardLinkFiles, int MaxPathLength);

/// <summary>
/// UC-13 / KT-02: đây là API xoá DUY NHẤT của Core, và nó chỉ đưa vào Thùng rác — không có tham số "xoá vĩnh viễn".
/// Chặn trước khi gọi: vị trí được bảo vệ (KT-03/KT-09), ổ không có Thùng rác, Thùng rác tắt/đầy, đường dẫn quá dài (ER-11).
/// </summary>
public static unsafe class RecycleBin
{
    // ------------------------------------------------------------------ ước tính dung lượng giải phóng
    public static FreedEstimate Estimate(ScanResult r, IReadOnlyList<int> nodes)
    {
        var t = r.Tree; var inSel = new HashSet<int>();
        long logical = 0, alloc = 0; int files = 0, dirs = 0, maxLen = 0;
        var linkIn = new Dictionary<(ulong, ulong, ulong), int>();
        var candByNode = r.HardLinkCands.GroupBy(c => c.Node).ToDictionary(g => g.Key, g => g.First());
        var groupFirst = new Dictionary<(ulong, ulong, ulong), Scan.HardLinkCand>();
        long freed = 0; int shared = 0;

        var stack = new Stack<(int Node, int PathLen)>();
        foreach (var n in nodes) stack.Push((n, t.FullPath(n).Length));
        var multi = new List<Scan.HardLinkCand>();
        while (stack.Count > 0)
        {
            var (n, plen) = stack.Pop();
            ref var nd = ref t[n];
            if (nd.Parent == -2) continue;
            maxLen = Math.Max(maxLen, plen);
            if (nd.IsDir)
            {
                dirs++; freed += nd.OwnAllocated; alloc += nd.OwnAllocated;
                for (int k = 0; k < nd.ChildCount; k++) { int c = nd.FirstChild + k; stack.Push((c, plen + 1 + t[c].NameLen)); }
                continue;
            }
            files += nd.Has(NodeFlags.Aggregate) ? nd.FileCount : 1;
            if (candByNode.TryGetValue(n, out var cand))
            {
                multi.Add(cand); logical += cand.Logical; alloc += cand.Allocated;
            }
            else { logical += nd.OwnLogical; alloc += nd.OwnAllocated; freed += nd.OwnAllocated; }
        }
        foreach (var g in multi.GroupBy(c => (c.Serial, c.IdLo, c.IdHi)))
        {
            var list = g.ToList();
            if (list.Count == list[0].Links) freed += list[0].Allocated;   // mọi liên kết đều nằm trong vùng chọn
            else shared += list.Count;
        }
        return new FreedEstimate(logical, alloc, freed, 0, files, dirs, shared, maxLen);
    }

    // ------------------------------------------------------------------ điều kiện
    public static (bool Ok, string? Reason) CheckEligibility(IReadOnlyList<string> paths, long totalLogical, int maxPathLength)
    {
        foreach (var p in paths)
        {
            var v = ProtectionPolicy.Check(p);
            if (!v.Allowed) return (false, v.Reason);
            var vol = VolumeInfo.For(p);
            if (vol.Kind != VolumeKind.Fixed)
                return (false, L.T($"Ổ {vol.RootPath} ({vol.Kind}) không có Thùng rác — app không xoá thẳng thay thế. Hãy xoá trong Explorer nếu thực sự muốn.", $"Drive {vol.RootPath} ({vol.Kind}) has no Recycle Bin — the app does not delete directly instead. Use Explorer if you really want that."));
            if (maxPathLength > 259)
                return (false, L.T($"Có đường dẫn dài {maxPathLength} ký tự — vượt giới hạn 259 của Thùng rác; Windows sẽ xoá vĩnh viễn nên app từ chối.", $"A path is {maxPathLength} characters long — over the Recycle Bin's 259 limit; Windows would delete permanently, so the app refuses."));
            var (nuke, capMb) = ReadBinSettings(vol);
            if (nuke) return (false, L.T($"Thùng rác của ổ {vol.RootPath} đang bị tắt (xoá thẳng) — app từ chối vì sẽ mất dữ liệu vĩnh viễn. Bật lại Thùng rác trong Properties của Thùng rác.", $"The Recycle Bin of drive {vol.RootPath} is disabled (delete immediately) — the app refuses because data would be lost permanently. Re-enable it in the Recycle Bin's Properties."));
            long cap = capMb.HasValue ? capMb.Value * 1024L * 1024 : vol.TotalBytes / 10;
            if (cap > 0 && totalLogical > cap)
                return (false, L.T($"Dung lượng cần xoá ({totalLogical:N0} B) vượt dung lượng tối đa Thùng rác của ổ {vol.RootPath} ({cap:N0} B); Windows sẽ xoá vĩnh viễn nên app từ chối. Tăng dung lượng Thùng rác hoặc xoá từng phần.", $"The amount to delete ({totalLogical:N0} B) exceeds the Recycle Bin's maximum on drive {vol.RootPath} ({cap:N0} B); Windows would delete permanently, so the app refuses. Raise the bin size or delete in parts."));
        }
        return (true, null);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeNameForVolumeMountPointW(string mount, [Out] char[] name, int len);

    static (bool Nuke, long? MaxCapacityMb) ReadBinSettings(VolumeInfo vol)
    {
        try
        {
            var buf = new char[64];
            if (!GetVolumeNameForVolumeMountPointW(vol.RootPath, buf, buf.Length)) return (false, null);
            string guid = new string(buf).TrimEnd('\0');                       // \\?\Volume{xxxxxxxx-…}\
            int a = guid.IndexOf('{'), b = guid.IndexOf('}');
            if (a < 0 || b < 0) return (false, null);
            string key = guid.Substring(a, b - a + 1);
            bool nuke = false; long? cap = null;
            using (var gk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket"))
            {
                if (gk?.GetValue("NukeOnDelete") is int gn && gn != 0 && (gk.GetValue("UseGlobalSettings") is int ug && ug != 0)) nuke = true;
            }
            using (var vk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + key))
            {
                if (vk?.GetValue("NukeOnDelete") is int n && n != 0) nuke = true;
                if (vk?.GetValue("MaxCapacity") is int m && m > 0) cap = m;
            }
            using (var pk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer"))
                if (pk?.GetValue("NoRecycleFiles") is int nr && nr != 0) nuke = true;
            return (nuke, cap);
        }
        catch { return (false, null); }
    }

    // ------------------------------------------------------------------ xoá
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd; public uint wFunc; public IntPtr pFrom; public IntPtr pTo; public ushort fFlags;
        public int fAnyOperationsAborted; public IntPtr hNameMappings; public IntPtr lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>Đưa từng mục vào Thùng rác. Caller PHẢI đã gọi CheckEligibility (hàm này vẫn kiểm tra lại ProtectionPolicy — lớp bảo vệ thứ hai).</summary>
    public static List<DeleteResult> Delete(IReadOnlyList<string> paths, IntPtr owner = default)
    {
        var results = new List<DeleteResult>();
        foreach (var p in paths)
        {
            var v = ProtectionPolicy.Check(p);
            if (!v.Allowed) { results.Add(new DeleteResult(p, DeleteOutcome.Refused, v.Reason ?? L.T("Bị chặn.", "Refused."), Array.Empty<string>())); continue; }
            string ext = NtApi.ToExtendedPath(p);
            if (!File.Exists(ext) && !Directory.Exists(ext)) { results.Add(new DeleteResult(p, DeleteOutcome.NotFound, L.T("Mục không còn tồn tại.", "Item no longer exists."), Array.Empty<string>())); continue; }

            IntPtr from = Marshal.StringToHGlobalUni(p + "\0");
            try
            {
                var op = new SHFILEOPSTRUCTW { hwnd = owner, wFunc = FO_DELETE, pFrom = from, fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING) };
                int rc = SHFileOperationW(ref op);
                bool gone = !File.Exists(ext) && !Directory.Exists(ext);
                if (rc == 0 && op.fAnyOperationsAborted == 0 && gone) results.Add(new DeleteResult(p, DeleteOutcome.Recycled, L.T("Đã đưa vào Thùng rác.", "Moved to the Recycle Bin."), Array.Empty<string>()));
                else if (rc is 2 or 3 or 0x7C or 0x402) results.Add(new DeleteResult(p, DeleteOutcome.NotFound, L.T("Mục không còn tồn tại.", "Item no longer exists."), Array.Empty<string>()));
                else if (rc is 0x20 or 0x21 or 0x5 || (rc == 0 && !gone))
                {
                    var procs = WhoLocks(p);
                    results.Add(new DeleteResult(p, DeleteOutcome.Locked,
                        procs.Count > 0 ? "Đang được dùng bởi: " + string.Join(", ", procs) : $"Không xoá được (mã {rc:X}) — có thể đang được tiến trình khác dùng hoặc không đủ quyền.", procs));
                }
                else results.Add(new DeleteResult(p, DeleteOutcome.Failed, $"Windows báo lỗi 0x{rc:X}" + (op.fAnyOperationsAborted != 0 ? " (bị huỷ)" : ""), Array.Empty<string>()));
            }
            finally { Marshal.FreeHGlobal(from); }
        }
        return results;
    }

    // ------------------------------------------------------------------ Restart Manager (ER-10)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO
    {
        public uint dwProcessId; public uint ftLow, ftHigh;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType; public uint AppStatus; public uint TSSessionId; public int bRestartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint h, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint h, uint nFiles, string[] files, uint nApps, IntPtr apps, uint nSvc, string[]? svc);
    [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint h, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[]? info, out uint reasons);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint h);

    public static List<string> WhoLocks(string path)
    {
        var names = new List<string>();
        var key = new StringBuilder(33);
        if (RmStartSession(out uint h, 0, key) != 0) return names;
        try
        {
            var files = new List<string>();
            if (File.Exists(NtApi.ToExtendedPath(path))) files.Add(path);
            else if (Directory.Exists(NtApi.ToExtendedPath(path)))
            {
                try { files.AddRange(Directory.EnumerateFiles(NtApi.ToExtendedPath(path), "*", SearchOption.AllDirectories).Take(400).Select(f => f.StartsWith(@"\\?\") ? f[4..] : f)); } catch { }
            }
            if (files.Count == 0) return names;
            if (RmRegisterResources(h, (uint)files.Count, files.ToArray(), 0, IntPtr.Zero, 0, null) != 0) return names;
            uint needed = 0, count = 0;
            int rc = RmGetList(h, out needed, ref count, null, out _);
            if (rc == 234 && needed > 0)
            {
                var info = new RM_PROCESS_INFO[needed]; count = needed;
                if (RmGetList(h, out needed, ref count, info, out _) == 0)
                    for (int i = 0; i < count; i++) names.Add($"{info[i].strAppName} (PID {info[i].dwProcessId})");
            }
        }
        catch { }
        finally { RmEndSession(h); }
        return names.Distinct().ToList();
    }
}
