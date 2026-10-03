using FolderSizePro.Model;
using FolderSizePro.Scan;

namespace FolderSizePro.Analysis;

/// <summary>
/// Giai đoạn "chốt số" sau khi quét: (a) chọn chủ hard link (Q2) → (b) gộp từ lá lên gốc → (c) Top-N, thống kê loại
/// → (d) kiểm tra nhất quán (UC-52) → (e) bảng Đối chiếu ổ đĩa (Q9). Dùng chung cho mọi chế độ quét.
/// </summary>
internal static class Finalizer
{
    /// <summary>Quét đầy đủ: thống kê loại và top lấy từ các luồng quét (nhanh hơn duyệt lại cây).</summary>
    public static void Run(ScanResult r, List<HardLinkCand> cands,
        List<(string Ext, long Count, long Logical, long Allocated)> types,
        List<(int Node, long Logical, long Allocated)> topCands, long progLogical, long progAlloc)
    {
        var tree = r.Tree;
        r.HardLinkCands = cands;
        var nonOwners = HardLinkResolver.Resolve(tree, r.HardLinkCands, out int groups, out int errors);
        Rollup.Run(tree);

        var tf = new List<TopFile>();
        foreach (var (node, _, _) in topCands)
        {
            ref var n = ref tree[node];
            if (n.Parent < 0 || n.Has(NodeFlags.HardLinkNonOwner)) continue;
            tf.Add(new TopFile(node, n.OwnLogical, n.OwnAllocated));
        }
        r.TopFiles = tf.OrderByDescending(x => x.Allocated).ThenByDescending(x => x.Logical).Take(Math.Max(r.Options.TopN, 1)).ToList();
        r.TopDirs = Rollup.TopDirectories(tree, Math.Max(r.Options.TopN, 1));

        var acc = new Dictionary<string, (long c, long l, long a)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ext, c, l, a) in types) acc[ext.ToLowerInvariant()] = (c, l, a);
        foreach (var no in nonOwners)
        {
            var ext = ExtOf(tree, no.Node).ToLowerInvariant();
            if (acc.TryGetValue(ext, out var v)) acc[ext] = (v.c, v.l - no.Logical, v.a - no.Allocated);
        }
        r.TypeStats = acc.Select(kv => new TypeStat(kv.Key, TypeCatalog.GroupOf(kv.Key), kv.Value.c, kv.Value.l, kv.Value.a))
            .Where(t => t.Count > 0).OrderByDescending(t => t.Allocated).ToList();

        Conclude(r, nonOwners, groups, errors, progLogical, progAlloc, withProgressCheck: true);
    }

    /// <summary>Sau khi quét lại một nhánh: chủ hard link tính lại TOÀN CỤC, top/thống kê loại tính lại bằng một lượt duyệt cây.</summary>
    public static void RunRescan(ScanResult r, List<HardLinkCand> newCands)
    {
        var tree = r.Tree;
        r.HardLinkCands.AddRange(newCands);
        var nonOwners = HardLinkResolver.Resolve(tree, r.HardLinkCands, out int groups, out int errors);
        Rollup.Run(tree);
        r.TopFiles = Rollup.TopFilesAll(tree, Math.Max(r.Options.TopN, 1));
        r.TopDirs = Rollup.TopDirectories(tree, Math.Max(r.Options.TopN, 1));
        r.TypeStats = Rollup.TypeStatsAll(tree);
        r.Consistency.Clear(); r.Reconcile.Clear();
        Conclude(r, nonOwners, groups, errors, 0, 0, withProgressCheck: false);
    }

    static void Conclude(ScanResult r, List<HardLinkCand> nonOwners, int groups, int errors, long progLogical, long progAlloc, bool withProgressCheck)
    {
        var tree = r.Tree;
        ref var root = ref tree[0];
        long sumOwnL = 0, sumOwnA = 0; int mismatches = 0; long dirsChecked = 0;
        int n0 = tree.Count;
        for (int i = 0; i < n0; i++)
        {
            ref var nd = ref tree[i];
            if (nd.Parent == -2) continue;
            sumOwnL += nd.OwnLogical; sumOwnA += nd.OwnAllocated;
            if (nd.IsDir)
            {
                dirsChecked++;
                long cl = nd.OwnLogical, ca = nd.OwnAllocated, cr = nd.OwnResident;
                for (int k = 0; k < nd.ChildCount; k++)
                {
                    ref var ch = ref tree[nd.FirstChild + k];
                    if (ch.Parent == -2) continue;
                    cl += ch.Logical; ca += ch.Allocated; cr += ch.Resident;
                }
                if (cl != nd.Logical || ca != nd.Allocated || cr != nd.Resident) mismatches++;
            }
        }
        r.Consistency.Add(new ConsistencyItem { Name = "Tổng gốc (Trên đĩa) = Σ Own của mọi nút", Expected = sumOwnA.ToString("N0"), Actual = root.Allocated.ToString("N0"), Ok = sumOwnA == root.Allocated });
        r.Consistency.Add(new ConsistencyItem { Name = "Tổng gốc (Kích thước) = Σ Own của mọi nút", Expected = sumOwnL.ToString("N0"), Actual = root.Logical.ToString("N0"), Ok = sumOwnL == root.Logical });
        r.Consistency.Add(new ConsistencyItem { Name = $"Mỗi thư mục: Σ con + riêng = tổng ({dirsChecked:N0} thư mục)", Expected = "0 lệch", Actual = $"{mismatches:N0} lệch", Ok = mismatches == 0 });
        long savedL = nonOwners.Sum(x => x.Logical), savedA = nonOwners.Sum(x => x.Allocated);
        r.HardLinkNonOwnerCount = nonOwners.Count; r.HardLinkSavedAllocated = savedA;
        if (withProgressCheck && r.Completeness == Completeness.Complete)
        {
            r.Consistency.Add(new ConsistencyItem { Name = "Tiến độ trực tuyến − phần liên kết cứng không chủ = Tổng gốc (Kích thước)", Expected = (progLogical - savedL).ToString("N0"), Actual = root.Logical.ToString("N0"), Ok = progLogical - savedL == root.Logical });
            r.Consistency.Add(new ConsistencyItem { Name = "Tiến độ trực tuyến − phần liên kết cứng không chủ = Tổng gốc (Trên đĩa, gồm index thư mục)", Expected = (progAlloc - savedA).ToString("N0"), Actual = root.Allocated.ToString("N0"), Ok = progAlloc - savedA == root.Allocated });
        }
        r.Consistency.Add(new ConsistencyItem { Name = $"Liên kết cứng: {groups:N0} nhóm, mỗi nhóm đúng 1 chủ", Expected = "0 nhóm sai", Actual = $"{errors:N0} nhóm sai", Ok = errors == 0 });
        foreach (var ri in r.Roots.Where(x => x.IsVolumeRoot)) r.Reconcile.Add(Reconciler.Build(r, ri));
    }

    static string ExtOf(ScanTree t, int node)
    {
        var nm = t.Name(node); int dot = nm.LastIndexOf('.');
        return dot > 0 && dot < nm.Length - 1 ? nm[(dot + 1)..].ToString() : "";
    }
}

