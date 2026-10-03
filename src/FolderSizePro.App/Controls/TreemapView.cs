using System.Globalization;
using FolderSizePro.Analysis;
using FolderSizePro.App.ViewModels;
using FolderSizePro.Model;

namespace FolderSizePro.App.Controls;

/// <summary>
/// UC-08: treemap tương tác (squarified). Diện tích ô ∝ Trên đĩa (hoặc Kích thước). Bấm = chọn, bấm đúp thư mục = đi sâu,
/// chuột phải = menu. Tự vẽ bằng DrawingContext — không phụ thuộc thư viện ngoài.
/// </summary>
public sealed class TreemapView : FrameworkElement
{
    sealed class Cell { public Rect R; public int Node; public int Depth; public bool IsDir; public bool HasChildrenLaid; public Brush Fill = Brushes.Gray; public string Name = ""; public long Value; public bool Label; }

    ScanTree? _tree;
    int _root;
    bool _useAllocated = true;
    readonly List<Cell> _cells = new();
    Cell? _hover;
    int _selected = -1;
    Size _laidFor;
    bool _dirty = true;
    readonly Dictionary<string, Brush> _groupBrush = new();
    static readonly Typeface Face = new("Segoe UI");

    public int RootNode => _root;
    public event Action<int>? NodeClicked;
    public event Action<int>? NodeDrilled;
    public event Action<int, Point>? NodeContext;

    public TreemapView() { ClipToBounds = true; SnapsToDevicePixels = true; Focusable = true; }

    public void SetData(ScanTree? tree, int root, bool useAllocated)
    {
        _tree = tree; _root = root; _useAllocated = useAllocated; _dirty = true; _hover = null; InvalidateVisual();
    }

    public void SetSelected(int node) { _selected = node; InvalidateVisual(); }
    public void Refresh() { _dirty = true; InvalidateVisual(); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height);

    long ValueOf(int n) => _tree == null ? 0 : _useAllocated ? _tree[n].Allocated : _tree[n].Logical;

    Brush BrushForFile(int node)
    {
        var nm = _tree!.Name(node); int dot = nm.LastIndexOf('.');
        string ext = dot > 0 ? nm[(dot + 1)..].ToString().ToLowerInvariant() : "";
        string g = TypeCatalog.GroupOf(ext);
        if (!_groupBrush.TryGetValue(g, out var b)) { b = new SolidColorBrush(Palette.Of(g)); b.Freeze(); _groupBrush[g] = b; }
        return b;
    }

    static readonly Brush[] DirBrushes = Enumerable.Range(0, 8).Select(i =>
    {
        byte v = (byte)(0x3A + i * 8); var b = new SolidColorBrush(Color.FromRgb(v, (byte)(v + 4), (byte)(v + 12))); b.Freeze(); return (Brush)b;
    }).ToArray();

    void Layout(Size size)
    {
        _cells.Clear();
        if (_tree == null || size.Width < 8 || size.Height < 8) return;
        var t = _tree;
        if (_root < 0 || _root >= t.Count || t[_root].Parent == -2) return;
        LayoutChildren(_root, new Rect(0, 0, size.Width, size.Height), 0);
        _laidFor = size; _dirty = false;
    }

    void LayoutChildren(int parent, Rect area, int depth)
    {
        var t = _tree!;
        ref var p = ref t[parent];
        if (!p.IsDir || p.ChildCount == 0 || depth > 7) return;
        var kids = new List<(int Node, long V)>(Math.Min(p.ChildCount, 4000));
        for (int i = 0; i < p.ChildCount; i++)
        {
            int c = p.FirstChild + i;
            if (t[c].Parent == -2) continue;
            long v = ValueOf(c);
            if (v > 0) kids.Add((c, v));
        }
        if (kids.Count == 0) return;
        kids.Sort((a, b) => b.V.CompareTo(a.V));
        double total = kids.Sum(k => (double)k.V);
        // bỏ các ô nhỏ hơn ~2 px² để khỏi vẽ hàng chục nghìn ô vô hình
        double minArea = 6.0; double scale = area.Width * area.Height / total;
        int keep = kids.Count; while (keep > 1 && kids[keep - 1].V * scale < minArea) keep--;
        var items = kids.Take(keep).ToList();
        double tot2 = items.Sum(k => (double)k.V);
        var rects = Squarify(items, area, tot2);
        for (int i = 0; i < items.Count; i++)
        {
            var r = rects[i]; if (r.Width < 1 || r.Height < 1) continue;
            int n = items[i].Node; bool isDir = t[n].IsDir;
            var cell = new Cell { R = r, Node = n, Depth = depth, IsDir = isDir, Value = items[i].V, Name = t.NameString(n) };
            cell.Fill = isDir ? DirBrushes[Math.Min(depth, 7)] : BrushForFile(n);
            cell.Label = r.Width >= 54 && r.Height >= 15;
            _cells.Add(cell);
            if (isDir && r.Width > 30 && r.Height > 24 && t[n].ChildCount > 0)
            {
                double header = r.Height > 40 && r.Width > 70 ? 16 : 0;
                var inner = new Rect(r.X + 2, r.Y + 2 + header, Math.Max(0, r.Width - 4), Math.Max(0, r.Height - 4 - header));
                if (inner.Width > 12 && inner.Height > 12) { cell.HasChildrenLaid = true; LayoutChildren(n, inner, depth + 1); }
            }
        }
    }

