using System.Text.RegularExpressions;
using FolderSizePro.Model;

namespace FolderSizePro.Analysis;

/// <summary>UC-09: lọc theo tên (ký tự đại diện hoặc regex), đuôi, khoảng kích thước, ngày sửa, thuộc tính.</summary>
public sealed class NodeFilter
{
    public string NameText { get; set; } = "";
    public bool UseRegex { get; set; }
    public string Extensions { get; set; } = "";               // "mp4, mkv"
    public long? MinBytes { get; set; }
    public long? MaxBytes { get; set; }
    public bool UseAllocated { get; set; } = true;             // true: so Trên đĩa; false: so Kích thước
    public DateTime? ModifiedAfter { get; set; }
    public DateTime? ModifiedBefore { get; set; }
    public NodeFlags RequireAny { get; set; }                  // khớp MỘT trong các cờ (Hidden, System, CloudOnly…)
    public bool FilesOnly { get; set; }
    public bool DirsOnly { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(NameText) && string.IsNullOrWhiteSpace(Extensions) && MinBytes == null && MaxBytes == null
        && ModifiedAfter == null && ModifiedBefore == null && RequireAny == NodeFlags.None && !FilesOnly && !DirsOnly;

    /// <summary>Biên dịch tên: trả (regex, lỗi). Lỗi → báo ngay ở ô lọc, không chạy tìm (ER-01 áp dụng cho ô lọc).</summary>
    public (Regex? Regex, string? Error) CompileName()
    {
        if (string.IsNullOrWhiteSpace(NameText)) return (null, null);
        try
        {
            string pat = UseRegex ? NameText : "^" + Regex.Escape(NameText.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            if (!UseRegex && !NameText.Contains('*') && !NameText.Contains('?')) pat = Regex.Escape(NameText.Trim());   // không có ký tự đại diện → "chứa"
            return (new Regex(pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)), null);
        }
        catch (ArgumentException ex) { return (null, "Biểu thức không hợp lệ: " + ex.Message); }
    }

    /// <summary>Tìm trên mọi nút còn sống. Trả tối đa <paramref name="limit"/> nút lớn nhất (theo Trên đĩa) và tổng số khớp.</summary>
    public static List<int> Search(ScanResult r, NodeFilter f, int limit, CancellationToken ct, out int totalMatches, out string? error)
    {
        error = null; totalMatches = 0;
        var (rx, err) = f.CompileName();
        if (err != null) { error = err; return new(); }
        var exts = f.Extensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.TrimStart('.').ToLowerInvariant()).ToHashSet();
        var t = r.Tree; int n = t.Count;
        var heap = new PriorityQueue<int, long>(); int total = 0;
        long afterFt = f.ModifiedAfter?.ToFileTimeUtc() ?? 0, beforeFt = f.ModifiedBefore?.ToFileTimeUtc() ?? long.MaxValue;
        for (int i = 1; i < n; i++)
        {
            if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            ref var nd = ref t[i];
            if (nd.Parent < 0) continue;
            if (f.FilesOnly && nd.IsDir) continue;
            if (f.DirsOnly && !nd.IsDir) continue;
            long size = f.UseAllocated ? nd.Allocated : nd.Logical;
            if (f.MinBytes is long mn && size < mn) continue;
            if (f.MaxBytes is long mx && size > mx) continue;
            if (f.RequireAny != NodeFlags.None && (nd.FlagsRaw & (uint)f.RequireAny) == 0) continue;
            if (afterFt > 0 && nd.MTime < afterFt) continue;
            if (nd.MTime > beforeFt) continue;
            if (exts.Count > 0)
            {
                if (nd.IsDir) continue;
                var nm = t.Name(i); int dot = nm.LastIndexOf('.');
                if (dot < 0 || !exts.Contains(nm[(dot + 1)..].ToString().ToLowerInvariant())) continue;
            }
            if (rx != null)
            {
                try { if (!rx.IsMatch(t.Name(i))) continue; } catch (RegexMatchTimeoutException) { continue; }
            }
            total++;
            if (heap.Count >= limit) { if (heap.TryPeek(out _, out var min) && size <= min) continue; heap.Dequeue(); }
            heap.Enqueue(i, size);
        }
        totalMatches = total;
        return heap.UnorderedItems.Select(x => (x.Element, x.Priority)).OrderByDescending(x => x.Priority).Select(x => x.Element).ToList();
    }
}
