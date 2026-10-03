using System.Net;
using System.Text;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Text;

namespace FolderSizePro.Storage;

/// <summary>UC-16: báo cáo HTML một file (CSS nhúng, không phụ thuộc mạng — KT-40). Luôn kèm mốc thời gian quét, cảnh báo và số byte chính xác.</summary>
public static class HtmlReport
{
    static string E(string s) => WebUtility.HtmlEncode(s);

    public static void Write(ScanResult r, Stream s, int depth, int top, UnitSystem unit = UnitSystem.Binary)
    {
        using var w = new StreamWriter(s, new UTF8Encoding(false), 65536, leaveOpen: true);
        var t = r.Tree;
        string F(long v) => ByteFormatter.Format(v, unit);
        string X(long v) => ByteFormatter.Exact(v);
        w.WriteLine("<!doctype html><html lang=\"" + (L.English ? "en" : "vi") + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        w.WriteLine("<title>Folder Size Pro — " + L.T("Báo cáo dung lượng", "Storage report") + "</title>");
        w.WriteLine("<style>:root{--bg:#fff;--fg:#1b1f24;--mut:#667085;--line:#e4e7ec;--acc:#2563eb;--warn:#b54708;--warnbg:#fffaeb;--err:#b42318;--card:#f8fafc}" +
            "@media(prefers-color-scheme:dark){:root{--bg:#0f1318;--fg:#e6e9ee;--mut:#98a2b3;--line:#273041;--acc:#60a5fa;--warn:#fdb022;--warnbg:#2a2110;--err:#f97066;--card:#161c25}}" +
            "body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 'Segoe UI',system-ui,sans-serif}main{max-width:1100px;margin:0 auto;padding:24px 16px 64px}" +
            "h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:28px 0 8px}p.mut,.mut{color:var(--mut)}table{border-collapse:collapse;width:100%}th,td{padding:5px 8px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}" +
            "th{font-weight:600;color:var(--mut);font-size:12px;text-transform:uppercase;letter-spacing:.03em}td.n,th.n{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}" +
            ".box{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px;margin:12px 0}.warn{background:var(--warnbg);border-color:var(--warn);color:var(--warn)}" +
            ".err{color:var(--err)}.bar{height:6px;background:var(--line);border-radius:3px;min-width:80px}.bar i{display:block;height:100%;background:var(--acc);border-radius:3px}" +
            "code{font-family:Consolas,monospace;font-size:12px}</style></head><body><main>");
        w.WriteLine("<h1>Folder Size Pro</h1>");
        w.WriteLine($"<p class=\"mut\">{L.T("Quét lúc", "Scanned")} {r.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} → {r.CompletedUtc.ToLocalTime():HH:mm:ss} " +
            $"({r.Duration.TotalSeconds:N1} s) · {L.T("chế độ", "mode")} {E(r.ModeUsed)} · <strong>{(r.Completeness == Completeness.Complete ? L.T("Hoàn tất", "Complete") : L.T("DỞ DANG", "PARTIAL"))}</strong> · UTC {r.CompletedUtc:O}</p>");
        if (r.Options.Approximate) w.WriteLine($"<div class=\"box warn\"><strong>{L.T("Kém chính xác", "Less accurate")}:</strong> {L.T("quét nhanh — kích thước lấy từ danh sách thư mục, bỏ ADS.", "fast scan — sizes from directory listing, ADS skipped.")}</div>");
        if (r.CompactModeUsed) w.WriteLine($"<div class=\"box warn\">{L.T("Chế độ gọn: vượt ngưỡng bộ nhớ nên chỉ giữ file lớn nhất mỗi thư mục; tổng vẫn đúng.", "Compact mode: memory limit exceeded, only the largest files per folder are kept; totals remain exact.")}</div>");
        foreach (var n in r.Notes) w.WriteLine($"<div class=\"box\">{E(n)}</div>");

        w.WriteLine($"<h2>{L.T("Tổng quan", "Overview")}</h2><table><tr><th>{L.T("Gốc quét", "Root")}</th><th class=\"n\">{L.T("Kích thước", "Size")}</th><th class=\"n\">{L.T("Trên đĩa", "On disk")}</th><th class=\"n\">{L.T("File", "Files")}</th><th class=\"n\">{L.T("Thư mục", "Folders")}</th></tr>");
        foreach (var ri in r.Roots)
        {
            ref var n = ref t[ri.Node];
            w.WriteLine($"<tr><td><code>{E(ri.Path)}</code></td><td class=\"n\" title=\"{X(n.Logical)}\">{F(n.Logical)}</td><td class=\"n\" title=\"{X(n.Allocated)}\">{F(n.Allocated)}</td><td class=\"n\">{n.FileCount:N0}</td><td class=\"n\">{n.DirCount:N0}</td></tr>");
        }
        w.WriteLine("</table>");
        if (r.HardLinkNonOwnerCount > 0) w.WriteLine($"<p class=\"mut\">{L.T("Liên kết cứng", "Hard links")}: {r.HardLinkNonOwnerCount:N0} — {L.T("không cộng thêm", "not double-counted")} {F(r.HardLinkSavedAllocated)}.</p>");

        foreach (var rc in r.Reconcile)
        {
            w.WriteLine($"<h2>{L.T("Đối chiếu ổ", "Volume reconciliation")} {E(rc.Volume.RootPath)}</h2>");
            w.WriteLine($"<table><tr><td>{L.T("Used của ổ (Tổng − Trống)", "Volume used (Total − Free)")}</td><td class=\"n\" title=\"{X(rc.Used)}\"><strong>{F(rc.Used)}</strong></td><td></td></tr>");
            foreach (var row in rc.Rows)
                w.WriteLine($"<tr><td>{E(row.Label)}</td><td class=\"n\">{(row.Bytes is long b ? F(b) : row.Status == RowStatus.Inaccessible ? L.T("không truy cập được", "inaccessible") : L.T("không xác định", "unknown"))}</td><td class=\"mut\">{E(row.Detail)}</td></tr>");
            w.WriteLine($"<tr><td>{L.T("Tổng đã đo trong cây", "Measured in tree")}</td><td class=\"n\">{F(rc.TreeAllocated)}</td><td></td></tr>");
            w.WriteLine($"<tr><td><strong>{L.T("Chưa giải thích", "Unexplained")}</strong></td><td class=\"n{(rc.Warn ? " err" : "")}\" title=\"{X(rc.Unexplained)}\"><strong>{F(rc.Unexplained)}</strong> ({ByteFormatter.Percent(rc.UnexplainedFraction)})</td><td></td></tr></table>");
            foreach (var h in rc.Hints) w.WriteLine($"<div class=\"box warn\">{E(h)}</div>");
        }

        w.WriteLine($"<h2>{L.T("Cây thư mục", "Folder tree")} ({L.T("độ sâu", "depth")} {depth})</h2><table><tr><th>{L.T("Tên", "Name")}</th><th class=\"n\">{L.T("Kích thước", "Size")}</th><th class=\"n\">{L.T("Trên đĩa", "On disk")}</th><th>%</th><th class=\"n\">{L.T("File", "Files")}</th></tr>");
        foreach (var (n, d) in ResultExporter.Walk(t, depth))
        {
            ref var nd = ref t[n];
            double pct = nd.Parent > 0 && t[nd.Parent].Allocated > 0 ? (double)nd.Allocated / t[nd.Parent].Allocated : 0;
            string badges = string.Join(" · ", ResultExporter.FlagNames(nd.Flags).Where(f => f is "AccessDenied" or "CloudOnly" or "HardLinkNonOwner" or "MountPoint" or "Symlink" or "Lost" or "TimedOut" or "CloudNotListed"));
            w.WriteLine($"<tr><td style=\"padding-left:{8 + d * 18}px\">{E(t.NameString(n))}{(nd.IsDir && d > 0 ? "\\" : "")} <span class=\"mut\">{E(badges)}</span></td><td class=\"n\" title=\"{X(nd.Logical)}\">{F(nd.Logical)}</td><td class=\"n\" title=\"{X(nd.Allocated)}\">{F(nd.Allocated)}</td>" +
                $"<td><div class=\"bar\"><i style=\"width:{pct * 100:0.#}%\"></i></div></td><td class=\"n\">{(nd.IsDir ? nd.FileCount.ToString("N0") : "")}</td></tr>");
        }
        w.WriteLine("</table>");

        w.WriteLine($"<h2>{L.T("File lớn nhất", "Largest files")}</h2><table><tr><th>{L.T("Đường dẫn", "Path")}</th><th class=\"n\">{L.T("Kích thước", "Size")}</th><th class=\"n\">{L.T("Trên đĩa", "On disk")}</th></tr>");
        foreach (var f in r.TopFiles.Take(top)) w.WriteLine($"<tr><td><code>{E(t.FullPath(f.Node))}</code></td><td class=\"n\" title=\"{X(f.Logical)}\">{F(f.Logical)}</td><td class=\"n\" title=\"{X(f.Allocated)}\">{F(f.Allocated)}</td></tr>");
        w.WriteLine("</table>");
        w.WriteLine($"<h2>{L.T("Thư mục chứa nhiều dữ liệu nhất (file trực tiếp)", "Folders holding the most data (direct files)")}</h2><table>");
        foreach (var f in r.TopDirs.Take(top)) w.WriteLine($"<tr><td><code>{E(t.FullPath(f.Node))}</code></td><td class=\"n\">{F(f.DirectFiles)}</td><td class=\"n\">{F(f.DirectAllocated)}</td></tr>");
        w.WriteLine("</table>");
        w.WriteLine($"<h2>{L.T("Theo loại file", "By file type")}</h2><table><tr><th>{L.T("Nhóm", "Group")}</th><th class=\"n\">{L.T("File", "Files")}</th><th class=\"n\">{L.T("Trên đĩa", "On disk")}</th></tr>");
        foreach (var g in r.TypeStats.GroupBy(x => x.Group).Select(g => (g.Key, c: g.Sum(x => x.Count), a: g.Sum(x => x.Allocated))).OrderByDescending(x => x.a))
            w.WriteLine($"<tr><td>{E(g.Key)}</td><td class=\"n\">{g.c:N0}</td><td class=\"n\">{F(g.a)}</td></tr>");
        w.WriteLine("</table>");

        var problems = r.Issues.Where(i => i.Kind is IssueKind.AccessDenied or IssueKind.Changed or IssueKind.VolumeLost or IssueKind.NetworkTimeout or IssueKind.Other).ToList();
        if (problems.Count > 0)
        {
            w.WriteLine($"<h2>{L.T("Vấn đề khi quét", "Scan issues")} ({problems.Count:N0})</h2><table>");
            foreach (var i in problems.Take(500)) w.WriteLine($"<tr><td>{i.Kind}</td><td><code>{E(i.Path)}</code></td><td class=\"mut\">{E(i.Message)}</td></tr>");
            w.WriteLine("</table>");
        }
        w.WriteLine($"<h2>{L.T("Kiểm tra nhất quán", "Consistency checks")}</h2><table>");
        foreach (var c in r.Consistency) w.WriteLine($"<tr><td>{(c.Ok ? "✔" : "✘")}</td><td>{E(c.Name)}</td><td class=\"mut\">{E(c.Expected)} / {E(c.Actual)}</td></tr>");
        w.WriteLine("</table><p class=\"mut\">Folder Size Pro — " + L.T("số liệu là ảnh chụp tại thời điểm quét; rê chuột lên số để xem byte chính xác.", "figures are a snapshot at scan time; hover a number for exact bytes.") + "</p></main></body></html>");
    }
}