    static List<Rect> Squarify(List<(int Node, long V)> items, Rect area, double total)
    {
        var result = new Rect[items.Count];
        double scale = area.Width * area.Height / total;
        var rem = area; int i = 0;
        while (i < items.Count)
        {
            double side = Math.Min(rem.Width, rem.Height);
            if (side <= 0) { for (; i < items.Count; i++) result[i] = new Rect(rem.X, rem.Y, 0, 0); break; }
            int j = i; double sum = 0, worst = double.MaxValue;
            while (j < items.Count)
            {
                double a = items[j].V * scale; double s2 = sum + a;
                double min = Math.Min(items[i].V * scale, a), max = Math.Max(items[i].V * scale, a);
                // tỉ lệ xấu nhất khi thêm phần tử j: đo trên phần tử lớn nhất (i) và nhỏ nhất (j)
                double big = items[i].V * scale, small = items[j].V * scale;
                double w = Math.Max(side * side * big / (s2 * s2), s2 * s2 / (side * side * small));
                if (j > i && w > worst) break;
                worst = w; sum = s2; j++;
            }
            // dựng hàng [i, j)
            double thick = sum / side;
            double pos = 0;
            for (int k = i; k < j; k++)
            {
                double len = items[k].V * scale / thick;
                result[k] = rem.Width >= rem.Height
                    ? new Rect(rem.X, rem.Y + pos, thick, len)
                    : new Rect(rem.X + pos, rem.Y, len, thick);
                pos += len;
            }
            rem = rem.Width >= rem.Height
                ? new Rect(rem.X + thick, rem.Y, Math.Max(0, rem.Width - thick), rem.Height)
                : new Rect(rem.X, rem.Y + thick, rem.Width, Math.Max(0, rem.Height - thick));
            i = j;
        }
        return result.ToList();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (_dirty || size != _laidFor) Layout(size);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        if (_cells.Count == 0)
        {
            var ft = new FormattedText(L.T("Chưa có dữ liệu để vẽ treemap.", "Nothing to draw yet."), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 13, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(16, 16)); return;
        }
        var line = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1); line.Freeze();
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var c in _cells)
        {
            bool container = c.IsDir && c.HasChildrenLaid;
            if (!container) dc.DrawRectangle(c.Fill, line, c.R);
            else
            {
                dc.DrawRectangle(c.Fill, line, c.R);
            }
        }
        // nhãn: vẽ sau cùng để không bị ô con che
        foreach (var c in _cells)
        {
            if (!c.Label) continue;
            bool container = c.IsDir && c.HasChildrenLaid;
            if (container && !(c.R.Height > 40 && c.R.Width > 70)) continue;
            string text = c.Name;
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, Brushes.White, dpi)
            { MaxTextWidth = Math.Max(10, c.R.Width - 6), MaxTextHeight = 18, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(ft, new Point(c.R.X + 3, c.R.Y + 1));
            if (!container && c.R.Height >= 30 && c.R.Width >= 60)
            {
                var ft2 = new FormattedText(Fmt.Size(c.Value), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 10, new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), dpi) { MaxTextWidth = Math.Max(10, c.R.Width - 6), MaxTextHeight = 16 };
                dc.DrawText(ft2, new Point(c.R.X + 3, c.R.Y + 14));
            }
        }
        if (_selected > 0)
        {
            var sel = _cells.FirstOrDefault(c => c.Node == _selected);
            if (sel != null) dc.DrawRectangle(null, new Pen(Brushes.White, 2.5), Deflate(sel.R, 1));
        }
        if (_hover != null) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), new Pen(Brushes.Yellow, 1.5), Deflate(_hover.R, 0.75));
    }

    static Rect Deflate(Rect r, double d) => new(r.X + d, r.Y + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));

    /// <summary>Ô sâu nhất chứa điểm (các ô con được thêm sau ô cha nên duyệt ngược).</summary>
    Cell? HitTest(Point p)
    {
        for (int i = _cells.Count - 1; i >= 0; i--) if (_cells[i].R.Contains(p)) return _cells[i];
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var c = HitTest(e.GetPosition(this));
        if (!ReferenceEquals(c, _hover))
        {
            _hover = c; InvalidateVisual();
            ToolTip = c == null || _tree == null ? null : $"{_tree.FullPath(c.Node)}\n{L.T("Trên đĩa", "On disk")}: {Fmt.Size(_tree[c.Node].Allocated)}   {L.T("Kích thước", "Size")}: {Fmt.Size(_tree[c.Node].Logical)}" +
                (c.IsDir ? $"\n{Fmt.Count(_tree[c.Node].FileCount)} {L.T("file", "files")}" : "");
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e) { _hover = null; InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var c = HitTest(e.GetPosition(this)); if (c == null) return;
        if (e.ClickCount >= 2 && c.IsDir) NodeDrilled?.Invoke(c.Node);
        else NodeClicked?.Invoke(c.Node);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var c = HitTest(e.GetPosition(this)); if (c == null) return;
        NodeClicked?.Invoke(c.Node);
        NodeContext?.Invoke(c.Node, e.GetPosition(this));
    }
}
