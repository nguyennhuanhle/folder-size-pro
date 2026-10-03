using System.Windows.Documents;
using FolderSizePro.App.ViewModels;

namespace FolderSizePro.App.Views;

/// <summary>Hộp thoại dựng bằng code (ít, nhỏ): chọn nút, thông báo, xác nhận xoá.</summary>
public static class Dialogs
{
    static Window Make(Window? owner, string title, double width = 520)
    {
        var w = new Window
        {
            Title = title, Owner = owner, SizeToContent = SizeToContent.Height, Width = width, MaxHeight = 640,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
        };
        return w;
    }

    static TextBlock Body(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    public static int Choose(Window? owner, string title, string text, params string[] buttons)
    {
        var w = Make(owner, title); int result = -1;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Body(text));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = buttons[i], Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0), IsDefault = i == 0 };
            if (i == 0) b.Style = (Style?)Application.Current.TryFindResource("AccentButtonStyle");
            b.Click += (_, _) => { result = idx; w.Close(); };
            row.Children.Add(b);
        }
        panel.Children.Add(row);
        w.Content = panel; w.ShowDialog();
        return result;
    }

    public static void Info(Window? owner, string title, string text)
    {
        var w = Make(owner, title, 620);
        var tb = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var ok = new Button { Content = "OK", Padding = new Thickness(22, 6, 22, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsDefault = true, IsCancel = true };
        ok.Style = (Style?)Application.Current.TryFindResource("AccentButtonStyle");
        ok.Click += (_, _) => w.Close();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(tb); panel.Children.Add(ok);
        w.Content = panel; w.ShowDialog();
    }

    public static bool Confirm(Window? owner, string title, string text, string ok, string cancel) => Choose(owner, title, text, ok, cancel) == 0;

    /// <summary>UC-13: hộp xác nhận xoá — nói thật hai con số: giải phóng ngay (0) và sẽ giải phóng sau khi dọn Thùng rác.</summary>
    public static bool ConfirmDelete(Window? owner, DeleteRequest req)
    {
        var w = Make(owner, L.T("Đưa vào Thùng rác", "Move to Recycle Bin"), 640); bool ok = false;
        var e = req.Estimate;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = L.T($"Đưa {req.Paths.Count} mục vào Thùng rác?", $"Move {req.Paths.Count} item(s) to the Recycle Bin?"), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var list = new TextBox
        {
            Text = string.Join("\n", req.Paths.Take(8)) + (req.Paths.Count > 8 ? $"\n… +{req.Paths.Count - 8}" : ""),
            IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12, Margin = new Thickness(0, 0, 0, 12),
        };
        panel.Children.Add(list);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        int r = 0;
        void Row(string label, string value, bool bold = false, Brush? color = null)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 2, 16, 2), Opacity = 0.8 }; Grid.SetRow(l, r);
            var v = new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = color ?? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] };
            Grid.SetRow(v, r); Grid.SetColumn(v, 1); grid.Children.Add(l); grid.Children.Add(v); r++;
        }
        Row(L.T("Gồm", "Contains"), L.T($"{e.Files:N0} file, {e.Dirs:N0} thư mục", $"{e.Files:N0} files, {e.Dirs:N0} folders"));
        Row(L.T("Kích thước", "Size"), $"{Fmt.Size(e.SelectedLogical)}  ({Fmt.Exact(e.SelectedLogical)})");
        Row(L.T("Giải phóng NGAY", "Freed NOW"), $"0 B — {L.T("Thùng rác nằm trên cùng ổ nên dữ liệu vẫn chiếm chỗ cho tới khi dọn Thùng rác.", "the Recycle Bin is on the same drive, so data keeps occupying space until the bin is emptied.")}", true);
        Row(L.T("Sẽ giải phóng SAU KHI dọn Thùng rác", "Freed AFTER emptying the bin"), $"{Fmt.Size(e.FreedAfterEmpty)}  ({Fmt.Exact(e.FreedAfterEmpty)})", true, Brushes.SeaGreen);
        if (e.SharedHardLinkFiles > 0)
            Row(L.T("Lưu ý hard link", "Hard-link note"), L.T($"{e.SharedHardLinkFiles:N0} file có liên kết cứng còn nằm NGOÀI vùng chọn — dữ liệu của chúng không được giải phóng và không tính vào số trên.", $"{e.SharedHardLinkFiles:N0} files have hard links remaining OUTSIDE the selection — their data is not freed and is excluded from the figure above."), false, Brushes.DarkOrange);
        panel.Children.Add(grid);
        panel.Children.Add(new TextBlock { Text = L.T("Có thể khôi phục từ Thùng rác. App không bao giờ xoá vĩnh viễn.", "You can restore from the Recycle Bin. The app never deletes permanently."), Margin = new Thickness(0, 12, 0, 0), Opacity = 0.7, FontSize = 12 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var yes = new Button { Content = L.T("Đưa vào Thùng rác", "Move to Recycle Bin"), Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        var no = new Button { Content = L.T("Huỷ", "Cancel"), Padding = new Thickness(16, 6, 16, 6), IsDefault = true, IsCancel = true };
        yes.Click += (_, _) => { ok = true; w.Close(); }; no.Click += (_, _) => w.Close();
        row.Children.Add(no); row.Children.Add(yes); panel.Children.Add(row);
        w.Content = panel; w.ShowDialog();
        return ok;
    }
}
