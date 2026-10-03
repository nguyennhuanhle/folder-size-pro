using FolderSizePro.Model;

namespace FolderSizePro.App.ViewModels;

/// <summary>Một dòng của cây (danh sách phẳng + thụt lề). Số liệu đọc thẳng từ arena nên luôn mới.</summary>
public sealed class TreeRow : Observable
{
    readonly Func<ScanTree?> _tree;
    public int Node { get; }
    public int Depth { get; }
    /// <summary>Dòng ảo "… và N mục nữa" (Node = -1).</summary>
    public string? MoreText { get; init; }
    public bool IsMore => MoreText != null;
    public int ParentNode { get; }
    bool _expanded;
    public bool WantsExpand { get; set; }

    public TreeRow(Func<ScanTree?> tree, int node, int depth, int parentNode)
    { _tree = tree; Node = node; Depth = depth; ParentNode = parentNode; }

    ScanTree? T => _tree();
    bool Live => !IsMore && T != null && Node >= 0 && Node < T.Count && T[Node].Parent != -2;

    public bool IsExpanded { get => _expanded; set { if (Set(ref _expanded, value)) Raise(nameof(Glyph)); } }
    public bool IsDir => Live && T![Node].IsDir;
    public bool HasChildren => Live && T![Node].IsDir && T[Node].ChildCount > 0;
    public bool CanExpand => IsDir && (HasChildren || T![Node].Has(NodeFlags.Pending));
    public double IndentWidth => Depth * 18;

    public string Name => IsMore ? MoreText! : !Live ? "" : T!.NameString(Node);
    public string Glyph => IsMore ? "" : !Live ? "" : IsDir ? (_expanded ? "" : "") : "";
    public string ExpanderGlyph => !CanExpand ? "" : _expanded ? "" : "";

    public string LogicalText => !Live ? "" : T![Node].Has(NodeFlags.AccessDenied) || T[Node].Has(NodeFlags.CloudNotListed) ? "—" : Fmt.Size(T[Node].Logical);
    public string AllocatedText => !Live ? "" : T![Node].Has(NodeFlags.AccessDenied) || T[Node].Has(NodeFlags.CloudNotListed) ? "—" : Fmt.Size(T[Node].Allocated);
    public string LogicalExact => !Live ? "" : Fmt.Exact(T![Node].Logical);
    public string AllocatedExact => !Live ? "" : Fmt.Exact(T![Node].Allocated);
    public string FilesText => !Live || !IsDir ? "" : Fmt.Count(T![Node].FileCount);
    public string DirsText => !Live || !IsDir ? "" : Fmt.Count(T![Node].DirCount);
    public string ModifiedText => !Live ? "" : Fmt.Date(T![Node].MTime);
    public string Badges => !Live ? "" : Fmt.Badges(T![Node]);

    public double PercentValue
    {
        get
        {
            if (!Live) return 0;
            var t = T!; long parent = ParentNode > 0 ? t[ParentNode].Allocated : t[Node].Allocated;
            return parent > 0 ? Math.Clamp((double)t[Node].Allocated / parent, 0, 1) : 0;
        }
    }
    public string PercentText => !Live || ParentNode <= 0 ? "" : ByteFormatter.Percent(PercentValue);

    /// <summary>0 bình thường · 1 mờ (không tính / chưa quét) · 2 lỗi (không truy cập được, mất kết nối).</summary>
    public int Severity
    {
        get
        {
            if (!Live) return 1;
            var n = T![Node];
            if (n.Has(NodeFlags.AccessDenied) || n.Has(NodeFlags.Lost) || n.Has(NodeFlags.TimedOut)) return 2;
            if (n.Has(NodeFlags.HardLinkNonOwner) || n.Has(NodeFlags.Excluded) || n.Has(NodeFlags.Pending) || n.Has(NodeFlags.CloudNotListed)) return 1;
            return 0;
        }
    }

    public void Refresh() => RaiseAll();
}
