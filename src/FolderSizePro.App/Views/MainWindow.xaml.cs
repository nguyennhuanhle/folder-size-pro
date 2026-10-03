using System.Windows.Interop;
using System.Windows.Threading;
using FolderSizePro.App.ViewModels;
using FolderSizePro.Storage;

namespace FolderSizePro.App.Views;

public partial class MainWindow : Window, IShell
{
    readonly MainViewModel _vm;
    int _treemapRoot = -1;
    double _savedOffset;
    readonly Dictionary<string, string> _headerBase = new();

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm; DataContext = vm; vm.Shell = this;
        Width = Math.Max(MinWidth, vm.Settings.WindowWidth); Height = Math.Max(MinHeight, vm.Settings.WindowHeight);
        vm.RowsResetting += () => { _savedOffset = Scroller()?.VerticalOffset ?? 0; };
        vm.RowsReset += OnRowsReset;
        vm.ViewsChanged += RefreshTreemap;
        Loaded += (_, _) =>
        {
            foreach (var col in ((GridView)TreeList.View).Columns) if (col.Header is GridViewColumnHeader h && h.Tag is string tag) _headerBase[tag] = h.Content?.ToString() ?? "";
            UpdateHeaderArrows();
            Treemap.NodeClicked += n => { _vm.SelectNode(n, switchTab: false); Treemap.SetSelected(n); };
            Treemap.NodeDrilled += n => { _treemapRoot = n; RefreshTreemap(); };
            Treemap.NodeContext += (n, p) => ShowNodeMenu(Treemap);
        };
        PathBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.KeyDownEvent, new KeyEventHandler((s, e) => { if (e.Key == Key.Enter) { _vm.StartCommand.Execute(null); e.Handled = true; } }));
        Closing += (_, _) => { _vm.Settings.WindowWidth = ActualWidth; _vm.Settings.WindowHeight = ActualHeight; SettingsStore.Save(_vm.Settings); _vm.Shutdown(); };
        Drop += OnDrop; DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { Tabs.SelectedIndex = 0; FilterBox.Focus(); e.Handled = true; }
            else if (e.Key == Key.F5 && _vm.RescanBranchCommand.CanExecute(null)) { _vm.RescanBranchCommand.Execute(null); e.Handled = true; }
        };
    }

    // ------------------------------------------------------------------ cắm / rút ổ (UC-51)
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(Hwnd)?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (msg == 0x0219 && ((int)wp is 0x8000 or 0x8004)) _vm.RefreshDrivesSoon();         // WM_DEVICECHANGE: arrival / removal
            return IntPtr.Zero;
        });
    }

    // ------------------------------------------------------------------ IShell
    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public string? PickFolder()
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Chọn thư mục hoặc ổ đĩa cần quét", "Pick a folder or drive to scan") };
        return d.ShowDialog(this) == true ? d.FolderName : null;
    }

    public string? PickOpenFsp()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Folder Size Pro snapshot (*.fsp)|*.fsp|All|*.*" };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public string? PickSave(string filter, string defaultName)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return d.ShowDialog(this) == true ? d.FileName : null;
    }

    public void Info(string title, string text) => Dialogs.Info(this, title, text);
    public bool Confirm(string title, string text, string ok, string cancel) => Dialogs.Confirm(this, title, text, ok, cancel);
    public int Choose(string title, string text, params string[] buttons) => Dialogs.Choose(this, title, text, buttons);
    public bool ConfirmDelete(DeleteRequest req) => Dialogs.ConfirmDelete(this, req);
    public void CopyText(string text) { try { Clipboard.SetText(text); } catch { } }
    public IReadOnlyList<TreeRow> SelectedRows() => TreeList.SelectedItems.OfType<TreeRow>().ToList();
    public void SelectTab(string key) { foreach (TabItem t in Tabs.Items) if ((string?)t.Tag == key) { Tabs.SelectedItem = t; break; } }
    public void ScrollRowIntoView(TreeRow row) { TreeList.SelectedItem = row; TreeList.ScrollIntoView(row); TreeList.Focus(); }

    // ------------------------------------------------------------------ cây
    ScrollViewer? Scroller() => FindChild<ScrollViewer>(TreeList);

    static T? FindChild<T>(DependencyObject o) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
        {
            var c = VisualTreeHelper.GetChild(o, i);
            if (c is T t) return t;
            var r = FindChild<T>(c); if (r != null) return r;
        }
        return null;
    }

    void OnRowsReset()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Scroller()?.ScrollToVerticalOffset(_savedOffset);
            if (_vm.SelectedRow != null && TreeList.Items.Contains(_vm.SelectedRow)) TreeList.SelectedItem = _vm.SelectedRow;
        }, DispatcherPriority.Loaded);
    }

    void OnTreeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TreeList.SelectedItem is TreeRow r) { _vm.SelectedRow = r; Treemap.SetSelected(r.Node); }
        else if (TreeList.SelectedItems.Count == 0 && e.RemovedItems.Count > 0 && _vm.SelectedRow != null && !TreeList.Items.Contains(_vm.SelectedRow)) { }
    }

    void OnExpanderClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TreeRow row) { _vm.ToggleExpand(row); e.Handled = true; }
    }

    void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<GridViewColumnHeader>(d) != null) return;
        if (TreeList.SelectedItem is not TreeRow row || row.IsMore) return;
        if (row.IsDir) _vm.ToggleExpand(row); else _vm.OpenInExplorerCommand.Execute(null);
    }

    static T? FindAncestor<T>(DependencyObject o) where T : DependencyObject
    {
        for (var p = o; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p)) if (p is T t) return t;
        return null;
    }

    void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        var row = TreeList.SelectedItem as TreeRow;
        switch (e.Key)
        {
            case Key.Right when row is { IsDir: true, IsExpanded: false }: _vm.ExpandRow(row); e.Handled = true; break;
            case Key.Right when row is { IsDir: true, IsExpanded: true } && TreeList.SelectedIndex + 1 < TreeList.Items.Count: TreeList.SelectedIndex++; e.Handled = true; break;
            case Key.Left when row is { IsExpanded: true }: _vm.CollapseRow(row); e.Handled = true; break;
            case Key.Left when row != null:
                { var parent = _vm.Rows.FirstOrDefault(r => r.Node == row.ParentNode); if (parent != null) { TreeList.SelectedItem = parent; TreeList.ScrollIntoView(parent); } e.Handled = true; break; }
            case Key.Enter when row != null: _vm.ToggleExpand(row); e.Handled = true; break;
            case Key.Delete: if (_vm.DeleteCommand.CanExecute(null)) _vm.DeleteCommand.Execute(null); e.Handled = true; break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control: _vm.CopyPathCommand.Execute(null); e.Handled = true; break;
        }
    }

    void OnTreeHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Tag: string tag } && Enum.TryParse<SortKey>(tag, out var key)) { _vm.SetSort(key); UpdateHeaderArrows(); }
    }

    void UpdateHeaderArrows()
    {
        foreach (var col in ((GridView)TreeList.View).Columns)
            if (col.Header is GridViewColumnHeader { Tag: string tag } h && _headerBase.TryGetValue(tag, out var b))
                h.Content = b + (tag == _vm.Sort.ToString() ? (_vm.SortDescending ? " ▼" : " ▲") : "");
    }

    void OnTreeContextMenu(object sender, ContextMenuEventArgs e)
    {
        if (TreeList.SelectedItem == null) { e.Handled = true; return; }
        ShowNodeMenu(TreeList); e.Handled = true;
    }

    void ShowNodeMenu(FrameworkElement target)
    {
        var m = new ContextMenu();
        MenuItem Item(string t, ICommand c, string? glyph = null) => new() { Header = t, Command = c, Icon = glyph == null ? null : new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont") } };
        m.Items.Add(Item(L.T("Mở trong Explorer", "Show in Explorer"), _vm.OpenInExplorerCommand, ""));
        m.Items.Add(Item(L.T("Sao chép đường dẫn", "Copy path"), _vm.CopyPathCommand, ""));
        m.Items.Add(Item(L.T("Properties (Windows)", "Properties (Windows)"), _vm.PropertiesCommand, ""));
        m.Items.Add(new Separator());
        m.Items.Add(Item(L.T("Quét lại nhánh này", "Rescan this branch"), _vm.RescanBranchCommand, ""));
        m.Items.Add(Item(L.T("Lọc: cùng đuôi file", "Filter: same extension"), _vm.FilterSameExtensionCommand, ""));
        var tm = new MenuItem { Header = L.T("Hiện trong Treemap", "Show in Treemap") };
        tm.Click += (_, _) => { if (_vm.SelectedRow is { IsDir: true } r) { _treemapRoot = r.Node; SelectTab("treemap"); RefreshTreemap(); } };
        m.Items.Add(tm);
        m.Items.Add(new Separator());
        m.Items.Add(Item(L.T("Đưa vào Thùng rác…", "Move to Recycle Bin…"), _vm.DeleteCommand, ""));
        m.PlacementTarget = target; m.IsOpen = true;
    }

    // ------------------------------------------------------------------ tìm
    void OnClearFilter(object sender, RoutedEventArgs e) => _vm.ClearFilter();

    void OnSearchSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SearchList.SelectedItem is SearchRow r) _vm.SelectNode(r.Node, switchTab: false);
    }

    void OnSearchDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is SearchRow r) { _vm.ClearFilter(); _vm.RevealNode(r.Node); }
    }

    // ------------------------------------------------------------------ Top lớn nhất
    void OnTopFileDoubleClick(object sender, MouseButtonEventArgs e) { if (((ListView)sender).SelectedItem is FileRow r) _vm.RevealNode(r.Node); }
    void OnTopDirDoubleClick(object sender, MouseButtonEventArgs e) { if (((ListView)sender).SelectedItem is DirRow r) _vm.RevealNode(r.Node); }

    // ------------------------------------------------------------------ treemap
    void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs) return;
        if (Tabs.SelectedItem is TabItem { Tag: "treemap" }) RefreshTreemap();
    }

    void RefreshTreemap()
    {
        var t = _vm.Tree;
        if (t == null) { Treemap.SetData(null, 0, true); TreemapCrumb.Text = ""; return; }
        if (_treemapRoot < 0 || _treemapRoot >= t.Count || t[_treemapRoot].Parent == -2) _treemapRoot = _vm.Result!.Roots.Count == 1 ? _vm.Result.Roots[0].Node : 0;
        Treemap.SetData(t, _treemapRoot, TmLogical.IsChecked != true);
        TreemapCrumb.Text = _treemapRoot == 0 ? L.T("Tất cả gốc quét", "All scan roots") : t.FullPath(_treemapRoot);
    }

    void OnTreemapUp(object sender, RoutedEventArgs e)
    {
        var t = _vm.Tree; if (t == null || _treemapRoot <= 0) return;
        int p = t[_treemapRoot].Parent;
        _treemapRoot = p > 0 ? p : (_vm.Result!.Roots.Count == 1 ? _treemapRoot : 0);
        RefreshTreemap();
    }

    void OnTreemapBasis(object sender, RoutedEventArgs e) => RefreshTreemap();

    // ------------------------------------------------------------------ so sánh
    async void OnPickA(object s, RoutedEventArgs e) { _vm.PickCompare(true); CmpA.Text = _vm.CompareAText; }
    async void OnPickB(object s, RoutedEventArgs e) { _vm.PickCompare(false); CmpB.Text = _vm.CompareBText; }
    void OnUseA(object s, RoutedEventArgs e) { _vm.UseCurrentAs(true); CmpA.Text = _vm.CompareAText; }
    void OnUseB(object s, RoutedEventArgs e) { _vm.UseCurrentAs(false); CmpB.Text = _vm.CompareBText; }
    async void OnRunCompare(object s, RoutedEventArgs e) => await _vm.RunCompareAsync();

    // ------------------------------------------------------------------ thanh công cụ
    void OnSnapshotMenu(object sender, RoutedEventArgs e)
    {
        var m = new ContextMenu();
        m.Items.Add(new MenuItem { Header = L.T("Lưu kết quả thành snapshot (.fsp)…", "Save result as snapshot (.fsp)…"), Command = _vm.SaveSnapshotCommand });
        m.Items.Add(new MenuItem { Header = L.T("Mở snapshot…", "Open snapshot…"), Command = _vm.OpenSnapshotCommand });
        var cmp = new MenuItem { Header = L.T("So sánh hai snapshot…", "Compare two snapshots…") };
        cmp.Click += (_, _) => SelectTab("compare");
        m.Items.Add(cmp);
        var cross = new MenuItem { Header = L.T("Đối chứng hai chế độ quét (MFT ↔ thường)…", "Cross-check scan modes (MFT ↔ normal)…") };
        cross.Click += (_, _) => _vm.CrossCheckModes();
        m.Items.Add(cross);
        m.PlacementTarget = (UIElement)sender; m.IsOpen = true;
    }

    void OnExportMenu(object sender, RoutedEventArgs e)
    {
        var m = new ContextMenu();
        foreach (var (k, t) in new[] { ("html", L.T("Báo cáo HTML…", "HTML report…")), ("csv", "CSV…"), ("json", "JSON…") })
            m.Items.Add(new MenuItem { Header = t, Command = _vm.ExportCommand, CommandParameter = k });
        m.PlacementTarget = (UIElement)sender; m.IsOpen = true;
    }

    void OnSettings(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_vm.Settings, this); w.ShowDialog();
        if (!w.Saved) return;
        SettingsStore.Save(_vm.Settings);
        App.ApplyTheme(_vm.Settings.Theme);
        _vm.ApplySettings();
        if (w.LanguageChanged) Info(L.T("Ngôn ngữ", "Language"), L.T("Hãy đóng và mở lại Folder Size Pro để đổi ngôn ngữ.", "Close and reopen Folder Size Pro to change the language."));
    }

    void OnRecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((ListBox)sender).SelectedItem is string s) { _vm.PathText = s; _vm.StartCommand.Execute(null); }
    }

    void OnBannerAction(object sender, RoutedEventArgs e) { if (((FrameworkElement)sender).Tag is BannerItem { Action: { } a }) a(); }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var fsp = files.FirstOrDefault(f => f.EndsWith(".fsp", StringComparison.OrdinalIgnoreCase));
        if (fsp != null && files.Length == 1) { _vm.OpenSnapshot(fsp); return; }
        _vm.PathText = string.Join(" | ", files);
        _vm.StartScan(files);
    }

    // ------------------------------------------------------------------ chụp ảnh cửa sổ (dev: --screenshot)
    public void PrepareForScreenshot(App.Args a)
    {
        if (a.Expand != null) { int n = _vm.FindNodeByPath(a.Expand); if (n > 0) _vm.SelectNode(n, switchTab: false); }
        if (a.Tab != null) SelectTab(a.Tab);
        UpdateLayout();
    }

    public void SaveScreenshot(string path) => ScreenshotUtil.Save(this, path);
}

internal static class ScreenshotUtil
{
    public static void Save(Window win, string path)
    {
        win.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(win);
        int w = (int)Math.Ceiling(win.ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(win.ActualHeight * dpi.DpiScaleY);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bool dark = Application.Current.ThemeMode == ThemeMode.Dark ||
                    (Application.Current.ThemeMode != ThemeMode.Light && Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, win.ActualWidth, win.ActualHeight));
        rtb.Render(dv);
        rtb.Render((Visual)win.Content);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(path); enc.Save(fs);
    }
}
