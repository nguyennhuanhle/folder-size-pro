using FolderSizePro.Model;

namespace FolderSizePro.Analysis;

public enum DiffStatus { Added, Removed, Grew, Shrank, Same }

public sealed record DiffEntry(string Path, bool IsDir, DiffStatus Status, long AllocatedA, long AllocatedB, long LogicalA, long LogicalB)
{
    public long DeltaAllocated => AllocatedB - AllocatedA;
    public long DeltaLogical => LogicalB - LogicalA;
}

public sealed class DiffResult
{
    public List<DiffEntry> Entries { get; } = new();
    public long TotalDeltaAllocated { get; set; }
    public long TotalDeltaLogical { get; set; }
}

/// <summary>UC-15: so sánh hai ảnh chụp. Bỏ qua nguyên nhánh không đổi (so tổng) nên nhanh kể cả cây hàng triệu nút.</summary>
public static class SnapshotComparer
{
    public static DiffResult Compare(ScanResult a, ScanResult b)
    {
        var d = new DiffResult();
        var ta = a.Tree; var tb = b.Tree;
        var rootsB = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var rb in b.Roots) rootsB[rb.Path.TrimEnd('\\')] = rb.Node;
        var matchedB = new HashSet<int>();
        foreach (var ra in a.Roots)
        {
            if (rootsB.TryGetValue(ra.Path.TrimEnd('\\'), out int nb)) { matchedB.Add(nb); Node(ta, ra.Node, tb, nb, ta.FullPath(ra.Node), d); }
            else Add(d, ta.FullPath(ra.Node), true, DiffStatus.Removed, ta[ra.Node].Allocated, 0, ta[ra.Node].Logical, 0);
        }
        foreach (var rb in b.Roots.Where(x => !matchedB.Contains(x.Node)))
            Add(d, tb.FullPath(rb.Node), true, DiffStatus.Added, 0, tb[rb.Node].Allocated, 0, tb[rb.Node].Logical);
        d.TotalDeltaAllocated = b.RootAllocated() - a.RootAllocated();
        d.TotalDeltaLogical = b.RootLogical() - a.RootLogical();
        d.Entries.Sort((x, y) => Math.Abs(y.DeltaAllocated).CompareTo(Math.Abs(x.DeltaAllocated)));
        return d;
    }

    static void Add(DiffResult d, string path, bool isDir, DiffStatus st, long aa, long ab, long la, long lb)
        => d.Entries.Add(new DiffEntry(path, isDir, st, aa, ab, la, lb));

    static void Node(ScanTree ta, int na, ScanTree tb, int nb, string path, DiffResult d)
    {
        ref var a = ref ta[na]; ref var b = ref tb[nb];
        if (a.Allocated == b.Allocated && a.Logical == b.Logical && a.FileCount == b.FileCount && a.DirCount == b.DirCount) return;   // nhánh không đổi
        bool isDir = a.IsDir || b.IsDir;
        d.Entries.Add(new DiffEntry(path, isDir, b.Allocated > a.Allocated ? DiffStatus.Grew : b.Allocated < a.Allocated ? DiffStatus.Shrank : DiffStatus.Same,
            a.Allocated, b.Allocated, a.Logical, b.Logical));
        if (!a.IsDir || !b.IsDir) return;
        var kidsB = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int k = 0; k < b.ChildCount; k++) kidsB[tb.NameString(b.FirstChild + k)] = b.FirstChild + k;
        for (int k = 0; k < a.ChildCount; k++)
        {
            int ca = a.FirstChild + k; string nm = ta.NameString(ca);
            string cp = path.EndsWith('\\') ? path + nm : path + "\\" + nm;
            if (kidsB.Remove(nm, out int cb)) Node(ta, ca, tb, cb, cp, d);
            else Add(d, cp, ta[ca].IsDir, DiffStatus.Removed, ta[ca].Allocated, 0, ta[ca].Logical, 0);
        }
        foreach (var (nm, cb) in kidsB)
        {
            string cp = path.EndsWith('\\') ? path + nm : path + "\\" + nm;
            Add(d, cp, tb[cb].IsDir, DiffStatus.Added, 0, tb[cb].Allocated, 0, tb[cb].Logical);
        }
    }
}
