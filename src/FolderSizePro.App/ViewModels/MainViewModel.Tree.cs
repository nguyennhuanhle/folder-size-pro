using System.Collections.ObjectModel;
using FolderSizePro.Model;

namespace FolderSizePro.App.ViewModels;

public enum SortKey { Name, Logical, Allocated, Files, Dirs, Modified }

public sealed class RangeCollection<T> : ObservableCollection<T>
{
    /// <summary>Thay toàn bộ nội dung bằng MỘT thông báo Reset (nhanh với hàng nghìn dòng).</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}

public sealed partial class MainViewModel
{
    public RangeCollection<TreeRow> Rows { get; } = new();
    readonly HashSet<int> _expanded = new();
    const int MaxChildrenShown = 2000;
    const int IncrementalLimit = 250;

    SortKey _sortKey = SortKey.Allocated;
    bool _sortDesc = true;
    public SortKey Sort => _sortKey;
    public bool SortDescending => _sortDesc;

    /// <summary>Báo cho cửa sổ biết danh sách vừa bị Reset để khôi phục chọn / vị trí cuộn.</summary>
    public event Action? RowsReset;
    public event Action? RowsResetting;

    public void SetSort(SortKey key)
    {
        if (_sortKey == key) _sortDesc = !_sortDesc; else { _sortKey = key; _sortDesc = key != SortKey.Name; }
        Raise(nameof(Sort)); Raise(nameof(SortDescending));
        RebuildRows();
    }

    IEnumerable<int> LiveChildren(int node)
    {
        var t = Tree; if (t == null) yield break;
        ref var n = ref t[node];
        if (!n.IsDir) yield break;
        int f = n.FirstChild, c = n.ChildCount;
        bool showHidden = Settings.ShowHiddenSystem;
        for (int i = 0; i < c; i++)
        {
            ref var ch = ref t[f + i];
            if (ch.Parent == -2) continue;
            if (!showHidden && (ch.Has(NodeFlags.Hidden) || ch.Has(NodeFlags.System))) continue;
            yield return f + i;
        }
    }

    List<int> SortedChildren(int node)
    {
        var t = Tree!;
        var list = LiveChildren(node).ToList();
        Comparison<int> cmp = _sortKey switch
        {
            SortKey.Name => (a, b) => string.Compare(t.NameString(a), t.NameString(b), StringComparison.OrdinalIgnoreCase),
            SortKey.Logical => (a, b) => t[a].Logical.CompareTo(t[b].Logical),
            SortKey.Files => (a, b) => t[a].FileCount.CompareTo(t[b].FileCount),
            SortKey.Dirs => (a, b) => t[a].DirCount.CompareTo(t[b].DirCount),
            SortKey.Modified => (a, b) => t[a].MTime.CompareTo(t[b].MTime),
            _ => (a, b) => t[a].Allocated.CompareTo(t[b].Allocated),
        };
        int sign = _sortDesc ? -1 : 1;
        list.Sort((a, b) => { int c = cmp(a, b) * sign; return c != 0 ? c : string.Compare(t.NameString(a), t.NameString(b), StringComparison.OrdinalIgnoreCase); });
        return list;
    }

    TreeRow NewRow(int node, int depth, int parent) => new(() => Tree, node, depth, parent) { IsExpanded = _expanded.Contains(node) };

    IEnumerable<TreeRow> ChildRows(int parentNode, int depth)
    {
        var kids = SortedChildren(parentNode);
        int shown = Math.Min(kids.Count, MaxChildrenShown);
        for (int i = 0; i < shown; i++) yield return NewRow(kids[i], depth, parentNode);
        if (kids.Count > shown)
            yield return new TreeRow(() => Tree, -1, depth, parentNode)
            { MoreText = L.T($"… và {kids.Count - shown:N0} mục nữa (dùng ô Tìm / Lọc hoặc Top lớn nhất để xem)", $"… and {kids.Count - shown:N0} more (use Search / Filter or Largest items)") };
    }

