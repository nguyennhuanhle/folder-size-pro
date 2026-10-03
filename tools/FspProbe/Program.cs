// FspProbe — R0 "phòng thí nghiệm độ chính xác".
// Mục đích: trả lời bằng số thật các câu hỏi mở trong plan.md mục 7 (R0) trước khi viết máy quét chính.
//   fixtures <root>   tạo bộ file mẫu (kích thước biên, hard link, nén, sparse, ADS, junction vòng, đường dẫn dài…)
//   probe <dir>       với mỗi file in: giá trị từ danh sách thư mục | FileStandardInformation | FileCompressionInformation
//                     | "sự thật" cộng từ extent (FSCTL_GET_RETRIEVAL_POINTERS) | các ADS
//   bench <dir>       đo chi phí: chỉ liệt kê thư mục vs liệt kê + mở từng file (+ ADS)
//   cloud <dir>       liệt kê file/thư mục placeholder đám mây, in cờ trước/sau khi quét để kiểm tra không bị tải xuống
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FolderSizePro.Native;

return Probe.Run(args);

static unsafe class Probe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetDiskFreeSpaceW(string root, out uint spc, out uint bps, out uint free, out uint total);

    public static int Run(string[] a)
    {
        if (a.Length < 2) { Console.WriteLine("dùng: FspProbe fixtures|probe|bench|cloud <đường dẫn>"); return 2; }
        return a[0] switch
        {
            "fixtures" => Fixtures(a[1]),
            "probe" => ProbeDir(a[1]),
            "bench" => Bench(a[1]),
            "cloud" => Cloud(a[1]),
            "ntfsdata" => NtfsData(a[1]),
            "mftrec" => MftRec(a[1], ulong.Parse(a[2])),
            _ => 2,
        };
    }

    // ------------------------------------------------------------------ fixtures
    static int Fixtures(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) { try { Directory.Delete(NtApi.ToExtendedPath(root), true); } catch { } }
        Directory.CreateDirectory(root);
        void W(string rel, int n) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, Enumerable.Range(0, n).Select(i => (byte)(i * 31 + 7)).ToArray()); }
        foreach (var n in new[] { 0, 1, 100, 700, 4095, 4096, 4097, 1048576 }) W($"sizes\\f{n}.bin", n);
        // hard link
        W("hl\\dirA\\file.bin", 100000);
        Directory.CreateDirectory(Path.Combine(root, "hl", "dirB"));
        Cmd($"fsutil hardlink create \"{Path.Combine(root, "hl", "dirB", "link.bin")}\" \"{Path.Combine(root, "hl", "dirA", "file.bin")}\"");
        // nén NTFS (dữ liệu lặp → nén tốt)
        var comp = Path.Combine(root, "comp", "text.txt"); Directory.CreateDirectory(Path.GetDirectoryName(comp)!);
        File.WriteAllText(comp, string.Concat(Enumerable.Repeat("Folder Size Pro đo chính xác từng byte. ", 40000)));
        Cmd($"compact /c \"{comp}\"");
        // CompactOS / WOF
        var wof = Path.Combine(root, "wof", "text.txt"); Directory.CreateDirectory(Path.GetDirectoryName(wof)!);
        File.WriteAllText(wof, string.Concat(Enumerable.Repeat("CompactOS WOF lzx test line. ", 60000)));
        Cmd($"compact /c /exe:lzx \"{wof}\"");
        // sparse: 100 MiB logical, chỉ ghi 1 MiB đầu
        var sp = Path.Combine(root, "sparse", "s.bin"); Directory.CreateDirectory(Path.GetDirectoryName(sp)!);
        File.WriteAllBytes(sp, new byte[1]); Cmd($"fsutil sparse setflag \"{sp}\"");
        using (var fs = new FileStream(sp, FileMode.Open, FileAccess.Write)) { var b = new byte[1048576]; new Random(1).NextBytes(b); fs.Write(b); fs.SetLength(100L * 1024 * 1024); }
        // ADS
        var ads = Path.Combine(root, "ads", "host.txt"); Directory.CreateDirectory(Path.GetDirectoryName(ads)!);
        File.WriteAllBytes(ads, new byte[10]);
        File.WriteAllBytes(ads + ":big", new byte[50000]);
        File.WriteAllBytes(ads + ":Zone.Identifier", Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\n"));
        // junction vòng + symlink thư mục
        Directory.CreateDirectory(Path.Combine(root, "loop", "real"));
        W("loop\\real\\inside.bin", 5000);
        Cmd($"mklink /j \"{Path.Combine(root, "loop", "real", "back")}\" \"{Path.Combine(root, "loop")}\"");
        // đường dẫn dài > 260 và tên "xấu"
        var longDir = Path.Combine(root, "long", new string('a', 120), new string('b', 120), new string('c', 60));
        Directory.CreateDirectory(NtApi.ToExtendedPath(longDir));
        File.WriteAllBytes(NtApi.ToExtendedPath(Path.Combine(longDir, "deep.bin")), new byte[3000]);
        File.WriteAllBytes(NtApi.ToExtendedPath(Path.Combine(root, "long", "trailing-space ")), new byte[1234]);
        Console.WriteLine($"Đã tạo fixture tại {root}");
        return 0;
    }

    static void Cmd(string line)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c " + line) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!; var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
        if (p.ExitCode != 0) Console.WriteLine($"  [cảnh báo] `{line}` thoát {p.ExitCode}: {o.Trim()}");
    }

    // ------------------------------------------------------------------ probe
    record Entry(string Path, string Name, uint Attr, long DirEof, long DirAlloc, uint Tag, ulong IdLo, ulong IdHi);

    static List<Entry> List(string dirPath)
    {
        var list = new List<Entry>();
        int st = NtApi.Open(NtApi.ToNtPath(NtApi.ToExtendedPath(dirPath)), IntPtr.Zero, NtApi.FILE_LIST_DIRECTORY | NtApi.SYNCHRONIZE,
            NtApi.FILE_DIRECTORY_FILE | NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var h);
        if (st < 0) { Console.WriteLine($"  [không mở được {dirPath}: {NtApi.StatusText(st)}]"); return list; }
        var buf = (byte*)NativeMemory.Alloc(256 * 1024);
        try
        {
            bool restart = true;
            while (true)
            {
                st = NtApi.NtQueryDirectoryFile(h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _, buf, 256 * 1024, NtApi.FileIdExtdDirectoryInformation, 0, null, (byte)(restart ? 1 : 0));
                restart = false;
                if (st == NtApi.STATUS_NO_MORE_FILES) break;
                if (st < 0) { Console.WriteLine($"  [liệt kê lỗi: {NtApi.StatusText(st)}]"); break; }
                for (byte* p = buf; ;)
                {
                    var e = (NtApi.FILE_ID_EXTD_DIR_INFORMATION*)p;
                    var name = new string((char*)(p + NtApi.ExtdDirHeaderSize), 0, (int)e->FileNameLength / 2);
                    if (name != "." && name != "..")
                        list.Add(new Entry(System.IO.Path.Combine(dirPath, name), name, e->FileAttributes, e->EndOfFile, e->AllocationSize, e->ReparsePointTag, e->FileIdLow, e->FileIdHigh));
                    if (e->NextEntryOffset == 0) break;
                    p += e->NextEntryOffset;
                }
            }
        }
        finally { NativeMemory.Free(buf); NtApi.NtClose(h); }
        return list;
    }

    static long ClusterSize(string path)
    {
        var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path))!;
        GetDiskFreeSpaceW(root, out var spc, out var bps, out _, out _);
        return (long)spc * bps;
    }

    /// <summary>"Sự thật": cộng cluster thật sự được cấp phát theo extent (không tính lỗ sparse / phần chưa cấp).</summary>
    static long ExtentAllocated(string path, long cluster, out string note)
    {
        note = "";
        var h = NtApi.CreateFileW(NtApi.ToExtendedPath(path), NtApi.FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, NtApi.OPEN_EXISTING,
            NtApi.FILE_FLAG_BACKUP_SEMANTICS | NtApi.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (h == NtApi.INVALID_HANDLE_VALUE) { note = $"open lỗi {Marshal.GetLastWin32Error()}"; return -1; }
        try
        {
            long total = 0, startVcn = 0; var out_ = (byte*)NativeMemory.Alloc(64 * 1024);
            try
            {
                while (true)
                {
                    long inVcn = startVcn;
                    bool ok = NtApi.DeviceIoControl(h, 0x90073, &inVcn, 8, out_, 64 * 1024, out _, IntPtr.Zero);
                    int err = ok ? 0 : Marshal.GetLastWin32Error();
                    if (!ok && err != 234) { note = err == 38 ? "không có extent (thường trú trong MFT)" : $"ioctl lỗi {err}"; return err == 38 ? 0 : -1; }
                    uint count = *(uint*)out_; long prev = *(long*)(out_ + 8);
                    for (uint i = 0; i < count; i++)
                    {
                        long next = *(long*)(out_ + 16 + i * 16), lcn = *(long*)(out_ + 24 + i * 16);
                        if (lcn != -1) total += (next - prev) * cluster;
                        prev = next;
                    }
                    if (ok) break;
                    startVcn = prev;
                }
            }
            finally { NativeMemory.Free(out_); }
            return total;
        }
        finally { NtApi.CloseHandle(h); }
    }

    static string Sz(long v) => v < 0 ? "?" : v.ToString("N0");

    static int ProbeDir(string root)
    {
        root = Path.GetFullPath(root); long cluster = ClusterSize(root);
        Console.WriteLine($"Cluster = {cluster} byte. Cột: [DirEnum EOF/Alloc] [Std EOF/Alloc/Links] [CompressedSize] [EXTENT-thật] [ADS]");
        Walk(root, root, cluster);
        return 0;
    }

    static void Walk(string root, string dir, long cluster)
    {
        foreach (var e in List(dir).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(root, e.Path);
            bool isDir = (e.Attr & NtApi.ATTR_DIRECTORY) != 0, isRp = (e.Attr & NtApi.ATTR_REPARSE_POINT) != 0;
            string flags = (isDir ? "D" : "F") + (isRp ? $"R(tag {e.Tag:X8})" : "") + ((e.Attr & NtApi.ATTR_COMPRESSED) != 0 ? "C" : "") + ((e.Attr & NtApi.ATTR_SPARSE) != 0 ? "S" : "")
                + ((e.Attr & (NtApi.ATTR_RECALL_ON_DATA_ACCESS | NtApi.ATTR_RECALL_ON_OPEN)) != 0 ? "Cloud" : "");
            if (isDir)
            {
                Console.WriteLine($"[{flags}] {rel}\\");
                if (!isRp) Walk(root, e.Path, cluster);
                continue;
            }
            string std = "?", comp = "?", ext = "?", streams = "";
            int st = NtApi.Open(NtApi.ToNtPath(NtApi.ToExtendedPath(e.Path)), IntPtr.Zero, NtApi.FILE_READ_ATTRIBUTES | NtApi.SYNCHRONIZE,
                NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var h);
            if (st >= 0)
            {
                NtApi.FILE_STANDARD_INFORMATION si; NtApi.FILE_COMPRESSION_INFORMATION ci;
                if (NtApi.NtQueryInformationFile(h, out _, &si, (uint)sizeof(NtApi.FILE_STANDARD_INFORMATION), NtApi.FileStandardInformation) >= 0)
                    std = $"{Sz(si.EndOfFile)}/{Sz(si.AllocationSize)}/{si.NumberOfLinks}";
                if (NtApi.NtQueryInformationFile(h, out _, &ci, (uint)sizeof(NtApi.FILE_COMPRESSION_INFORMATION), NtApi.FileCompressionInformation) >= 0)
                    comp = Sz(ci.CompressedFileSize);
                var sb = (byte*)NativeMemory.Alloc(64 * 1024);
                try
                {
                    if (NtApi.NtQueryInformationFile(h, out _, sb, 64 * 1024, NtApi.FileStreamInformation) >= 0)
                    {
                        var parts = new List<string>();
                        for (byte* p = sb; ;)
                        {
                            uint next = *(uint*)p, nameLen = *(uint*)(p + 4); long ssz = *(long*)(p + 8), salloc = *(long*)(p + 16);
                            string sname = new((char*)(p + 24), 0, (int)nameLen / 2);
                            if (sname != "::$DATA")
                            {
                                long truth = ExtentAllocated(e.Path + sname.Replace(":$DATA", ""), cluster, out _);
                                parts.Add($"{sname} eof={Sz(ssz)} alloc={Sz(salloc)} extent={Sz(truth)}");
                            }
                            if (next == 0) break; p += next;
                        }
                        streams = string.Join(" | ", parts);
                    }
                }
                finally { NativeMemory.Free(sb); }
                NtApi.NtClose(h);
            }
            else std = $"mở lỗi: {NtApi.StatusText(st)}";
            long truthMain = ExtentAllocated(e.Path, cluster, out var note);
            ext = Sz(truthMain) + (note != "" ? $" ({note})" : "");
            Console.WriteLine($"[{flags}] {rel}\n    dir={Sz(e.DirEof)}/{Sz(e.DirAlloc)}  std={std}  compressed={comp}  EXTENT={ext}" + (streams != "" ? $"\n    ADS: {streams}" : ""));
        }
    }

    // ------------------------------------------------------------------ bench
    static int Bench(string root)
    {
        root = Path.GetFullPath(root);
        for (int round = 0; round < 2; round++)
        {
            var sw = Stopwatch.StartNew(); long files = 0, dirs = 0, bytes = 0;
            WalkCount(root, ref files, ref dirs, ref bytes, 0);
            Console.WriteLine($"[chỉ liệt kê]          {sw.ElapsedMilliseconds,7} ms  files={files:N0} dirs={dirs:N0} bytes={bytes:N0}");
            sw.Restart(); files = dirs = bytes = 0;
            WalkCount(root, ref files, ref dirs, ref bytes, 1);
            Console.WriteLine($"[liệt kê + mở + Std]   {sw.ElapsedMilliseconds,7} ms  files={files:N0} dirs={dirs:N0} alloc={bytes:N0}");
            sw.Restart(); files = dirs = bytes = 0;
            WalkCount(root, ref files, ref dirs, ref bytes, 2);
            Console.WriteLine($"[liệt kê + mở + Std + ADS] {sw.ElapsedMilliseconds,7} ms  files={files:N0} dirs={dirs:N0} alloc={bytes:N0}");
        }
        return 0;
    }

    static void WalkCount(string dir, ref long files, ref long dirs, ref long bytes, int mode)
    {
        int st = NtApi.Open(NtApi.ToNtPath(NtApi.ToExtendedPath(dir)), IntPtr.Zero, NtApi.FILE_LIST_DIRECTORY | NtApi.SYNCHRONIZE,
            NtApi.FILE_DIRECTORY_FILE | NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var h);
        if (st < 0) return;
        var subdirs = new List<string>();
        var buf = (byte*)NativeMemory.Alloc(256 * 1024); var sbuf = (byte*)NativeMemory.Alloc(16 * 1024);
        try
        {
            bool restart = true;
            while (true)
            {
                st = NtApi.NtQueryDirectoryFile(h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _, buf, 256 * 1024, NtApi.FileIdExtdDirectoryInformation, 0, null, (byte)(restart ? 1 : 0));
                restart = false;
                if (st < 0) break;
                for (byte* p = buf; ;)
                {
                    var e = (NtApi.FILE_ID_EXTD_DIR_INFORMATION*)p;
                    var nm = new ReadOnlySpan<char>(p + NtApi.ExtdDirHeaderSize, (int)e->FileNameLength / 2);
                    if (!(nm.SequenceEqual(".") || nm.SequenceEqual("..")))
                    {
                        if ((e->FileAttributes & NtApi.ATTR_DIRECTORY) != 0)
                        { dirs++; if ((e->FileAttributes & NtApi.ATTR_REPARSE_POINT) == 0) subdirs.Add(System.IO.Path.Combine(dir, nm.ToString())); }
                        else
                        {
                            files++;
                            if (mode == 0) bytes += e->EndOfFile;
                            else if (NtApi.OpenRelative(h, nm, NtApi.FILE_READ_ATTRIBUTES | NtApi.SYNCHRONIZE, NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var fh) >= 0)
                            {
                                NtApi.FILE_STANDARD_INFORMATION si;
                                if (NtApi.NtQueryInformationFile(fh, out _, &si, (uint)sizeof(NtApi.FILE_STANDARD_INFORMATION), NtApi.FileStandardInformation) >= 0) bytes += si.AllocationSize;
                                if (mode == 2) NtApi.NtQueryInformationFile(fh, out _, sbuf, 16 * 1024, NtApi.FileStreamInformation);
                                NtApi.NtClose(fh);
                            }
                        }
                    }
                    if (e->NextEntryOffset == 0) break;
                    p += e->NextEntryOffset;
                }
            }
        }
        finally { NativeMemory.Free(buf); NativeMemory.Free(sbuf); NtApi.NtClose(h); }
        foreach (var s in subdirs) WalkCount(s, ref files, ref dirs, ref bytes, mode);
    }

    // ------------------------------------------------------------------ mftrec: thử FSCTL_GET_NTFS_FILE_RECORD qua handle thư mục gốc (không cần admin?)
    static int MftRec(string drive, ulong record)
    {
        var h = NtApi.CreateFileW(drive.TrimEnd('\\') + "\\", NtApi.FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, NtApi.OPEN_EXISTING, NtApi.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h == NtApi.INVALID_HANDLE_VALUE) { Console.WriteLine("open lỗi"); return 1; }
        try
        {
            ulong input = record; var buf = (byte*)NativeMemory.Alloc(8192);
            bool ok = NtApi.DeviceIoControl(h, 0x90068, &input, 8, buf, 8192, out var ret, IntPtr.Zero);
            Console.WriteLine($"FSCTL_GET_NTFS_FILE_RECORD({record}) → {(ok ? "OK, " + ret + " byte, FileRecordLength=" + *(uint*)(buf + 8) + ", ref=" + *(ulong*)buf : "LỖI " + Marshal.GetLastWin32Error())}");
            if (ok) { Console.Write("  đầu bản ghi: "); for (int i = 0; i < 24; i++) Console.Write($"{buf[12 + i]:X2} "); Console.WriteLine(); }
            NativeMemory.Free(buf);
        }
        finally { NtApi.CloseHandle(h); }
        return 0;
    }

    // ------------------------------------------------------------------ ntfsdata
    static int NtfsData(string drive)
    {
        var h = NtApi.CreateFileW(drive.TrimEnd('\\') + "\\", NtApi.FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, NtApi.OPEN_EXISTING, NtApi.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        Console.WriteLine($"open root dir FILE_READ_ATTRIBUTES → {(h == NtApi.INVALID_HANDLE_VALUE ? "LỖI " + Marshal.GetLastWin32Error() : "OK")}");
        if (h == NtApi.INVALID_HANDLE_VALUE) return 1;
        try
        {
            var buf = (byte*)NativeMemory.Alloc(512);
            bool ok = NtApi.DeviceIoControl(h, 0x90064, null, 0, buf, 512, out var ret, IntPtr.Zero);
            Console.WriteLine($"FSCTL_GET_NTFS_VOLUME_DATA → {(ok ? "OK " + ret + " byte" : "LỖI " + Marshal.GetLastWin32Error())}");
            if (ok)
            {
                long total = *(long*)(buf + 16), free = *(long*)(buf + 24), reserved = *(long*)(buf + 32);
                uint bpc = *(uint*)(buf + 44), frs = *(uint*)(buf + 48);
                long mftValid = *(long*)(buf + 56), mftLcn = *(long*)(buf + 64), mft2 = *(long*)(buf + 72), zs = *(long*)(buf + 80), ze = *(long*)(buf + 88);
                Console.WriteLine($"  TotalClusters={total:N0} Free={free:N0} Reserved={reserved:N0} → Used={(total - free) * bpc:N0} B; cluster={bpc} FRS={frs}");
                Console.WriteLine($"  MftValidDataLength={mftValid:N0} B ({mftValid / 1048576.0:N1} MiB); MftZone [{zs:N0},{ze:N0}) = {(ze - zs) * bpc / 1048576.0:N1} MiB; Reserved={reserved * bpc / 1048576.0:N1} MiB");
            }
            NativeMemory.Free(buf);
        }
        finally { NtApi.CloseHandle(h); }
        return 0;
    }

    // ------------------------------------------------------------------ cloud
    static int Cloud(string root)
    {
        root = Path.GetFullPath(root);
        Console.WriteLine($"Quét cờ đám mây dưới {root} (đọc thuộc tính qua handle FILE_READ_ATTRIBUTES, không đọc dữ liệu)");
        var before = Collect(root);
        Console.WriteLine($"Trước: {before.Count:N0} mục có cờ RECALL; ví dụ: ");
        foreach (var kv in before.Take(8)) Console.WriteLine($"   {kv.Value:X8}  {kv.Key}");
        // chạy "quét" kiểu của app: mở mức thuộc tính từng file đám mây + hỏi Std + Stream
        long touched = 0;
        foreach (var kv in before)
        {
            if (NtApi.Open(NtApi.ToNtPath(NtApi.ToExtendedPath(kv.Key)), IntPtr.Zero, NtApi.FILE_READ_ATTRIBUTES | NtApi.SYNCHRONIZE,
                NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var h) >= 0)
            {
                NtApi.FILE_STANDARD_INFORMATION si; NtApi.NtQueryInformationFile(h, out _, &si, (uint)sizeof(NtApi.FILE_STANDARD_INFORMATION), NtApi.FileStandardInformation);
                var sb = (byte*)NativeMemory.Alloc(16 * 1024); NtApi.NtQueryInformationFile(h, out _, sb, 16 * 1024, NtApi.FileStreamInformation); NativeMemory.Free(sb);
                NtApi.NtClose(h); touched++;
            }
        }
        var after = Collect(root);
        int changed = before.Count(kv => !after.TryGetValue(kv.Key, out var v) || v != kv.Value);
        Console.WriteLine($"Đã mở-thuộc-tính + hỏi Std/Stream {touched:N0} mục. Sau: {after.Count:N0} mục có cờ RECALL; số mục đổi cờ = {changed} (KỲ VỌNG 0 → KT-05 đạt).");
        return 0;
    }

    static Dictionary<string, uint> Collect(string dir)
    {
        var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        void Rec(string p)
        {
            foreach (var e in List(p))
            {
                bool isDir = (e.Attr & NtApi.ATTR_DIRECTORY) != 0;
                bool cloud = (e.Attr & (NtApi.ATTR_RECALL_ON_DATA_ACCESS | NtApi.ATTR_RECALL_ON_OPEN)) != 0;
                if (cloud) d[e.Path] = e.Attr;
                // không liệt kê thư mục RECALL_ON_OPEN (Q4)
                if (isDir && (e.Attr & NtApi.ATTR_REPARSE_POINT) == 0 && (e.Attr & NtApi.ATTR_RECALL_ON_OPEN) == 0) Rec(e.Path);
            }
        }
        Rec(dir); return d;
    }
}
