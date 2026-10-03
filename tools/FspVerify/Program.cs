// FspVerify — kiểm chứng THẬT, in số THẬT (không assert). Mỗi mục in "kỳ vọng / nguồn độc lập" cạnh "kết quả của máy quét"
// để mắt người so. Cú pháp:  FspVerify <mục>...   (mục: fixture, truth, denied, churn, bulk, onedrive, protect, snapshot, export, all)
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;
using FolderSizePro.Safety;
using FolderSizePro.Scan;
using FolderSizePro.Storage;

Console.OutputEncoding = new UTF8Encoding(false);
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\_fixtures"));
if (args.Length > 1 && args[0] == "pv") { var c = PathValidator.Validate(args[1]); Console.WriteLine($"Ok={c.Ok} Path=[{c.Path}] Err={c.Error}"); return 0; }
var sections = args.Length > 0 && (args[0] == "truth" || args[0] == "mft") ? new[] { args[0] } : args.Length == 0 || args[0] == "all" ? new[] { "fixture", "truth", "denied", "churn", "bulk", "onedrive", "protect", "snapshot", "export", "rescan", "recycle", "mftparser", "watch", "misc" } : args;
foreach (var s in sections)
{
    Console.WriteLine($"\n════════════════════════════════ {s.ToUpperInvariant()} ════════════════════════════════");
    try
    {
        switch (s)
        {
            case "fixture": Verify.Fixture(root); break;
            case "truth": Verify.Truth(args.Length > 1 ? args[1] : @"D:\GitHub\qr-studio"); break;
            case "denied": Verify.Denied(root); break;
            case "churn": Verify.Churn(root); break;
            case "bulk": Verify.Bulk(root); break;
            case "onedrive": Verify.OneDrive(); break;
            case "protect": Verify.Protect(); break;
            case "snapshot": Verify.Snapshot(root); break;
            case "export": Verify.Export(root); break;
            case "rescan": Verify.Rescan(root); break;
            case "mftparser": Verify.MftParserSelfTest(); break;
            case "watch": Verify.Watch(root); break;
            case "misc": Verify.Misc(root); break;
            case "mft": Verify.Mft(args.Length > 1 ? args[1] : "D:"); break;
            case "recycle": Verify.Recycle(root); break;
            default: Console.WriteLine("Mục không biết: " + s); break;
        }
    }
    catch (Exception ex) { Console.WriteLine("LỖI khi chạy mục: " + ex); }
}
return 0;