    IEnumerable<TreeRow> Enumerate(int parentNode, int depth)
    {
        foreach (var row in ChildRows(parentNode, depth))
        {
            yield return row;
            if (!row.IsMore && _expanded.Contains(row.Node) && row.IsDir)
                foreach (var c in Enumerate(row.Node, depth + 1)) yield return c;
        }
    }

    void BuildRootRows()
    {
        var t = Tree; if (t == null) return;
        var roots = Result!.Roots;
        if (roots.Count == 1) _expanded.Add(roots[0].Node);
        RebuildRows();
    }

    public void RebuildRows()
    {
        var t = Tree; if (t == null) { Rows.Clear(); return; }
        int keep = SelectedRow?.Node ?? -1;
        RowsResetting?.Invoke();
        Rows.ReplaceAll(Enumerate(0, 0));
        if (keep > 0) { var again = Rows.FirstOrDefault(r => r.Node == keep); if (again != null) SelectedRow = again; }
        RowsReset?.Invoke();
    }

    /// <summary>Cập nhật số liệu của các dòng đang hiện + nạp con cho nhánh đang mở mà vừa có dữ liệu (lúc đang quét).</summary>
    void RefreshRows()
    {
        List<TreeRow>? toFill = null;
        foreach (var r in Rows)
        {
            r.Refresh();
            if (r.WantsExpand && r.HasChildren) (toFill ??= new()).Add(r);
        }
        if (toFill != null) foreach (var r in toFill) { r.WantsExpand = false; ExpandRow(r); }
    }

    public void ToggleExpand(TreeRow row)
    {
        if (row.IsMore || !row.IsDir) return;
        if (row.IsExpanded) CollapseRow(row); else ExpandRow(row);
    }

    public void ExpandRow(TreeRow row)
    {
        if (row.IsMore || !row.IsDir || Tree == null) return;
        _expanded.Add(row.Node); row.IsExpanded = true;
        if (!row.HasChildren) { row.WantsExpand = true; row.Refresh(); return; }       // đang quét: nạp khi có dữ liệu
        int idx = Rows.IndexOf(row); if (idx < 0) return;
        if (idx + 1 < Rows.Count && Rows[idx + 1].Depth > row.Depth) return;           // đã có con
        var kids = Enumerate(row.Node, row.Depth + 1).ToList();
        if (kids.Count > IncrementalLimit) { RebuildRows(); return; }
        for (int i = 0; i < kids.Count; i++) Rows.Insert(idx + 1 + i, kids[i]);
        row.Refresh();
    }

    public void CollapseRow(TreeRow row)
    {
        _expanded.Remove(row.Node); row.IsExpanded = false; row.WantsExpand = false;
        int idx = Rows.IndexOf(row); if (idx < 0) return;
        int end = idx + 1; while (end < Rows.Count && Rows[end].Depth > row.Depth) end++;
        int count = end - idx - 1;
        if (count > IncrementalLimit) { RebuildRows(); return; }
        for (int i = 0; i < count; i++) Rows.RemoveAt(idx + 1);
        row.Refresh();
    }

    /// <summary>Mở dần các tổ tiên của một nút rồi chọn nó (từ Top lớn nhất, kết quả tìm, treemap…).</summary>
    public void RevealNode(int node) => SelectNode(node, switchTab: true);

    public void SelectNode(int node, bool switchTab)
    {
        var t = Tree; if (t == null || node <= 0 || t[node].Parent == -2) return;
        if (SelectedRow?.Node == node) return;
        var chain = new List<int>(); for (int i = t[node].Parent; i > 0; i = t[i].Parent) chain.Add(i);
        bool changed = false; foreach (var a in chain) changed |= _expanded.Add(a);
        var row = Rows.FirstOrDefault(r => r.Node == node);
        if (changed || row == null) { RebuildRows(); row = Rows.FirstOrDefault(r => r.Node == node); }
        if (row != null) { SelectedRow = row; Shell.ScrollRowIntoView(row); }
        if (switchTab) Shell.SelectTab("tree");
    }
}