internal static class HardLinkResolver
{
    /// <summary>
    /// Quy tắc Q2: chủ = đường dẫn nhỏ nhất (OrdinalIgnoreCase). Trước hết khôi phục kích thước gốc của mọi thành viên (để tính lại
    /// được sau khi quét lại nhánh / xoá), rồi đặt Own*=0 cho các liên kết KHÔNG chủ. Trả danh sách liên kết không chủ.
    /// </summary>
    public static List<HardLinkCand> Resolve(ScanTree tree, List<HardLinkCand> cands, out int groups, out int errors)
    {
        var nonOwners = new List<HardLinkCand>(); groups = 0; errors = 0;
        foreach (var c in cands)
        {
            ref var n = ref tree[c.Node];
            if (n.Parent == -2) continue;
            n.OwnLogical = c.Logical; n.OwnAllocated = c.Allocated; n.OwnResident = c.Resident;
            n.Flags &= ~NodeFlags.HardLinkNonOwner;
        }
        foreach (var g in cands.Where(c => tree[c.Node].Parent != -2).GroupBy(c => (c.Serial, c.IdLo, c.IdHi)))
        {
            var list = g.ToList();
            if (list.Count < 2) continue;
            groups++;
            var ordered = list.Select(c => (c, path: tree.FullPath(c.Node))).OrderBy(x => x.path, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                var c = ordered[i].c;
                ref var n = ref tree[c.Node];
                n.OwnLogical = n.OwnAllocated = n.OwnResident = 0;
                n.Flags |= NodeFlags.HardLinkNonOwner;
                nonOwners.Add(c);
            }
            int ownersNow = ordered.Count(x => !tree[x.c.Node].Has(NodeFlags.HardLinkNonOwner));
            if (ownersNow != 1) errors++;
        }
        return nonOwners;
    }
}

