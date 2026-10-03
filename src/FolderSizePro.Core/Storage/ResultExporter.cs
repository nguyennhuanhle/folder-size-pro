using System.Globalization;
using System.Text;
using System.Text.Json;
using FolderSizePro.Model;

namespace FolderSizePro.Storage;

/// <summary>UC-16 / UC-40: xuất kết quả. Luôn kèm số byte chính xác, mốc thời gian quét và cảnh báo (Q7, Q8).</summary>
public static class ResultExporter
{
    public static IEnumerable<string> FlagNames(NodeFlags f)
    {
        foreach (NodeFlags v in Enum.GetValues<NodeFlags>())
            if (v != NodeFlags.None && v != NodeFlags.Dir && (f & v) == v) yield return v.ToString();
    }

    /// <summary>Duyệt cây tới độ sâu tối đa (độ sâu 0 = các gốc quét), con sắp theo Trên đĩa giảm dần.</summary>
    public static IEnumerable<(int Node, int Depth)> Walk(ScanTree t, int maxDepth, bool sortOnDisk = true)
    {
        var stack = new Stack<(int, int)>();
        ref var vr = ref t[0];
        var roots = Enumerable.Range(vr.FirstChild, vr.ChildCount).ToList();
        for (int i = roots.Count - 1; i >= 0; i--) stack.Push((roots[i], 0));
        while (stack.Count > 0)
        {
            var (n, d) = stack.Pop();
            yield return (n, d);
            ref var nd = ref t[n];
            if (!nd.IsDir || d >= maxDepth || nd.ChildCount == 0) continue;
            var kids = Enumerable.Range(nd.FirstChild, nd.ChildCount).ToList();
            kids.Sort((a, b) =>
            {
                int c = sortOnDisk ? t[b].Allocated.CompareTo(t[a].Allocated) : t[b].Logical.CompareTo(t[a].Logical);
                return c != 0 ? c : string.Compare(t.NameString(a), t.NameString(b), StringComparison.OrdinalIgnoreCase);
            });
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], d + 1));
        }
    }

    static string Time(long filetime) => filetime > 0 ? DateTime.FromFileTimeUtc(filetime).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "";

    public static void WriteJson(ScanResult r, Stream s, int depth, int top)
    {
        using var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var t = r.Tree;
        w.WriteStartObject();
        w.WriteString("tool", "Folder Size Pro");
        w.WriteString("startedUtc", r.StartedUtc.ToString("O"));
        w.WriteString("completedUtc", r.CompletedUtc.ToString("O"));
        w.WriteString("completeness", r.Completeness.ToString());
        w.WriteString("mode", r.ModeUsed);
        w.WriteBoolean("approximate", r.Options.Approximate);
        w.WriteBoolean("compactMode", r.CompactModeUsed);
        w.WriteBoolean("includeAds", r.Options.IncludeAds);
        w.WriteStartArray("notes"); foreach (var n in r.Notes) w.WriteStringValue(n); w.WriteEndArray();

        w.WriteStartArray("roots");
        foreach (var ri in r.Roots)
        {
            w.WriteStartObject();
            w.WriteString("path", ri.Path); w.WriteString("volume", ri.Volume.RootPath); w.WriteString("fileSystem", ri.Volume.FileSystem);
            w.WriteNumber("clusterSize", ri.Volume.ClusterSize); w.WriteNumber("volumeTotal", ri.Volume.TotalBytes); w.WriteNumber("volumeFree", ri.Volume.FreeBytes);
            w.WriteNumber("logical", t[ri.Node].Logical); w.WriteNumber("allocated", t[ri.Node].Allocated);
            w.WriteNumber("residentInMft", t[ri.Node].Resident);
            w.WriteNumber("files", t[ri.Node].FileCount); w.WriteNumber("dirs", t[ri.Node].DirCount);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("nodes");
        foreach (var (n, d) in Walk(t, depth))
        {
            ref var nd = ref t[n];
            w.WriteStartObject();
            w.WriteString("path", t.FullPath(n)); w.WriteNumber("depth", d); w.WriteBoolean("isDir", nd.IsDir);
            w.WriteNumber("logical", nd.Logical); w.WriteNumber("allocated", nd.Allocated); w.WriteNumber("residentInMft", nd.Resident);
            if (nd.IsDir) { w.WriteNumber("files", nd.FileCount); w.WriteNumber("dirs", nd.DirCount); }
            if (nd.LinkCount > 1) w.WriteNumber("hardLinks", nd.LinkCount);
            w.WriteString("modifiedUtc", Time(nd.MTime));
            w.WriteStartArray("flags"); foreach (var f in FlagNames(nd.Flags)) w.WriteStringValue(f); w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("topFiles");
        foreach (var f in r.TopFiles.Take(top)) { w.WriteStartObject(); w.WriteString("path", t.FullPath(f.Node)); w.WriteNumber("logical", f.Logical); w.WriteNumber("allocated", f.Allocated); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WriteStartArray("topDirs");
        foreach (var f in r.TopDirs.Take(top)) { w.WriteStartObject(); w.WriteString("path", t.FullPath(f.Node)); w.WriteNumber("directFilesLogical", f.DirectFiles); w.WriteNumber("directFilesAllocated", f.DirectAllocated); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WriteStartArray("types");
        foreach (var f in r.TypeStats.Take(Math.Max(top, 50))) { w.WriteStartObject(); w.WriteString("extension", f.Extension); w.WriteString("group", f.Group); w.WriteNumber("count", f.Count); w.WriteNumber("logical", f.Logical); w.WriteNumber("allocated", f.Allocated); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WriteStartArray("issues");
        foreach (var i in r.Issues) { w.WriteStartObject(); w.WriteString("kind", i.Kind.ToString()); w.WriteString("path", i.Path); w.WriteString("message", i.Message); w.WriteNumber("ntstatus", i.NtStatus); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WriteStartArray("reconcile");
        foreach (var rc in r.Reconcile)
        {
            w.WriteStartObject();
            w.WriteString("volume", rc.Volume.RootPath); w.WriteNumber("used", rc.Used); w.WriteNumber("treeAllocated", rc.TreeAllocated); w.WriteNumber("unexplained", rc.Unexplained);
            w.WriteNumber("unexplainedFraction", rc.UnexplainedFraction); w.WriteBoolean("warn", rc.Warn);
            w.WriteStartArray("rows");
            foreach (var row in rc.Rows) { w.WriteStartObject(); w.WriteString("key", row.Key); w.WriteString("label", row.Label); if (row.Bytes is long b) w.WriteNumber("bytes", b); else w.WriteNull("bytes"); w.WriteString("status", row.Status.ToString()); w.WriteString("detail", row.Detail); w.WriteEndObject(); }
            w.WriteEndArray();
            w.WriteStartArray("hints"); foreach (var h in rc.Hints) w.WriteStringValue(h); w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("consistency");
        foreach (var c in r.Consistency) { w.WriteStartObject(); w.WriteString("check", c.Name); w.WriteString("expected", c.Expected); w.WriteString("actual", c.Actual); w.WriteBoolean("ok", c.Ok); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static void WriteCsv(ScanResult r, Stream s, int depth)
    {
        using var w = new StreamWriter(s, new UTF8Encoding(true), 65536, leaveOpen: true);   // BOM để Excel đọc đúng tiếng Việt
        var t = r.Tree;
        w.WriteLine($"# Folder Size Pro; completedUtc={r.CompletedUtc:O}; completeness={r.Completeness}; mode={r.ModeUsed}; approximate={r.Options.Approximate}");
        w.WriteLine("path,type,logical_bytes,allocated_bytes,resident_in_mft_bytes,files,dirs,hard_links,modified_utc,flags");
        foreach (var (n, _) in Walk(t, depth))
        {
            ref var nd = ref t[n];
            w.WriteLine(string.Join(',', Csv(t.FullPath(n)), nd.IsDir ? "dir" : "file", nd.Logical, nd.Allocated, nd.Resident,
                nd.IsDir ? nd.FileCount : 1, nd.IsDir ? nd.DirCount : 0, nd.LinkCount, Time(nd.MTime), Csv(string.Join('|', FlagNames(nd.Flags)))));
        }
    }

    static string Csv(string v) => v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