static unsafe class Verify
{
    // ---------------------------------------------------------------- helpers
    public static void Cmd(string line)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c " + line) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!; var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
        if (p.ExitCode != 0) Console.WriteLine($"  [cảnh báo] `{line}` thoát {p.ExitCode}: {o.Trim()}");
    }

    static string N(long v) => v.ToString("N0");
    static void Row(string name, string expected, string actual) =>
        Console.WriteLine($"  {name,-58} kỳ vọng {expected,18}   thực tế {actual,18}   {(expected == actual ? "" : "← KHÁC")}");

    public static string BuildFixture(string root)
    {
        string fx = Path.Combine(root, "verify");
        if (Directory.Exists(fx)) { try { Directory.Delete(NtApi.ToExtendedPath(fx), true); } catch { } }
        Directory.CreateDirectory(fx);
        void W(string rel, int n) { var p = Path.Combine(fx, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, Enumerable.Range(0, n).Select(i => (byte)(i * 31 + 7)).ToArray()); }
        foreach (var n in new[] { 0, 1, 100, 700, 4095, 4096, 4097, 1048576 }) W($"sizes\\f{n}.bin", n);
        W("hl\\dirA\\file.bin", 100000);
        Directory.CreateDirectory(Path.Combine(fx, "hl", "dirB"));
        Cmd($"fsutil hardlink create \"{Path.Combine(fx, "hl", "dirB", "link.bin")}\" \"{Path.Combine(fx, "hl", "dirA", "file.bin")}\"");
        // link thứ 3 NẰM NGOÀI vùng quét (để thử "liên kết ngoài vùng")
        Directory.CreateDirectory(Path.Combine(root, "outside"));
        File.Delete(Path.Combine(root, "outside", "extlink.bin"));
        Cmd($"fsutil hardlink create \"{Path.Combine(root, "outside", "extlink.bin")}\" \"{Path.Combine(fx, "hl", "dirA", "file.bin")}\"");
        // ghi thêm qua link A để link B có kích thước "cũ" trong danh sách thư mục
        using (var fs = File.Open(Path.Combine(fx, "hl", "dirA", "file.bin"), FileMode.Append)) fs.Write(new byte[50000]);
        var comp = Path.Combine(fx, "comp", "text.txt"); Directory.CreateDirectory(Path.GetDirectoryName(comp)!);
        File.WriteAllText(comp, string.Concat(Enumerable.Repeat("Folder Size Pro đo chính xác từng byte. ", 40000)));
        Cmd($"compact /c \"{comp}\"");
        var sp = Path.Combine(fx, "sparse", "s.bin"); Directory.CreateDirectory(Path.GetDirectoryName(sp)!);
        File.WriteAllBytes(sp, new byte[1]); Cmd($"fsutil sparse setflag \"{sp}\"");
        using (var fs = new FileStream(sp, FileMode.Open, FileAccess.Write)) { var b = new byte[1048576]; new Random(1).NextBytes(b); fs.Write(b); fs.SetLength(100L * 1024 * 1024); }
        var ads = Path.Combine(fx, "ads", "host.txt"); Directory.CreateDirectory(Path.GetDirectoryName(ads)!);
        File.WriteAllBytes(ads, new byte[10]); File.WriteAllBytes(ads + ":big", new byte[50000]); File.WriteAllBytes(ads + ":Zone.Identifier", Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n"));
        Directory.CreateDirectory(Path.Combine(fx, "loop", "real")); W("loop\\real\\inside.bin", 5000);
        Cmd($"mklink /j \"{Path.Combine(fx, "loop", "real", "back")}\" \"{Path.Combine(fx, "loop")}\"");
        Cmd($"mklink /d \"{Path.Combine(fx, "loop", "symdir")}\" \"{Path.Combine(fx, "sizes")}\"");
        var longDir = Path.Combine(fx, "long", new string('a', 120), new string('b', 120), new string('c', 60));
        Directory.CreateDirectory(NtApi.ToExtendedPath(longDir));
        File.WriteAllBytes(NtApi.ToExtendedPath(Path.Combine(longDir, "deep.bin")), new byte[3000]);
        File.WriteAllBytes(NtApi.ToExtendedPath(Path.Combine(fx, "long", "trailing-space ")), new byte[1234]);
        File.WriteAllBytes(NtApi.ToExtendedPath(Path.Combine(fx, "long", "CON")), new byte[77]);   // tên dành riêng, tạo được qua \\?\
        W("unicode\\tệp-tiếng-việt-ảnh.png", 9000);
        return fx;
    }

    static long Ceil(long v, long c) => (v + c - 1) / c * c;

    // ---------------------------------------------------------------- 1. fixture
    public static void Fixture(string root)
    {
        string fx = BuildFixture(root);
        var vol = VolumeInfo.For(fx); long cl = vol.ClusterSize;
        Console.WriteLine($"Fixture: {fx}   ổ {vol.RootPath} {vol.FileSystem}, cluster {cl}");
        var sw = Stopwatch.StartNew();
        var r = ScanJob.Run(new[] { fx });
        Console.WriteLine($"Quét xong trong {sw.ElapsedMilliseconds} ms, {r.Issues.Count} mục ghi chú/lỗi, trạng thái {r.Completeness}.\n");
        var t = r.Tree;
        int Find(string rel) { int cur = r.Roots[0].Node; foreach (var part in rel.Split('\\')) { int found = -1; foreach (var c in t.Children(cur)) if (t.NameString(c).Equals(part, StringComparison.OrdinalIgnoreCase)) { found = c; break; } if (found < 0) return -1; cur = found; } return cur; }
        void Check(string rel, long expL, long expA, string? note = null)
        {
            int n = Find(rel);
            if (n < 0) { Console.WriteLine($"  {rel,-58} KHÔNG TÌM THẤY trong cây"); return; }
            Console.WriteLine($"  {rel,-40} Kích thước kỳ vọng {N(expL),12} thực tế {N(t[n].Logical),12} | Trên đĩa kỳ vọng {N(expA),10} thực tế {N(t[n].Allocated),10} {(t[n].Logical == expL && t[n].Allocated == expA ? "" : "← KHÁC")} {note}");
        }
        Console.WriteLine("Kích thước biên (cluster = " + cl + "; ≤ ~700 B là 'thường trú' trong MFT → 0 cluster):");
        foreach (var n in new[] { 0, 1, 100, 700 }) Check($"sizes\\f{n}.bin", n, 0, n > 0 ? "(thường trú MFT)" : "");
        Check("sizes\\f4095.bin", 4095, 4096); Check("sizes\\f4096.bin", 4096, 4096); Check("sizes\\f4097.bin", 4097, 8192); Check("sizes\\f1048576.bin", 1048576, 1048576);
        Console.WriteLine("\nHard link (file.bin 150.000 B; 3 đường dẫn: dirA, dirB, và 1 NGOÀI vùng quét):");
        Check("hl\\dirA\\file.bin", 150000, Ceil(150000, cl), "← chủ (đường dẫn nhỏ nhất)");
        Check("hl\\dirB\\link.bin", 0, 0, "← liên kết không chủ, không cộng");
        Check("hl", 150000, Ceil(150000, cl), "← tổng thư mục KHÔNG đếm đôi, dù danh sách thư mục báo link.bin = 100.000 (cũ)");
        Console.WriteLine("\nNén NTFS / sparse / ADS / junction / đường dẫn dài:");
        long compExp = ExtentTruthFile(Path.Combine(fx, "comp", "text.txt"), cl);
        Check("comp\\text.txt", 1800000, compExp, $"← EXTENT thật = {N(compExp)}");
        Check("sparse\\s.bin", 104857600, 1048576, "← 100 MiB logical, 1 MiB thật");
        Check("ads\\host.txt", 10 + 50000 + 26, 53248, "← 10 + ADS :big 50.000 + Zone.Identifier 26; trên đĩa chỉ :big");
        Check("loop\\real\\back", 0, 0, "← mount point, không đi theo");
        Check("loop\\symdir", 0, 0, "← symlink thư mục, không đi theo (không đếm đôi thư mục sizes)");
        Check("loop", 5000, Ceil(5000, cl));
        Check("long", 3000 + 1234 + 77, Ceil(3000, cl) + Ceil(1234, cl) + 0 + 0, "← CON (77 B) thường trú → 0; deep.bin ở đường dẫn ~330 ký tự");
        Check("unicode", 9000, Ceil(9000, cl));
        Console.WriteLine($"\nLiên kết cứng không chủ: {r.HardLinkNonOwnerCount} (kỳ vọng 1: dirB\\link.bin; link ngoài vùng không có trong cây)");
        Console.WriteLine("Mục ghi chú:"); foreach (var i in r.Issues) Console.WriteLine($"   [{i.Kind}] {i.Path} — {i.Message}");
        Console.WriteLine("Nhất quán:"); foreach (var c in r.Consistency) Console.WriteLine($"   {(c.Ok ? "ĐẠT " : "LỆCH")} {c.Name}  ({c.Expected} / {c.Actual})");
    }

    // ---------------------------------------------------------------- nguồn độc lập: extent thật
    static long ExtentTruthFile(string path, long cluster)
    {
        var h = NtApi.CreateFileW(NtApi.ToExtendedPath(path), NtApi.FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, NtApi.OPEN_EXISTING, NtApi.FILE_FLAG_BACKUP_SEMANTICS | NtApi.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (h == NtApi.INVALID_HANDLE_VALUE) return -1;
        try
        {
            long total = 0, startVcn = 0; var o = (byte*)NativeMemory.Alloc(64 * 1024);
            try
            {
                while (true)
                {
                    long inV = startVcn;
                    bool ok = NtApi.DeviceIoControl(h, 0x90073, &inV, 8, o, 64 * 1024, out _, IntPtr.Zero);
                    int err = ok ? 0 : Marshal.GetLastWin32Error();
                    if (!ok && err != 234) return err == 38 ? 0 : -1;
                    uint cnt = *(uint*)o; long prev = *(long*)(o + 8);
                    for (uint i = 0; i < cnt; i++) { long next = *(long*)(o + 16 + i * 16), lcn = *(long*)(o + 24 + i * 16); if (lcn != -1) total += (next - prev) * cluster; prev = next; }
                    if (ok) break; startVcn = prev;
                }
            }
            finally { NativeMemory.Free(o); }
            return total;
        }
        finally { NtApi.CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(IntPtr h, out BY_HANDLE_FILE_INFORMATION info);
    [StructLayout(LayoutKind.Sequential)]
    struct BY_HANDLE_FILE_INFORMATION { public uint Attr, CL, CH, AL, AH, WL, WH, Serial, SizeHigh, SizeLow, Links, IdHigh, IdLow; }

    // ---------------------------------------------------------------- 2. truth: so với nguồn độc lập trên thư mục thật
    public static void Truth(string dir)
    {
        dir = Path.GetFullPath(dir);
        var vol = VolumeInfo.For(dir); long cl = vol.ClusterSize;
        Console.WriteLine($"Thư mục thật: {dir}  (cluster {cl})");
        var sw = Stopwatch.StartNew();
        var r = ScanJob.Run(new[] { dir });
        long ourMs = sw.ElapsedMilliseconds;
        var rn = r.Tree[r.Roots[0].Node];

        // nguồn độc lập 1: .NET FileInfo.Length (danh sách thư mục) — chỉ luồng chính, đếm đôi hard link
        sw.Restart();
        long netLogical = 0, netFiles = 0; var seen = new HashSet<(uint, uint, uint)>();
        long truthAlloc = 0, truthLogical = 0, truthFiles = 0, multi = 0;
        var stack = new Stack<string>(); stack.Push(dir);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            try
            {
                var di = new DirectoryInfo(NtApi.ToExtendedPath(d));
                foreach (var e in di.EnumerateFileSystemInfos())
                {
                    if ((e.Attributes & FileAttributes.Directory) != 0) { if ((e.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(e.FullName.StartsWith(@"\\?\") ? e.FullName[4..] : e.FullName); continue; }
                    var fi = (FileInfo)e; netLogical += fi.Length; netFiles++;
                    // nguồn độc lập 2: BY_HANDLE (id file) để loại hard link + extent thật
                    var h = NtApi.CreateFileW(NtApi.ToExtendedPath(fi.FullName.StartsWith(@"\\?\") ? fi.FullName[4..] : fi.FullName), NtApi.FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, NtApi.OPEN_EXISTING, NtApi.FILE_FLAG_BACKUP_SEMANTICS | NtApi.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
                    if (h == NtApi.INVALID_HANDLE_VALUE) continue;
                    try
                    {
                        if (!GetFileInformationByHandle(h, out var bi)) continue;
                        if (bi.Links > 1) { multi++; if (!seen.Add((bi.Serial, bi.IdHigh, bi.IdLow))) continue; }
                        truthFiles++;
                        truthLogical += ((long)bi.SizeHigh << 32) | bi.SizeLow;
                        string p = fi.FullName.StartsWith(@"\\?\") ? fi.FullName[4..] : fi.FullName;
                        long main = ExtentTruthFile(p, cl); if (main > 0) truthAlloc += main;
                        // ADS: dùng FindFirstStreamW (nguồn độc lập với NtQueryInformationFile)
                        foreach (var (name, size) in Streams(p))
                        { truthLogical += size; long a = ExtentTruthFile(p + name.Replace(":$DATA", ""), cl); if (a > 0) truthAlloc += a; }
                    }
                    finally { NtApi.CloseHandle(h); }
                }
            }
            catch (Exception ex) { Console.WriteLine($"  (bỏ qua {d}: {ex.Message})"); }
        }
        long truthMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"Máy quét: {ourMs} ms · nguồn độc lập (extent + FindFirstStream + id file): {truthMs} ms\n");
        Row("Số file", N(truthFiles), N(rn.FileCount));
        Row("Kích thước (đã loại hard link, gồm ADS)", N(truthLogical), N(rn.Logical));
        Row("Trên đĩa (cộng EXTENT thật, gồm ADS, trừ thường trú)", N(truthAlloc), N(rn.Allocated - rn.OwnAllocated - DirIndexAlloc(r.Tree, r.Roots[0].Node)));
        Console.WriteLine($"\n  (tham khảo) tổng .NET FileInfo.Length KHÔNG loại hard link, KHÔNG ADS: {N(netLogical)} ({N(netFiles)} file, {N(multi)} file nhiều link)");
        Console.WriteLine($"  (tham khảo) 'Trên đĩa' của máy quét gồm cả phần index thư mục = {N(DirIndexAlloc(r.Tree, r.Roots[0].Node) + rn.OwnAllocated)} B nên đã trừ ra ở dòng so sánh trên.");
    }

    static long DirIndexAlloc(ScanTree t, int node)
    {
        long sum = 0; var st = new Stack<int>(); st.Push(node);
        while (st.Count > 0) { var n = st.Pop(); if (!t[n].IsDir) continue; if (n != node) sum += t[n].OwnAllocated; foreach (var c in t.Children(n)) st.Push(c); }
        return sum;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr FindFirstStreamW(string name, int infoLevel, out WIN32_FIND_STREAM_DATA data, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool FindNextStreamW(IntPtr h, out WIN32_FIND_STREAM_DATA data);
    [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WIN32_FIND_STREAM_DATA { public long Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name; }

    static IEnumerable<(string, long)> Streams(string path)
    {
        var list = new List<(string, long)>();
        var h = FindFirstStreamW(NtApi.ToExtendedPath(path), 0, out var d, 0);
        if (h == NtApi.INVALID_HANDLE_VALUE) return list;
        do { if (d.Name != "::$DATA") list.Add((d.Name, d.Size)); } while (FindNextStreamW(h, out d));
        FindClose(h);
        return list;
    }

    // ---------------------------------------------------------------- 3. denied
    public static void Denied(string root)
    {
        string dir = Path.Combine(root, "denied");
        try { Cmd($"icacls \"{dir}\" /reset /t /c /q"); Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(Path.Combine(dir, "ok")); Directory.CreateDirectory(Path.Combine(dir, "secret", "inner"));
        File.WriteAllBytes(Path.Combine(dir, "ok", "a.bin"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(dir, "secret", "inner", "hidden.bin"), new byte[123456]);
        string me = Environment.UserName;
        Cmd($"icacls \"{Path.Combine(dir, "secret")}\" /deny \"{me}:(OI)(CI)(RX)\" /q");
        var r = ScanJob.Run(new[] { dir });
        var t = r.Tree; var rn = t[r.Roots[0].Node];
        Console.WriteLine($"Thư mục 'secret' bị icacls /deny (đọc/liệt kê). Tổng Trên đĩa thấy được = {N(rn.Allocated)} (kỳ vọng 8.192 = chỉ a.bin; 'hidden.bin' 123.456 B KHÔNG được đoán).");
        foreach (var c in t.Children(r.Roots[0].Node)) Console.WriteLine($"   {t.NameString(c),-10} cờ: {string.Join('|', ResultExporter.FlagNames(t[c].Flags))}");
        Console.WriteLine("Mục lỗi: "); foreach (var i in r.Issues) Console.WriteLine($"   [{i.Kind}] {i.Path} — {i.Message}");
        Cmd($"icacls \"{dir}\" /reset /t /c /q"); try { Directory.Delete(dir, true); } catch (Exception ex) { Console.WriteLine("(dọn: " + ex.Message + ")"); }
    }

    // ---------------------------------------------------------------- 4. churn
    public static void Churn(string root)
    {
        string dir = Path.Combine(root, "churn"); try { Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        for (int d = 0; d < 200; d++) { var sub = Path.Combine(dir, "d" + d); Directory.CreateDirectory(sub); for (int f = 0; f < 100; f++) File.WriteAllBytes(Path.Combine(sub, $"f{f}.bin"), new byte[1000 + f]); }
        using var stop = new CancellationTokenSource();
        long created = 0, deleted = 0;
        var writer = Task.Run(() =>
        {
            var rnd = new Random(7);
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var sub = Path.Combine(dir, "d" + rnd.Next(200));
                    File.WriteAllBytes(Path.Combine(sub, "new" + rnd.Next(1000) + ".bin"), new byte[rnd.Next(5000)]); created++;
                    File.Delete(Path.Combine(sub, $"f{rnd.Next(100)}.bin")); deleted++;
                }
                catch { }
            }
        });
        var r = ScanJob.Run(new[] { dir });
        stop.Cancel(); writer.Wait();
        Console.WriteLine($"Ghi/xoá file liên tục trong lúc quét ({created:N0} tạo, {deleted:N0} xoá). Máy quét KHÔNG crash; trạng thái {r.Completeness}.");
        Console.WriteLine($"Ghi nhận 'thay đổi khi quét': {r.Issues.Count(i => i.Kind == IssueKind.Changed)} mục (ER-05)  · tổng file {N(r.Tree[r.Roots[0].Node].FileCount)}");
        foreach (var c in r.Consistency) Console.WriteLine($"   {(c.Ok ? "ĐẠT " : "LỆCH")} {c.Name}");
        try { Directory.Delete(dir, true); } catch { }
    }

    // ---------------------------------------------------------------- 5. bulk
    public static void Bulk(string root)
    {
        string dir = Path.Combine(root, "bulk");
        if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Count() < 10)
        {
            Console.WriteLine("Tạo 100.000 file nhỏ (một lần)…"); Directory.CreateDirectory(dir);
            Parallel.For(0, 1000, d => { var sub = Path.Combine(dir, "d" + d); Directory.CreateDirectory(sub); for (int f = 0; f < 100; f++) File.WriteAllBytes(Path.Combine(sub, $"f{f}.dat"), new byte[(d * 7 + f * 13) % 6000]); });
        }
        var sw = Stopwatch.StartNew();
        var r = ScanJob.Run(new[] { dir });
        var rn = r.Tree[r.Roots[0].Node];
        Console.WriteLine($"Quét {N(rn.FileCount)} file / {N(rn.DirCount)} thư mục trong {sw.ElapsedMilliseconds} ms ({rn.FileCount * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):N0} file/s)");
        Console.WriteLine($"RAM tiến trình: {Process.GetCurrentProcess().WorkingSet64 / 1048576:N0} MiB · cây {r.Tree.ApproxBytes / 1048576:N1} MiB");
        // nguồn độc lập: PowerShell
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"$x=Get-ChildItem -LiteralPath '{dir}' -Recurse -Force -File | Measure-Object Length -Sum; '{{0}} {{1}}' -f $x.Count,$x.Sum\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        sw.Restart(); using var p = Process.Start(psi)!; var outp = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit();
        var parts = outp.Split(' ');
        Row("PowerShell Get-ChildItem: số file", parts[0], rn.FileCount.ToString());
        Row("PowerShell Get-ChildItem: tổng Length", parts.Length > 1 ? parts[1] : "?", rn.Logical.ToString());
        Console.WriteLine($"  (PowerShell mất {sw.ElapsedMilliseconds:N0} ms)");
    }

    // ---------------------------------------------------------------- 6. onedrive
    public static void OneDrive()
    {
        string od = Environment.GetEnvironmentVariable("OneDrive") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
        if (!Directory.Exists(od)) { Console.WriteLine("Không có thư mục OneDrive: " + od); return; }
        // chụp cờ trước
        var before = new Dictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase);
        void Snap(Dictionary<string, FileAttributes> d)
        {
            var st = new Stack<string>(); st.Push(od);
            while (st.Count > 0)
            {
                var p = st.Pop();
                try
                {
                    foreach (var e in new DirectoryInfo(p).EnumerateFileSystemInfos())
                    {
                        var a = e.Attributes;
                        if ((a & (FileAttributes)0x400000) != 0 || (a & (FileAttributes)0x40000) != 0) d[e.FullName] = a;
                        if ((a & FileAttributes.Directory) != 0 && (a & FileAttributes.ReparsePoint) == 0 && (a & (FileAttributes)0x40000) == 0) st.Push(e.FullName);
                    }
                }
                catch { }
            }
        }
        Snap(before);
        var sw = Stopwatch.StartNew();
        var r = ScanJob.Run(new[] { od });
        var rn = r.Tree[r.Roots[0].Node];
        var after = new Dictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase); Snap(after);
        int changed = before.Count(kv => !after.TryGetValue(kv.Key, out var v) || v != kv.Value);
        Console.WriteLine($"Quét {od}: {N(rn.FileCount)} file, Kích thước {N(rn.Logical)} B, Trên đĩa {N(rn.Allocated)} B, {sw.ElapsedMilliseconds:N0} ms");
        long cloudOnly = 0, cloudOnlyLogical = 0;
        for (int i = 1; i < r.Tree.Count; i++) if (r.Tree[i].Parent >= 0 && r.Tree[i].Has(NodeFlags.CloudOnly)) { cloudOnly++; cloudOnlyLogical += r.Tree[i].Logical; }
        Console.WriteLine($"File 'chỉ trên đám mây' (Trên đĩa = 0): {N(cloudOnly)} file, {N(cloudOnlyLogical)} B logical");
        Console.WriteLine($"Thư mục đám mây chưa nạp danh sách, không liệt kê: {r.Issues.Count(i => i.Kind == IssueKind.CloudNotListed)}");
        Console.WriteLine($"KT-05: {N(before.Count)} mục có cờ RECALL trước quét; sau quét số mục đổi cờ = {changed} (kỳ vọng 0)");
    }

    // ---------------------------------------------------------------- 7. protect
    public static void Protect()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cases = new[]
        {
            @"C:\", @"D:\", @"C:\Windows", @"C:\Windows\System32\drivers", @"C:\Windows\Temp", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData",
            @"C:\Users", profile, Path.Combine(profile, "Documents"), Path.Combine(profile, "AppData", "Local", "Temp"),
            @"C:\System Volume Information", @"C:\$Recycle.Bin", @"C:\pagefile.sys", @"C:\$MFT", @"D:\$Recycle.Bin\S-1-5-21", @"C:\PROGRA~1",
            @"c:\windows\SYSTEM32", @"C:\Windows\..\Windows", @"\\?\C:\Windows", @"D:\GitHub\folder-size-pro\_fixtures\verify\loop\real\back", @"D:\GitHub\folder-size-pro\_fixtures\verify\loop\symdir",
            @"D:\GitHub\folder-size-pro\_fixtures\verify\sizes\f1.bin", @"D:\GitHub\folder-size-pro\_fixtures",
        };
        foreach (var c in cases) { var v = ProtectionPolicy.Check(c); Console.WriteLine($"  {(v.Allowed ? "CHO PHÉP " : "CHẶN     ")} {c,-82} {v.Reason}"); }
    }

    // ---------------------------------------------------------------- 8. snapshot
    public static void Snapshot(string root)
    {
        string fx = Path.Combine(root, "verify");
        if (!Directory.Exists(fx)) BuildFixture(root);
        var r = ScanJob.Run(new[] { fx });
        string f = Path.Combine(root, "verify.fsp");
        var sw = Stopwatch.StartNew(); FspFile.Save(r, f); long saveMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"Đã lưu {f}: {new FileInfo(f).Length:N0} byte ({saveMs} ms)");
        sw.Restart(); var r2 = FspFile.Load(f);
        Console.WriteLine($"Đã mở lại ({sw.ElapsedMilliseconds} ms). Gốc: A {N(r.RootAllocated())} / B {N(r2.RootAllocated())}; Kích thước A {N(r.RootLogical())} / B {N(r2.RootLogical())}");
        var diff = SnapshotComparer.Compare(r, r2);
        Console.WriteLine($"So sánh snapshot với chính nó → {diff.Entries.Count} khác biệt (kỳ vọng 0).");
        // thay đổi thật rồi so sánh
        File.WriteAllBytes(Path.Combine(fx, "sizes", "grew.bin"), new byte[300000]);
        File.Delete(Path.Combine(fx, "sizes", "f4097.bin"));
        var r3 = ScanJob.Run(new[] { fx });
        var d3 = SnapshotComparer.Compare(r2, r3);
        Console.WriteLine($"Sau khi thêm grew.bin (300.000 B) và xoá f4097.bin: Δ trên đĩa = {N(d3.TotalDeltaAllocated)} (kỳ vọng {N(Ceil(300000, 4096) - 8192)})");
        foreach (var e in d3.Entries.Take(6)) Console.WriteLine($"   {e.Status,-8} Δ {N(e.DeltaAllocated),10}  {e.Path}");
        // ER-12: các kiểu file hỏng
        byte[] good = File.ReadAllBytes(f);
        void Bad(string label, byte[] bytes)
        {
            string p = Path.Combine(root, "bad.fsp"); File.WriteAllBytes(p, bytes);
            try { FspFile.Load(p); Console.WriteLine($"  {label,-28} → ĐỌC ĐƯỢC (không đáng có!)"); }
            catch (FspFormatException ex) { Console.WriteLine($"  {label,-28} → {ex.Reason}: {ex.Message}"); }
        }
        var flip = (byte[])good.Clone(); flip[flip.Length / 2] ^= 0xFF; Bad("lật 1 byte giữa file", flip);
        Bad("cắt cụt", good[..(good.Length / 2)]);
        var magic = (byte[])good.Clone(); magic[0] = (byte)'X'; Bad("sai magic", magic);
        var ver = (byte[])good.Clone(); ver[4] = 99; Bad("version mới hơn", ver);
        Bad("file rỗng", Array.Empty<byte>());
        try { FspFile.Load(Path.Combine(root, "nope.fsp")); } catch (FspFormatException ex) { Console.WriteLine($"  {"không tồn tại",-28} → {ex.Reason}: {ex.Message}"); }
        Console.WriteLine("  Không còn file .tmp sót lại: " + !File.Exists(f + ".tmp"));
    }

    // ---------------------------------------------------------------- 10. rescan (UC-17)
    public static void Rescan(string root)
    {
        string fx = BuildFixture(root);
        var r = ScanJob.Run(new[] { fx });
        var t = r.Tree; int rootNode = r.Roots[0].Node;
        int Find(string rel) { int cur = rootNode; foreach (var part in rel.Split((char)92)) { int f = -1; foreach (var c in t.Children(cur)) if (t.NameString(c).Equals(part, StringComparison.OrdinalIgnoreCase)) { f = c; break; } if (f < 0) return -1; cur = f; } return cur; }
        Console.WriteLine($"Ban đầu: Trên đĩa gốc = {N(t[rootNode].Allocated)}, file = {N(t[rootNode].FileCount)}");
        // thay đổi thật trên đĩa
        File.WriteAllBytes(Path.Combine(fx, "sizes", "added1.bin"), new byte[200000]);
        File.WriteAllBytes(Path.Combine(fx, "sizes", "added2.bin"), new byte[10000]);
        File.Delete(Path.Combine(fx, "sizes", "f4096.bin"));
        File.WriteAllBytes(Path.Combine(fx, "hl", "dirA", "file.bin"), new byte[20000]);   // đổi nội dung file hard link
        // quét lại hai nhánh
        foreach (var rel in new[] { "sizes", "hl" })
        {
            int n = Find(rel);
            var sw = Stopwatch.StartNew();
            var job = ScanJob.StartRescan(r, n); job.Completion.Wait();
            Console.WriteLine($"  Quét lại nhánh '{rel}' trong {sw.ElapsedMilliseconds} ms");
        }
        // đối chiếu với một lần quét MỚI toàn bộ (nguồn độc lập về mặt quy trình)
        var fresh = ScanJob.Run(new[] { fx });
        Row("Trên đĩa gốc sau quét lại nhánh vs quét mới toàn bộ", N(fresh.Tree[fresh.Roots[0].Node].Allocated), N(t[rootNode].Allocated));
        Row("Kích thước gốc", N(fresh.Tree[fresh.Roots[0].Node].Logical), N(t[rootNode].Logical));
        Row("Số file", N(fresh.Tree[fresh.Roots[0].Node].FileCount), N(t[rootNode].FileCount));
        Row("Số thư mục", N(fresh.Tree[fresh.Roots[0].Node].DirCount), N(t[rootNode].DirCount));
        Row("Liên kết cứng không chủ", N(fresh.HardLinkNonOwnerCount), N(r.HardLinkNonOwnerCount));
        var d = SnapshotComparer.Compare(fresh, r);
        Console.WriteLine($"So sánh cây quét-lại với cây quét-mới: {d.Entries.Count} khác biệt (kỳ vọng 0)");
        foreach (var e in d.Entries.Take(5)) Console.WriteLine($"   {e.Status} {e.Path} Δ{N(e.DeltaAllocated)}");
        Row("Loại file: tổng số file theo thống kê", N(fresh.TypeStats.Sum(x => x.Count)), N(r.TypeStats.Sum(x => x.Count)));
        foreach (var c in r.Consistency) Console.WriteLine($"   {(c.Ok ? "ĐẠT " : "LỆCH")} {c.Name}");
        foreach (var n in r.Notes) Console.WriteLine("   ghi chú: " + n);
    }

    // ---------------------------------------------------------------- 11. recycle (UC-13)
    public static void Recycle(string root)
    {
        string fx = BuildFixture(root);
        var r = ScanJob.Run(new[] { fx });
        var t = r.Tree; int rootNode = r.Roots[0].Node;
        int Find(string rel) { int cur = rootNode; foreach (var part in rel.Split((char)92)) { int f = -1; foreach (var c in t.Children(cur)) if (t.NameString(c).Equals(part, StringComparison.OrdinalIgnoreCase)) { f = c; break; } if (f < 0) return -1; cur = f; } return cur; }
        void Est(string label, params string[] rels)
        {
            var nodes = rels.Select(Find).ToList();
            var e = RecycleBin.Estimate(r, nodes);
            Console.WriteLine($"  {label,-52} chọn {N(e.SelectedAllocated),10} B trên đĩa | giải phóng ngay {N(e.FreedNow)} | sau dọn Thùng rác {N(e.FreedAfterEmpty),10} | hard link nơi khác: {e.SharedHardLinkFiles} | đường dẫn dài nhất {e.MaxPathLength}");
        }
        Console.WriteLine("Ước tính dung lượng giải phóng (file.bin = 150.000 B / 151.552 B trên đĩa, có 3 liên kết: dirA, dirB và 1 NGOÀI vùng quét):");
        Est("chỉ hl\\dirB\\link.bin (liên kết không chủ)", "hl\\dirB\\link.bin");
        Est("cả thư mục hl (2/3 liên kết trong vùng chọn)", "hl");
        Est("thư mục sizes (không hard link)", "sizes");
        Est("long (đường dẫn > 260)", "long");
        Console.WriteLine("\nĐiều kiện xoá (CheckEligibility):");
        void Elig(string label, string[] paths, long logical, int maxLen) { var (ok, why) = RecycleBin.CheckEligibility(paths, logical, maxLen); Console.WriteLine($"  {(ok ? "CHO PHÉP " : "TỪ CHỐI  ")} {label,-44} {why}"); }
        Elig("file thường trên ổ D", new[] { Path.Combine(fx, "sizes", "f4097.bin") }, 4097, 100);
        Elig("đường dẫn dài 330 ký tự", new[] { Path.Combine(fx, "long") }, 4311, 330);
        Elig("dung lượng vượt giới hạn Thùng rác (giả lập 1 PiB)", new[] { Path.Combine(fx, "sizes", "f4097.bin") }, 1L << 50, 100);
        Elig("vị trí bảo vệ C:\\Windows", new[] { @"C:\Windows" }, 1, 10);
        Console.WriteLine("\nXoá THẬT vào Thùng rác trên fixture:");
        string victim = Path.Combine(fx, "sizes", "f4097.bin"), victim2 = Path.Combine(fx, "sizes", "f4095.bin");
        int nv = Find("sizes\\f4097.bin"), nv2 = Find("sizes\\f4095.bin");
        long before = t[rootNode].Allocated;
        File.WriteAllBytes(Path.Combine(root, "locked.bin"), new byte[1000]);
        var results = RecycleBin.Delete(new[] { victim, victim2, Path.Combine(fx, "khong-ton-tai.bin"), @"C:\Windows\System32", Path.Combine(root, "locked.bin") });
        // (locked.bin chưa bị khoá — chỉ thử đường đi bình thường)
        foreach (var res in results) Console.WriteLine($"  {res.Outcome,-9} {res.Path}  → {res.Detail}");
        Console.WriteLine($"  Còn trên đĩa? f4097: {File.Exists(victim)}  f4095: {File.Exists(victim2)}");
        TreeEditor.RemoveNodes(r, new[] { nv, nv2 }, "Đã đưa vào Thùng rác");
        Row("Gốc Trên đĩa giảm đúng (8.192 + 4.096)", N(before - 8192 - 4096), N(t[rootNode].Allocated));
        foreach (var c in r.Consistency) Console.WriteLine($"   {(c.Ok ? "ĐẠT " : "LỆCH")} {c.Name}");
        // khoá file thật rồi thử xoá → Locked + tên tiến trình (ER-10)
        string lockedFile = Path.Combine(fx, "sizes", "f1048576.bin");
        using (var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var lr = RecycleBin.Delete(new[] { lockedFile });
            Console.WriteLine($"  Khoá file rồi xoá: {lr[0].Outcome} — {lr[0].Detail}");
        }
        Console.WriteLine("\nXác nhận có trong Thùng rác của ổ D: (SHQueryRecycleBin) — xem PowerShell bên ngoài.");
    }

    // ---------------------------------------------------------------- 15. misc: ER-07, ER-15, ER-41, ER-42, UC-54, KT-40
    public static void Misc(string root)
    {
        Console.WriteLine("-- ER-07 Chế độ gọn: giới hạn bộ nhớ 1 MB, giữ 5 file/thư mục — tổng phải KHÔNG đổi --");
        string dir = @"D:\GitHub\qr-studio";
        var normal = ScanJob.Run(new[] { dir });
        var compact = ScanJob.Run(new[] { dir }, new ScanOptions { MemoryLimitBytes = 1_000_000, CompactKeepPerDir = 5 });
        var a = normal.Tree[normal.Roots[0].Node]; var b = compact.Tree[compact.Roots[0].Node];
        Row("Trên đĩa gốc (thường vs gọn)", N(a.Allocated), N(b.Allocated));
        Row("Kích thước gốc", N(a.Logical), N(b.Logical));
        Row("Số file", N(a.FileCount), N(b.FileCount));
        int agg = Enumerable.Range(1, compact.Tree.Count - 1).Count(i => compact.Tree[i].Parent >= 0 && compact.Tree[i].Has(NodeFlags.Aggregate));
        Console.WriteLine($"  Chế độ gọn bật: {compact.CompactModeUsed}; nút gộp '<N file khác>': {agg}; số nút {compact.Tree.Count:N0} vs {normal.Tree.Count:N0}");
        Console.WriteLine($"  Nhất quán (gọn): {string.Join(" ", compact.Consistency.Select(c => c.Ok ? "ĐẠT" : "LỆCH"))}");

        Console.WriteLine("\n-- ER-41 cài đặt hỏng → đổi tên .bad + mặc định; ER-42 thư mục dữ liệu không ghi được --");
        string tmp = Path.Combine(root, "appdata-test"); try { Directory.Delete(tmp, true); } catch { }
        FolderSizePro.Storage.AppPaths.Override(tmp);
        File.WriteAllText(FolderSizePro.Storage.AppPaths.SettingsFile, "{ not json ,,, ");
        var s = FolderSizePro.Storage.SettingsStore.Load();
        Console.WriteLine($"  Đọc file hỏng: không crash; có .bad = {File.Exists(FolderSizePro.Storage.AppPaths.SettingsFile + ".bad")}; cảnh báo = {FolderSizePro.Storage.SettingsStore.LastWarning}; TopN mặc định = {s.TopN}");
        FolderSizePro.Storage.AppPaths.Override(@"C:\Windows\System32\config\khong-ghi-duoc");
        Console.WriteLine($"  AppPaths.Writable = {FolderSizePro.Storage.AppPaths.Writable} ({FolderSizePro.Storage.AppPaths.WritableError}); Save() = {FolderSizePro.Storage.SettingsStore.Save(new FolderSizePro.Storage.AppSettings())}; Log không ném lỗi: ", false);
        FolderSizePro.Storage.Log.Info("thử"); Console.WriteLine("đúng");

        Console.WriteLine("\n-- UC-54 xoay vòng log (10 file × 2 MB) --");
        FolderSizePro.Storage.AppPaths.Override(tmp);
        for (int i = 0; i < 40; i++) FolderSizePro.Storage.Log.Info(new string('x', 200_000));
        foreach (var f in Directory.GetFiles(FolderSizePro.Storage.AppPaths.Logs).OrderBy(x => x)) Console.WriteLine($"   {Path.GetFileName(f)} {new FileInfo(f).Length:N0} B");

        Console.WriteLine("\n-- ER-15 / UC-53 khôi phục phiên: đánh dấu, autosave giữa chừng, 'sập', rồi kiểm tra --");
        var job = ScanJob.Start(new[] { @"D:\GitHub" });
        FolderSizePro.Storage.SessionStore.MarkScanning(new[] { @"D:\GitHub" });
        Thread.Sleep(1500);
        FolderSizePro.Storage.SessionStore.Autosave(job.Result, job);
        Thread.Sleep(2500);
        long filesAtSave = job.Snapshot().Files;
        job.Cancel(); job.Completion.Wait();            // "sập": marker còn đó, không gọi Clear()
        var chk = FolderSizePro.Storage.SessionStore.Check();
        Console.WriteLine($"  Phát hiện phiên gián đoạn: {(chk != null)}; gốc = {string.Join(",", chk?.Roots ?? Array.Empty<string>())}; có partial = {chk?.HasPartial}");
        var rec = FolderSizePro.Storage.SessionStore.LoadPartial();
        if (rec != null)
        {
            int pending = Enumerable.Range(1, rec.Tree.Count - 1).Count(i => rec.Tree[i].Parent >= 0 && rec.Tree[i].Has(NodeFlags.Pending));
            Console.WriteLine($"  Khôi phục: {rec.Completeness}; {N(rec.Tree[rec.Roots[0].Node].FileCount)} file đã đo (lúc autosave ~{N(filesAtSave)}); thư mục 'chưa quét': {N(pending)}; nhất quán: {string.Join(" ", rec.Consistency.Select(c => c.Ok ? "ĐẠT" : "LỆCH"))}");
        }
        FolderSizePro.Storage.SessionStore.Clear();
        Console.WriteLine($"  Sau Clear(): còn marker = {FolderSizePro.Storage.SessionStore.Check() != null}");

        Console.WriteLine("\n-- KT-40 các assembly tham chiếu System.Net / Http / Sockets? --");
        foreach (var asm in new[] { typeof(ScanJob).Assembly, typeof(Verify).Assembly })
        {
            var refs = asm.GetReferencedAssemblies().Select(r => r.Name).Where(n => n!.StartsWith("System.Net") || n.Contains("Http") || n.Contains("Socket")).ToList();
            Console.WriteLine($"  {asm.GetName().Name}: {(refs.Count == 0 ? "KHÔNG tham chiếu" : string.Join(", ", refs))}");
        }
        string appDll = Path.Combine(AppContext.BaseDirectory, "FolderSizePro.dll");
        if (File.Exists(appDll)) Console.WriteLine($"  FolderSizePro.dll (App): {string.Join(", ", System.Reflection.Assembly.LoadFrom(appDll).GetReferencedAssemblies().Select(r => r.Name).Where(n => n!.StartsWith("System.Net") || n.Contains("Http")).DefaultIfEmpty("KHÔNG tham chiếu"))}");
        try { Directory.Delete(tmp, true); } catch { }
    }

    // ---------------------------------------------------------------- 14. watch (UC-50)
    public static void Watch(string root)
    {
        string fx = BuildFixture(root);
        var r = ScanJob.Run(new[] { fx });
        var changed = new System.Collections.Concurrent.ConcurrentBag<string>(); string? lost = null;
        using var w = new FolderSizePro.Watch.ChangeWatcher(new[] { fx }, 300);
        w.DirectoriesChanged += d => { foreach (var x in d) changed.Add(x); };
        w.Lost += m => lost = m;
        Thread.Sleep(500);
        File.WriteAllBytes(Path.Combine(fx, "sizes", "live1.bin"), new byte[50000]);
        Directory.CreateDirectory(Path.Combine(fx, "newdir", "deep")); File.WriteAllBytes(Path.Combine(fx, "newdir", "deep", "x.bin"), new byte[9000]);
        File.Delete(Path.Combine(fx, "sizes", "f4096.bin"));
        Thread.Sleep(1500);
        Console.WriteLine("Thư mục được báo đổi (sau debounce 300 ms): " + string.Join(" | ", changed.Distinct().Select(d => Path.GetRelativePath(fx, d))));
        // áp dụng cập nhật: quét lại nút gần nhất tồn tại
        var t = r.Tree; int rootNode = r.Roots[0].Node;
        int Find(string path) { string rel = Path.GetRelativePath(fx, path); if (rel == ".") return rootNode; int cur = rootNode; foreach (var part in rel.Split((char)92)) { int f = -1; foreach (var c in t.Children(cur)) if (t.NameString(c).Equals(part, StringComparison.OrdinalIgnoreCase)) { f = c; break; } if (f < 0) return -1; cur = f; } return cur; }
        var nodes = new HashSet<int>();
        foreach (var d in changed.Distinct()) { string p = d; int n; while ((n = Find(p)) < 0) p = Path.GetDirectoryName(p)!; nodes.Add(n); }
        foreach (var n in nodes.ToList()) for (int p = t[n].Parent; p > 0; p = t[p].Parent) if (nodes.Contains(p)) { nodes.Remove(n); break; }
        foreach (var n in nodes) { var j = ScanJob.StartRescan(r, n); j.Completion.Wait(); Console.WriteLine($"  quét lại nút '{t.FullPath(n)}'"); }
        var fresh = ScanJob.Run(new[] { fx });
        var d2 = SnapshotComparer.Compare(fresh, r);
        Row("Trên đĩa gốc: cây cập nhật trực tiếp vs quét mới", N(fresh.RootAllocated()), N(r.RootAllocated()));
        Console.WriteLine($"Khác biệt cây: {d2.Entries.Count} (kỳ vọng 0). Báo mất theo dõi: {lost ?? "không"}");
        // thử tràn bộ đệm: 60k thao tác nhanh khi bộ xử lý bị chặn
        using var w2 = new FolderSizePro.Watch.ChangeWatcher(new[] { fx }, 5000); string? lost2 = null; w2.Lost += m => lost2 = m;
        string flood = Path.Combine(fx, "flood"); Directory.CreateDirectory(flood);
        Parallel.For(0, 60000, i => File.WriteAllBytes(Path.Combine(flood, "f" + i + ".t"), Array.Empty<byte>()));
        Thread.Sleep(1500);
        Console.WriteLine($"Tạo 60.000 file cực nhanh → báo mất theo dõi (ER-40): {(lost2 != null ? "CÓ — " + lost2 : "không xảy ra tràn trong lần thử này")}");
        try { Directory.Delete(flood, true); } catch { }
    }

    // ---------------------------------------------------------------- 12. mftparser (không cần admin)
    public static void MftParserSelfTest()
    {
        Console.WriteLine("Dựng bản ghi MFT tổng hợp 1024 byte (fixup, SI, FILE_NAME, DATA phi cư trú có run thưa, ADS thường trú) rồi parse:");
        const int frs = 1024; var buf = (byte*)NativeMemory.AlignedAlloc(frs, 4096); new Span<byte>(buf, frs).Clear();
        buf[0] = (byte)'F'; buf[1] = (byte)'I'; buf[2] = (byte)'L'; buf[3] = (byte)'E';
        *(ushort*)(buf + 4) = 48; *(ushort*)(buf + 6) = 3;
        *(ushort*)(buf + 16) = 7; *(ushort*)(buf + 18) = 1; *(ushort*)(buf + 20) = 56; *(ushort*)(buf + 22) = 1;
        *(uint*)(buf + 28) = frs; *(ulong*)(buf + 32) = 0;
        int p = 56;
        *(uint*)(buf + p) = 0x10; *(uint*)(buf + p + 4) = 24 + 48; buf[p + 8] = 0; *(uint*)(buf + p + 16) = 48; *(ushort*)(buf + p + 20) = 24;
        *(long*)(buf + p + 24 + 8) = 133_500_000_000_000_000L; *(uint*)(buf + p + 24 + 32) = 0x20 | 0x2;
        p += 72;
        string nm = "demo.bin"; int fnLen = 66 + nm.Length * 2; int alen = (24 + fnLen + 7) & ~7;
        *(uint*)(buf + p) = 0x30; *(uint*)(buf + p + 4) = (uint)alen; *(uint*)(buf + p + 16) = (uint)fnLen; *(ushort*)(buf + p + 20) = 24;
        *(ulong*)(buf + p + 24) = (5UL << 48) | 5; buf[p + 24 + 64] = (byte)nm.Length; buf[p + 24 + 65] = 1;
        for (int i = 0; i < nm.Length; i++) *(char*)(buf + p + 24 + 66 + i * 2) = nm[i];
        p += alen;
        byte[] runs = { 0x11, 10, 100, 0x01, 5, 0x11, 3, 50, 0 };
        int dlen = (64 + runs.Length + 7) & ~7;
        *(uint*)(buf + p) = 0x80; *(uint*)(buf + p + 4) = (uint)dlen; buf[p + 8] = 1; *(long*)(buf + p + 16) = 0; *(ushort*)(buf + p + 32) = 64;
        *(long*)(buf + p + 40) = 18 * 4096; *(long*)(buf + p + 48) = 1_000_000; *(long*)(buf + p + 56) = 1_000_000;
        for (int i = 0; i < runs.Length; i++) buf[p + 64 + i] = runs[i];
        p += dlen;
        string an = "Zone.Identifier"; int valOff = (24 + an.Length * 2 + 7) & ~7; int adsLen = valOff + 32;
        *(uint*)(buf + p) = 0x80; *(uint*)(buf + p + 4) = (uint)adsLen; buf[p + 8] = 0; buf[p + 9] = (byte)an.Length; *(ushort*)(buf + p + 10) = 24;
        *(uint*)(buf + p + 16) = 26; *(ushort*)(buf + p + 20) = (ushort)valOff;
        for (int i = 0; i < an.Length; i++) *(char*)(buf + p + 24 + i * 2) = an[i];
        p += adsLen;
        *(uint*)(buf + p) = 0xFFFFFFFF; *(uint*)(buf + 24) = (uint)(p + 8);
        *(ushort*)(buf + 48) = 0xBEEF; *(ushort*)(buf + 50) = *(ushort*)(buf + 510); *(ushort*)(buf + 52) = *(ushort*)(buf + 1022);
        *(ushort*)(buf + 510) = 0xBEEF; *(ushort*)(buf + 1022) = 0xBEEF;
        bool okFix = MftParser.ApplyFixups(buf, frs);
        Console.WriteLine($"  ApplyFixups = {okFix}");
        var co = new MftChunkOut(); var r = new MftRec();
        MftParser.Parse(buf, frs, 4096, co, 0, ref r);
        Console.WriteLine($"  State={r.State} (kỳ vọng 1) Seq={r.Seq} (7) Links={r.Links} (1) FileAttr=0x{r.FileAttr:X} (0x22) MTime={r.MTime:N0} (133.500.000.000.000.000) Corrupt={r.Corrupt}");
        Console.WriteLine($"  Tên: {co.Names.Count} (1) → '{new string(co.Pool, co.Names[0].PoolOffset, co.Names[0].Len)}' parent=0x{co.Names[0].ParentRef:X} (0x0005000000000005)");
        foreach (var s in co.Streams)
            Console.WriteLine($"  Luồng '{(s.NameLen == 0 ? "(chính)" : new string(co.Pool, s.NameOffset, s.NameLen))}': Size={s.Size:N0} Clusters(byte)={s.Clusters:N0} Resident={s.Resident} HasSize={s.HasSize}  — kỳ vọng chính: 1.000.000 / {13 * 4096:N0} (10+3 cluster, 5 cluster thưa KHÔNG tính); ADS: 26 / 0 / 32");
        *(ushort*)(buf + 510) = 0x1234; Console.WriteLine($"  Fixup khi 2 byte cuối sector bị đổi → ApplyFixups = {MftParser.ApplyFixups(buf, frs)} (kỳ vọng False — bản ghi hỏng bị bỏ, ER-21)");
        var decoded = MftParser.DecodeRuns(buf + 56 + 72 + alen + 64, buf + 56 + 72 + alen + 64 + runs.Length);
        Console.WriteLine("  Run list giải mã: " + string.Join(", ", decoded.Select(x => $"(vcn {x.Vcn}, lcn {x.Lcn}, len {x.Len})")) + "  — kỳ vọng (0,100,10) (10,-1,5) (15,150,3)");
        NativeMemory.AlignedFree(buf);
    }

    // ---------------------------------------------------------------- 13. mft (CẦN ADMIN): đối chiếu hai chế độ trên cả ổ
    public static void Mft(string drive)
    {
        drive = drive.TrimEnd('\\', ':') + ":\\";
        bool admin = FolderSizePro.Analysis.Reconciler.IsAdmin();
        Console.WriteLine($"Quyền Administrator: {admin}");
        if (!admin) { Console.WriteLine("→ Mục này CẦN terminal Administrator. Mở PowerShell bằng 'Run as administrator' rồi chạy lại."); return; }
        var sw = Stopwatch.StartNew();
        var opt = new ScanOptions { Mode = ScanMode.Mft };
        var plan = ScanPlanner.Plan(new[] { drive }, opt);
        var job = ScanPlanner.Start(new[] { drive }, opt, plan);
        while (!job.Completion.Wait(1000)) { var p = job.Snapshot(); Console.Write($"\r  MFT: {p.CurrentPath}   ".PadRight(110)); }
        Console.WriteLine();
        var mft = job.Result; long mftMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"MFT: {mftMs:N0} ms · chế độ={mft.ModeUsed} · {mft.Completeness} · ghi chú: {string.Join(" | ", mft.Notes)}");
        sw.Restart();
        var normal = ScanJob.Run(new[] { drive }, new ScanOptions());
        Console.WriteLine($"Thường: {sw.ElapsedMilliseconds:N0} ms (cùng quyền Administrator → đọc được cả System Volume Information)\n");
        var a = normal.Tree[normal.Roots[0].Node]; var b = mft.Tree[mft.Roots[0].Node];
        Row("Số file (đã đặt vào cây)", N(a.FileCount), N(b.FileCount));
        Row("Số thư mục", N(a.DirCount), N(b.DirCount));
        Row("Kích thước (logical) gốc", N(a.Logical), N(b.Logical));
        Row("Trên đĩa gốc", N(a.Allocated), N(b.Allocated));
        Row("Liên kết cứng không chủ", N(normal.HardLinkNonOwnerCount), N(mft.HardLinkNonOwnerCount));
        Console.WriteLine("\nKhác biệt theo nhánh (chế độ thường → MFT), 30 dòng đầu theo |Δ trên đĩa|. MFT thêm các metafile $MFT/$LogFile/$Bitmap/$Extend… nên 'Mới' ở gốc là ĐÚNG; mọi thư mục của người dùng phải gần như không lệch:");
        var d = SnapshotComparer.Compare(normal, mft);
        foreach (var e in d.Entries.Take(30)) Console.WriteLine($"   {e.Status,-8} Δ {N(e.DeltaAllocated),16}  (A {N(e.AllocatedA),16} → B {N(e.AllocatedB),16})  {e.Path}");
        Console.WriteLine($"   … tổng {d.Entries.Count:N0} khác biệt; Δ gốc = {N(d.TotalDeltaAllocated)} B trên đĩa, {N(d.TotalDeltaLogical)} B kích thước");
        // So riêng DỮ LIỆU NGƯỜI DÙNG: bỏ các metafile NTFS ở gốc ($MFT, $LogFile, $Extend…) mà chế độ thường không thấy được
        static bool IsMeta(string path, string drive) { var rel = path.Length > drive.Length ? path[drive.Length..] : ""; var first = rel.Split('\\')[0]; return first.StartsWith('$') && !first.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase); }
        (long L, long A, long F, long D) UserTotals(ScanResult r)
        {
            var t = r.Tree; int root = r.Roots[0].Node; long l = 0, al = 0, f = 0, dd = 0;
            foreach (var c in t.Children(root))
            {
                if (t[c].Parent == -2 || IsMeta(t.FullPath(c), drive)) continue;
                l += t[c].Logical; al += t[c].Allocated;
                if (t[c].IsDir) { f += t[c].FileCount; dd += t[c].DirCount + 1; } else f++;
            }
            return (l, al + t[root].OwnAllocated, f, dd);
        }
        var ua = UserTotals(normal); var ub = UserTotals(mft);
        Console.WriteLine("\nCHỈ DỮ LIỆU NGƯỜI DÙNG (bỏ metafile $… ở gốc):");
        Row("  Số file", N(ua.F), N(ub.F)); Row("  Số thư mục", N(ua.D), N(ub.D));
        Row("  Kích thước", N(ua.L), N(ub.L)); Row("  Trên đĩa", N(ua.A), N(ub.A));
        // hai phần chỉ MFT thấy được (đã kiểm chứng trên ổ C: thật): vùng diff VSS bị volsnap giấu, luồng nội bộ Cloud Files bị cldflt giấu
        var sviE = d.Entries.FirstOrDefault(e => e.Path.TrimEnd('\\').Equals(Path.Combine(drive, "System Volume Information"), StringComparison.OrdinalIgnoreCase));
        long sviL = sviE?.DeltaLogical ?? 0, sviA = sviE?.DeltaAllocated ?? 0, cf = mft.MftExtras.Values.Sum(x => x.CloudInternalBytes);
        Console.WriteLine($"  Phần chỉ chế độ MFT thấy được: vùng diff VSS trong System Volume Information Δkt {N(sviL)} Δđĩa {N(sviA)} · luồng nội bộ Cloud Files (OneDrive) Δđĩa {N(cf)}");
        Row("  Kích thước, trừ phần trên", N(ua.L), N(ub.L - sviL)); Row("  Trên đĩa, trừ phần trên", N(ua.A), N(ub.A - sviA - cf));
        var userDiffs = d.Entries.Where(e => !IsMeta(e.Path, drive)).ToList();
        Console.WriteLine($"  Khác biệt ở dữ liệu người dùng: {userDiffs.Count:N0} mục (file: {userDiffs.Count(e => !e.IsDir):N0})");
        Console.WriteLine("  15 FILE lệch nhiều nhất theo |Δ kích thước| (kèm giờ sửa cuối — nếu vừa sửa trong lúc quét thì là thay đổi thật, không phải lỗi đo):");
        foreach (var e in userDiffs.Where(e => !e.IsDir).OrderByDescending(e => Math.Abs(e.DeltaLogical)).ThenByDescending(e => Math.Abs(e.DeltaAllocated)).Take(15))
        {
            string when; try { when = File.Exists(e.Path) ? File.GetLastWriteTime(e.Path).ToString("HH:mm:ss") : "không còn"; } catch { when = "?"; }
            Console.WriteLine($"     {e.Status,-7} Δkt {N(e.DeltaLogical),14}  Δđĩa {N(e.DeltaAllocated),12}  sửa {when}  {e.Path}");
        }
        Console.WriteLine("  15 THƯ MỤC lệch nhiều nhất theo |Δ trên đĩa| (không tính thư mục cha của chúng):");
        var dirDiffs = userDiffs.Where(e => e.IsDir).ToList();
        var leafDirs = dirDiffs.Where(x => !dirDiffs.Any(y => y.Path.Length > x.Path.Length && y.Path.StartsWith(x.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && y.DeltaAllocated == x.DeltaAllocated)).ToList();
        foreach (var e in leafDirs.OrderByDescending(e => Math.Abs(e.DeltaAllocated)).Take(15))
            Console.WriteLine($"     {e.Status,-7} Δđĩa {N(e.DeltaAllocated),12}  Δkt {N(e.DeltaLogical),12}  {e.Path}");
        Console.WriteLine("\nBảng đối chiếu ổ (MFT):");
        foreach (var rc in mft.Reconcile)
        {
            Console.WriteLine($"  Used {N(rc.Used)}  | đã đo trong cây {N(rc.TreeAllocated)} | chưa giải thích {N(rc.Unexplained)} ({rc.UnexplainedFraction:P3})");
            foreach (var row in rc.Rows) Console.WriteLine($"     • {row.Label,-70} {(row.Bytes is long x ? N(x) : row.Status.ToString()),20}  {row.Detail}");
        }
        Console.WriteLine("\nBảng đối chiếu ổ (thường, quyền Administrator):");
        foreach (var rc in normal.Reconcile) Console.WriteLine($"  Used {N(rc.Used)}  | đã đo trong cây {N(rc.TreeAllocated)} | chưa giải thích {N(rc.Unexplained)} ({rc.UnexplainedFraction:P3})");
        Console.WriteLine("\nNhất quán (MFT):"); foreach (var c in mft.Consistency) Console.WriteLine($"   {(c.Ok ? "ĐẠT " : "LỆCH")} {c.Name}");
    }

    // ---------------------------------------------------------------- 9. export
    public static void Export(string root)
    {
        string fx = Path.Combine(root, "verify");
        if (!Directory.Exists(fx)) BuildFixture(root);
        var r = ScanJob.Run(new[] { fx });
        string jp = Path.Combine(root, "verify.json"), cp = Path.Combine(root, "verify.csv");
        using (var fs = File.Create(jp)) ResultExporter.WriteJson(r, fs, 5, 10);
        using (var fs = File.Create(cp)) ResultExporter.WriteCsv(r, fs, 5);
        using var doc = JsonDocument.Parse(File.ReadAllText(jp));
        var jr = doc.RootElement;
        Console.WriteLine($"JSON: {new FileInfo(jp).Length:N0} B, đọc lại được. roots[0].allocated = {N(jr.GetProperty("roots")[0].GetProperty("allocated").GetInt64())} (máy quét {N(r.RootAllocated())}); nodes = {jr.GetProperty("nodes").GetArrayLength()}; completedUtc = {jr.GetProperty("completedUtc").GetString()}");
        var lines = File.ReadAllLines(cp);
        Console.WriteLine($"CSV: {lines.Length - 2} dòng dữ liệu; dòng đầu: {lines[0]}");
        Console.WriteLine("     " + lines[2]);
        Console.WriteLine("     " + lines.First(l => l.Contains("host.txt")));
    }
}