internal static class Rollup
{
    /// <summary>Gộp từ lá lên gốc. Cha luôn có chỉ số nhỏ hơn con nên duyệt ngược là đủ. Bỏ qua nút chết (Parent = -2).</summary>
    public static void Run(ScanTree tree)
    {
        int n = tree.Count;
        for (int i = 0; i < n; i++)
        {
            ref var nd = ref tree[i];
            if (nd.Parent == -2) continue;
            nd.Logical = nd.OwnLogical; nd.Allocated = nd.OwnAllocated; nd.Resident = nd.OwnResident;
            if (nd.IsDir) { nd.FileCount = 0; nd.DirCount = 0; }
            else if (!nd.Has(NodeFlags.Aggregate)) nd.FileCount = 1;
        }
        for (int i = n - 1; i >= 1; i--)
        {
            ref var nd = ref tree[i];
            if (nd.Parent < 0) continue;
            ref var p = ref tree[nd.Parent];
            p.Logical += nd.Logical; p.Allocated += nd.Allocated; p.Resident += nd.Resident;
            if (nd.IsDir) { p.FileCount += nd.FileCount; p.DirCount += nd.DirCount + 1; }
            else p.FileCount += nd.FileCount;
            if (nd.MTime > p.MTime) p.MTime = nd.MTime;
        }
    }

    /// <summary>Thư mục chứa nhiều dữ liệu nhất — chỉ tính file trực tiếp (tránh lặp lại cha/con).</summary>
    public static List<TopDir> TopDirectories(ScanTree tree, int top)
    {
        var heap = new PriorityQueue<TopDir, long>();
        int n = tree.Count;
        for (int i = 1; i < n; i++)
        {
            ref var d = ref tree[i];
            if (d.Parent < 0 || !d.IsDir || d.ChildCount == 0) continue;
            long l = 0, a = 0;
            for (int k = 0; k < d.ChildCount; k++)
            {
                ref var c = ref tree[d.FirstChild + k];
                if (c.IsDir || c.Parent == -2) continue;
                l += c.Logical; a += c.Allocated;
            }
            if (a == 0 && l == 0) continue;
            if (heap.Count >= top) { if (heap.TryPeek(out _, out var min) && a <= min) continue; heap.Dequeue(); }
            heap.Enqueue(new TopDir(i, l, a), a);
        }
        return heap.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.DirectAllocated).ToList();
    }

    public static List<TopFile> TopFilesAll(ScanTree tree, int top)
    {
        var heap = new PriorityQueue<TopFile, long>();
        int n = tree.Count;
        for (int i = 1; i < n; i++)
        {
            ref var f = ref tree[i];
            if (f.Parent < 0 || f.IsDir || f.Has(NodeFlags.Aggregate) || f.Has(NodeFlags.HardLinkNonOwner)) continue;
            long a = f.OwnAllocated;
            if (heap.Count >= top) { if (heap.TryPeek(out _, out var min) && a <= min) continue; heap.Dequeue(); }
            heap.Enqueue(new TopFile(i, f.OwnLogical, a), a);
        }
        return heap.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Allocated).ThenByDescending(x => x.Logical).ToList();
    }

    public static List<TypeStat> TypeStatsAll(ScanTree tree)
    {
        var acc = new Dictionary<string, (long c, long l, long a)>(StringComparer.OrdinalIgnoreCase);
        var look = acc.GetAlternateLookup<ReadOnlySpan<char>>();
        int n = tree.Count;
        for (int i = 1; i < n; i++)
        {
            ref var f = ref tree[i];
            if (f.Parent < 0 || f.IsDir || f.Has(NodeFlags.Aggregate)) continue;          // đếm mọi mục; kích thước của liên kết không chủ đã là 0
            var name = tree.Name(i); int dot = name.LastIndexOf('.');
            var ext = dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : default;
            if (ext.Length > 24) ext = ext[..24];
            look.TryGetValue(ext, out var v);
            look[ext] = (v.c + 1, v.l + f.OwnLogical, v.a + f.OwnAllocated);
        }
        return acc.Select(kv => new TypeStat(kv.Key.ToLowerInvariant(), TypeCatalog.GroupOf(kv.Key), kv.Value.c, kv.Value.l, kv.Value.a))
            .OrderByDescending(t => t.Allocated).ToList();
    }
}
