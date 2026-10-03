using System.Text;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Scan;
using FolderSizePro.Storage;
using FolderSizePro.Text;

namespace FolderSizePro.Cli;

/// <summary>
/// fsp — dòng lệnh Folder Size Pro. Chỉ ĐỌC (KT-30), không bật UAC (KT-31), cùng lõi đo với giao diện (UC-40).
/// Mã thoát (UC-44): 0 đầy đủ · 1 xong nhưng có mục không truy cập được · 2 đối số/đường dẫn sai · 3 huỷ/dở dang · 4 cần Administrator.
/// </summary>
internal static class Program
{
    const string Usage = """
        Folder Size Pro (fsp) — đo dung lượng chính xác từng byte trên Windows

        Dùng:
          fsp scan <đường dẫn>... [tuỳ chọn]        quét thư mục / ổ đĩa
          fsp reconcile <ổ>                          quét cả ổ và in bảng Đối chiếu ổ đĩa (Q9)
          fsp compare <a.fsp> <b.fsp> [--top N]      so sánh hai snapshot
          fsp help

        Tuỳ chọn của scan:
          --json | --csv        đầu ra máy đọc được (mặc định: bảng văn bản)
          --out <file>          ghi đầu ra vào file thay vì màn hình
          --save <file.fsp>     lưu snapshot để mở bằng giao diện / so sánh sau
          --depth N             độ sâu cây in ra (mặc định 1)
          --top N               in N file / thư mục lớn nhất (mặc định 10; 0 = tắt)
          --on-disk             sắp xếp theo Kích thước trên đĩa (mặc định theo Kích thước trên đĩa; dùng --logical để theo Kích thước)
          --logical             sắp xếp theo Kích thước (logical)
          --exclude <thư mục>   loại trừ (lặp lại được)
          --no-ads              không tính Alternate Data Stream (đánh dấu KÉM CHÍNH XÁC)
          --fast                Quét nhanh: tin danh sách thư mục, bỏ ADS (KÉM CHÍNH XÁC)
          --mft                 bắt buộc quét MFT (cần Administrator, chỉ NTFS)
          --no-mft              bắt buộc quét thường
          --si                  đơn vị SI (kB = 1000) thay vì nhị phân (KiB = 1024)
          --exact               in số byte chính xác
          --threads N           số luồng I/O
          --partial             khi Ctrl+C vẫn ghi kết quả dở dang
          --no-progress         không in tiến độ ra stderr

        Mã thoát: 0 đầy đủ · 1 có mục không truy cập được · 2 đối số/đường dẫn sai · 3 huỷ/dở dang · 4 cần Administrator
        """;

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "/?") { Console.WriteLine(Usage); return args.Length == 0 ? 2 : 0; }
            return args[0].ToLowerInvariant() switch
            {
                "scan" => Scan(args[1..], reconcileMode: false),
                "reconcile" => Scan(args[1..], reconcileMode: true),
                "compare" => Compare(args[1..]),
                _ => Fail($"Lệnh không hợp lệ: '{args[0]}'."),
            };
        }
        catch (ArgException ex) { return Fail(ex.Message); }
    }

    sealed class ArgException(string m) : Exception(m);

    static int Fail(string msg)
    {
        Console.Error.WriteLine("Lỗi: " + msg);
        Console.Error.WriteLine("Gõ 'fsp help' để xem cách dùng.");
        return 2;
    }

    sealed class Opts
    {
        public List<string> Paths = new();
        public bool Json, Csv, Si, Exact, Partial, NoProgress, SortLogical, Fast, NoAds, Mft, NoMft;
        public string? Out, Save; public int Depth = 1, Top = 10, Threads; public List<string> Exclude = new();
    }

    static Opts Parse(string[] a)
    {
        var o = new Opts();
        for (int i = 0; i < a.Length; i++)
        {
            string s = a[i];
            string Next() => i + 1 < a.Length ? a[++i] : throw new ArgException($"Tuỳ chọn {s} cần một giá trị.");
            int Num() => int.TryParse(Next(), out var v) && v >= 0 ? v : throw new ArgException($"Tuỳ chọn {s} cần một số nguyên không âm.");
            switch (s.ToLowerInvariant())
            {
                case "--json": o.Json = true; break;
                case "--csv": o.Csv = true; break;
                case "--si": o.Si = true; break;
                case "--exact": o.Exact = true; break;
                case "--partial": o.Partial = true; break;
                case "--no-progress": o.NoProgress = true; break;
                case "--on-disk": o.SortLogical = false; break;
                case "--logical": o.SortLogical = true; break;
                case "--fast": o.Fast = true; break;
                case "--no-ads": o.NoAds = true; break;
                case "--mft": o.Mft = true; break;
                case "--no-mft": o.NoMft = true; break;
                case "--out": o.Out = Next(); break;
                case "--save": o.Save = Next(); break;
                case "--depth": o.Depth = Num(); break;
                case "--top": o.Top = Num(); break;
                case "--threads": o.Threads = Num(); break;
                case "--exclude": o.Exclude.Add(Next()); break;
                default:
                    if (s.StartsWith("--", StringComparison.Ordinal)) throw new ArgException($"Tuỳ chọn không hợp lệ: {s}");
                    o.Paths.Add(s); break;
            }
        }
        if (o.Json && o.Csv) throw new ArgException("Chỉ chọn một trong --json / --csv.");
        return o;
    }

    static int Scan(string[] a, bool reconcileMode)
    {
        var o = Parse(a);
        if (o.Paths.Count == 0) throw new ArgException(reconcileMode ? "Thiếu ổ đĩa cần đối chiếu (vd: fsp reconcile D:)." : "Thiếu đường dẫn cần quét.");
        if (o.Mft && o.NoMft) throw new ArgException("Không dùng đồng thời --mft và --no-mft.");

        var roots = new List<string>();
        foreach (var p in o.Paths)
        {
            var chk = PathValidator.Validate(p);
            if (!chk.Ok) { Console.Error.WriteLine($"Lỗi: {chk.Error}"); Console.Error.WriteLine("Gõ 'fsp help' để xem cách dùng."); return 2; }  // ER-30: stdout trống
            roots.Add(chk.Path);
        }
        if (reconcileMode)
            foreach (var r in roots)
                if (!r.TrimEnd('\\').Equals(Native.VolumeInfo.For(r).RootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new ArgException($"'{r}' không phải gốc ổ đĩa; 'reconcile' cần cả ổ (vd: D:).");

        var opt = new ScanOptions
        {
            FastMode = o.Fast, IncludeAds = !o.NoAds && !o.Fast, MaxThreads = o.Threads, ExcludedPaths = o.Exclude,
            Mode = o.Mft ? ScanMode.Mft : o.NoMft ? ScanMode.Normal : ScanMode.Auto, TopN = Math.Max(o.Top, 10),
        };

        // KT-31: không bật UAC; thiếu quyền → mã 4
        var plan = ScanPlanner.Plan(roots, opt);
        if (plan.ExitCode4Reason is { } why) { Console.Error.WriteLine("Lỗi: " + why); return 4; }

        using var cts = new CancellationTokenSource();
        var job = ScanPlanner.Start(roots, opt, plan);
        bool cancelled = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled = true; job.Cancel(); };
        bool showProgress = !o.NoProgress && !Console.IsErrorRedirected;
        while (!job.Completion.Wait(500))
        {
            if (!showProgress) continue;
            var p = job.Snapshot();
            string cur = p.CurrentPath.Length > 60 ? "…" + p.CurrentPath[^59..] : p.CurrentPath;
            Console.Error.Write($"\r  {p.Files:N0} file · {p.Dirs:N0} thư mục · {ByteFormatter.Format(p.Allocated, o.Si ? UnitSystem.Si : UnitSystem.Binary)} · {p.FilesPerSec:N0} file/s · {cur}".PadRight(Console.WindowWidth > 10 ? Console.WindowWidth - 1 : 100));
        }
        if (showProgress) Console.Error.Write("\r" + new string(' ', Math.Max(Console.WindowWidth - 1, 80)) + "\r");
        var res = job.Result;

        if (cancelled && !o.Partial)
        {
            Console.Error.WriteLine("Đã huỷ. Dùng --partial để vẫn ghi kết quả dở dang.");
            return 3;
        }

        if (o.Save != null)
        {
            try { FspFile.Save(res, o.Save); Console.Error.WriteLine($"Đã lưu snapshot: {o.Save}"); }
            catch (Exception ex) { Console.Error.WriteLine($"Lỗi: không ghi được snapshot: {ex.Message}"); return 2; }   // ER-32
        }

        try { Output(res, o, reconcileMode); }
        catch (IOException ex) { Console.Error.WriteLine($"Lỗi: không ghi được đầu ra: {ex.Message}"); return 2; }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"Lỗi: không ghi được đầu ra: {ex.Message}"); return 2; }

        if (res.Completeness == Completeness.Partial || cancelled) return 3;
        return res.Issues.Any(i => i.Kind is IssueKind.AccessDenied or IssueKind.Changed or IssueKind.VolumeLost or IssueKind.NetworkTimeout or IssueKind.Other) ? 1 : 0;
    }

    static void Output(ScanResult r, Opts o, bool reconcileMode)
    {
        if (o.Json || o.Csv)
        {
            if (o.Out != null)
            {
                string tmp = o.Out + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                { if (o.Json) ResultExporter.WriteJson(r, fs, o.Depth, o.Top); else ResultExporter.WriteCsv(r, fs, o.Depth); fs.Flush(true); }
                File.Move(tmp, o.Out, true);
            }
            else
            {
                using var so = Console.OpenStandardOutput();
                if (o.Json) ResultExporter.WriteJson(r, so, o.Depth, o.Top); else ResultExporter.WriteCsv(r, so, o.Depth);
            }
            return;
        }
        string text = RenderText(r, o, reconcileMode);
        if (o.Out != null) { string tmp = o.Out + ".tmp"; File.WriteAllText(tmp, text, new UTF8Encoding(true)); File.Move(tmp, o.Out, true); }
        else Console.Write(text);
    }

    static string RenderText(ScanResult r, Opts o, bool reconcileMode)
    {
        var u = o.Si ? UnitSystem.Si : UnitSystem.Binary;
        string F(long v) => o.Exact ? ByteFormatter.Exact(v) : ByteFormatter.Format(v, u);
        var sb = new StringBuilder(); var t = r.Tree;
        sb.AppendLine("Folder Size Pro — kết quả quét");
        sb.AppendLine($"  Thời điểm quét : {r.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} → {r.CompletedUtc.ToLocalTime():HH:mm:ss}  (UTC {r.CompletedUtc:O})");
        sb.AppendLine($"  Thời lượng     : {r.Duration.TotalSeconds:N1} s    Chế độ: {r.ModeUsed}{(r.Options.Approximate ? " — KÉM CHÍNH XÁC (quét nhanh / bỏ ADS)" : "")}{(r.CompactModeUsed ? " — Chế độ gọn (xem ghi chú)" : "")}");
        sb.AppendLine($"  Trạng thái     : {(r.Completeness == Completeness.Complete ? "HOÀN TẤT" : "DỞ DANG — chưa hoàn tất")}");
        foreach (var n in r.Notes) sb.AppendLine("  Ghi chú        : " + n);
        sb.AppendLine();
        sb.AppendLine($"  {"KÍCH THƯỚC",14}  {"TRÊN ĐĨA",14}  {"%CHA",6}  {"FILE",11}  {"THƯ MỤC",9}  TÊN");
        foreach (var (n, d) in ResultExporter.Walk(t, o.Depth, !o.SortLogical))
        {
            ref var nd = ref t[n];
            long parentTotal = nd.Parent > 0 ? t[nd.Parent].Allocated : 0;
            string pct = parentTotal > 0 ? ByteFormatter.Percent((double)nd.Allocated / parentTotal) : "";
            string name = d == 0 ? t.NameString(n) : t.NameString(n);
            string badge = Badges(nd);
            sb.AppendLine($"  {F(nd.Logical),14}  {F(nd.Allocated),14}  {pct,6}  {(nd.IsDir ? nd.FileCount.ToString("N0") : ""),11}  {(nd.IsDir ? nd.DirCount.ToString("N0") : ""),9}  {new string(' ', d * 2)}{name}{(nd.IsDir && d == 0 ? "" : nd.IsDir ? "\\" : "")}{badge}");
        }
        if (o.Top > 0)
        {
            sb.AppendLine().AppendLine($"  Top {Math.Min(o.Top, r.TopFiles.Count)} file lớn nhất (theo Trên đĩa):");
            foreach (var f in r.TopFiles.Take(o.Top)) sb.AppendLine($"    {F(f.Logical),14}  {F(f.Allocated),14}  {t.FullPath(f.Node)}");
            sb.AppendLine().AppendLine($"  Top {Math.Min(o.Top, r.TopDirs.Count)} thư mục chứa nhiều dữ liệu nhất (chỉ file trực tiếp):");
            foreach (var f in r.TopDirs.Take(o.Top)) sb.AppendLine($"    {F(f.DirectFiles),14}  {F(f.DirectAllocated),14}  {t.FullPath(f.Node)}");
            sb.AppendLine().AppendLine("  Theo loại file (nhóm):");
            foreach (var g in r.TypeStats.GroupBy(x => x.Group).Select(g => (g.Key, c: g.Sum(x => x.Count), l: g.Sum(x => x.Logical), a: g.Sum(x => x.Allocated))).OrderByDescending(x => x.a).Take(8))
                sb.AppendLine($"    {g.Key,-10} {g.c,12:N0} file  {F(g.l),14}  {F(g.a),14}");
        }
        if (r.HardLinkNonOwnerCount > 0)
            sb.AppendLine().AppendLine($"  Liên kết cứng: {r.HardLinkNonOwnerCount:N0} liên kết không phải chủ — không cộng thêm {F(r.HardLinkSavedAllocated)} trên đĩa (Q2).");
        var problems = r.Issues.Where(i => i.Kind is IssueKind.AccessDenied or IssueKind.Changed or IssueKind.VolumeLost or IssueKind.NetworkTimeout or IssueKind.Other).ToList();
        if (problems.Count > 0)
        {
            sb.AppendLine().AppendLine($"  {problems.Count:N0} mục có vấn đề (không truy cập được / thay đổi khi quét / lỗi):");
            foreach (var i in problems.Take(15)) sb.AppendLine($"    [{i.Kind}] {i.Path} — {i.Message}");
            if (problems.Count > 15) sb.AppendLine($"    … và {problems.Count - 15:N0} mục nữa (dùng --json để xem đủ).");
        }
        var info = r.Issues.Count - problems.Count;
        if (info > 0) sb.AppendLine($"  {info:N0} mục thông tin (đã loại trừ / thư mục đám mây chưa nạp / ước lượng từ danh sách) — dùng --json để xem.");
        foreach (var rc in r.Reconcile) sb.Append(RenderReconcile(rc, F));
        if (reconcileMode || r.Consistency.Count > 0)
        {
            sb.AppendLine().AppendLine("  Kiểm tra nhất quán (UC-52):");
            foreach (var c in r.Consistency) sb.AppendLine($"    {(c.Ok ? "ĐẠT " : "LỆCH")}  {c.Name}: kỳ vọng {c.Expected} · thực tế {c.Actual}");
        }
        return sb.ToString();
    }

    static string RenderReconcile(ReconcileResult rc, Func<long, string> F)
    {
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine($"  ĐỐI CHIẾU Ổ {rc.Volume.RootPath} ({rc.Volume.FileSystem}, cluster {rc.Volume.ClusterSize:N0} B) — đo lúc {rc.MeasuredUtc.ToLocalTime():HH:mm:ss}");
        sb.AppendLine($"    Used của ổ (Tổng − Trống)        : {F(rc.Used),16}   ({rc.Used:N0} B)");
        foreach (var row in rc.Rows)
        {
            string val = row.Bytes is long b ? F(b) : row.Status == RowStatus.Inaccessible ? "không truy cập được" : "không xác định";
            sb.AppendLine($"      • {row.Label,-60} {val,18}   {row.Detail}");
        }
        sb.AppendLine($"    Tổng đã đo trong cây              : {F(rc.TreeAllocated),16}");
        sb.AppendLine($"    CHƯA GIẢI THÍCH = Used − cây      : {F(rc.Unexplained),16}   ({ByteFormatter.Percent(rc.UnexplainedFraction)} của Used){(rc.Warn ? "   ⚠ vượt 1%" : "")}");
        foreach (var h in rc.Hints) sb.AppendLine("    → " + h);
        return sb.ToString();
    }

    static string Badges(in Node n)
    {
        var l = new List<string>();
        if (n.Has(NodeFlags.AccessDenied)) l.Add("KHÔNG TRUY CẬP ĐƯỢC");
        if (n.Has(NodeFlags.MountPoint)) l.Add("junction/mount");
        else if (n.Has(NodeFlags.Symlink)) l.Add("symlink");
        else if (n.Has(NodeFlags.Reparse) && n.IsDir && !n.Has(NodeFlags.Cloud)) l.Add("reparse");
        if (n.Has(NodeFlags.CloudNotListed)) l.Add("chưa tải danh sách (đám mây)");
        if (n.Has(NodeFlags.CloudOnly)) l.Add("chỉ trên đám mây");
        if (n.Has(NodeFlags.HardLinkNonOwner)) l.Add("liên kết cứng — tính ở bản chủ");
        if (n.Has(NodeFlags.Lost)) l.Add("MẤT KẾT NỐI");
        if (n.Has(NodeFlags.TimedOut)) l.Add("HẾT THỜI GIAN");
        if (n.Has(NodeFlags.Excluded)) l.Add("đã loại trừ");
        if (n.Has(NodeFlags.Aggregate)) l.Add("gộp (chế độ gọn)");
        return l.Count == 0 ? "" : "  [" + string.Join("; ", l) + "]";
    }

    static int Compare(string[] a)
    {
        var o = Parse(a);
        if (o.Paths.Count != 2) throw new ArgException("fsp compare cần đúng hai file .fsp.");
        ScanResult x, y;
        try { x = FspFile.Load(o.Paths[0]); y = FspFile.Load(o.Paths[1]); }
        catch (FspFormatException ex) { Console.Error.WriteLine("Lỗi: " + ex.Message); return 2; }
        catch (IOException ex) { Console.Error.WriteLine("Lỗi: " + ex.Message); return 2; }
        var diff = SnapshotComparer.Compare(x, y);
        var u = o.Si ? UnitSystem.Si : UnitSystem.Binary;
        Console.WriteLine($"So sánh snapshot:\n  A: {o.Paths[0]}  ({x.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm})\n  B: {o.Paths[1]}  ({y.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm})");
        Console.WriteLine($"  Tổng trên đĩa: A {ByteFormatter.Format(x.RootAllocated(), u)} → B {ByteFormatter.Format(y.RootAllocated(), u)}  (Δ {ByteFormatter.Format(diff.TotalDeltaAllocated, u)})");
        Console.WriteLine($"  {"Δ TRÊN ĐĨA",14}  {"A",12}  {"B",12}  TRẠNG THÁI  ĐƯỜNG DẪN");
        foreach (var e in diff.Entries.Take(o.Top == 10 && !a.Contains("--top") ? 30 : o.Top))
            Console.WriteLine($"  {ByteFormatter.Format(e.DeltaAllocated, u),14}  {ByteFormatter.Format(e.AllocatedA, u),12}  {ByteFormatter.Format(e.AllocatedB, u),12}  {e.Status,-10}  {e.Path}");
        return 0;
    }
}
