using FolderSizePro.Storage;

namespace FolderSizePro.App.Views;

/// <summary>UC-18: cửa sổ cài đặt (dựng bằng code). Đổi ngôn ngữ cần mở lại app (ngôn ngữ chọn một lần lúc khởi động).</summary>
public sealed class SettingsWindow : Window
{
    readonly AppSettings _s;
    readonly ComboBox _lang = new(), _theme = new(), _unit = new(), _mode = new();
    readonly CheckBox _ads = new(), _fast = new(), _hidden = new(), _live = new();
    readonly ListBox _excl = new();
    readonly TextBox _timeout = new(), _mem = new(), _top = new();
    public bool Saved { get; private set; }
    public bool LanguageChanged { get; private set; }

    public SettingsWindow(AppSettings s, Window owner)
    {
        _s = s; Owner = owner; Title = L.T("Cài đặt", "Settings"); Width = 640; SizeToContent = SizeToContent.Height; MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        var sp = new StackPanel { Margin = new Thickness(20) };
        TextBlock H(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        TextBlock Note(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 4) };
        StackPanel Pair(string label, Control c) { var p = new DockPanel { Margin = new Thickness(0, 3, 0, 3) }; var l = new TextBlock { Text = label, Width = 250, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left); p.Children.Add(l); p.Children.Add(c); var wrap = new StackPanel(); wrap.Children.Add(p); return wrap; }

        _lang.Items.Add("Tiếng Việt"); _lang.Items.Add("English"); _lang.SelectedIndex = s.Language == "en" ? 1 : 0;
        _theme.Items.Add(L.T("Theo Windows", "Follow Windows")); _theme.Items.Add(L.T("Sáng", "Light")); _theme.Items.Add(L.T("Tối", "Dark"));
        _theme.SelectedIndex = s.Theme == "light" ? 1 : s.Theme == "dark" ? 2 : 0;
        _unit.Items.Add(L.T("Nhị phân — KiB, MiB, GiB (1024)", "Binary — KiB, MiB, GiB (1024)")); _unit.Items.Add(L.T("SI — kB, MB, GB (1000)", "SI — kB, MB, GB (1000)"));
        _unit.SelectedIndex = s.Unit == UnitSystem.Si ? 1 : 0;
        sp.Children.Add(H(L.T("Hiển thị", "Display")));
        sp.Children.Add(Pair(L.T("Ngôn ngữ (mở lại app để áp dụng)", "Language (restart to apply)"), _lang));
        sp.Children.Add(Pair(L.T("Giao diện", "Theme"), _theme));
        sp.Children.Add(Pair(L.T("Đơn vị", "Units"), _unit));
        _hidden.Content = L.T("Hiện file ẩn / hệ thống trong cây (chỉ ảnh hưởng hiển thị, tổng không đổi)", "Show hidden / system items in the tree (display only, totals unchanged)"); _hidden.IsChecked = s.ShowHiddenSystem;
        sp.Children.Add(_hidden);

        sp.Children.Add(H(L.T("Quét", "Scanning")));
        _mode.Items.Add(L.T("Tự động (MFT khi là Administrator + ổ NTFS + gốc ổ)", "Automatic (MFT when Administrator + NTFS + drive root)")); _mode.Items.Add(L.T("Luôn quét thường (đối chứng)", "Always normal scan (cross-check)")); _mode.Items.Add(L.T("Luôn quét MFT (cần Administrator)", "Always MFT scan (needs Administrator)"));
        _mode.SelectedIndex = s.ScanModePref == "normal" ? 1 : s.ScanModePref == "mft" ? 2 : 0;
        sp.Children.Add(Pair(L.T("Chế độ quét", "Scan mode"), _mode));
        _ads.Content = L.T("Tính Alternate Data Stream (ADS) — cần cho file CompactOS, nên bật", "Count Alternate Data Streams (ADS) — needed for CompactOS files, keep on"); _ads.IsChecked = s.IncludeAds;
        _fast.Content = L.T("Quét nhanh (tin kích thước trong danh sách thư mục, bỏ ADS) — KÉM CHÍNH XÁC", "Fast scan (trust directory-listing sizes, skip ADS) — LESS ACCURATE"); _fast.IsChecked = s.FastMode;
        sp.Children.Add(_ads); sp.Children.Add(_fast);
        sp.Children.Add(Note(L.T("Danh sách thư mục có thể báo kích thước CŨ với hard link và 0 với file CompactOS. Mọi kết quả quét nhanh luôn mang nhãn “KÉM CHÍNH XÁC”.", "Directory listings can report STALE sizes for hard links and 0 for CompactOS files. Every fast-scan result carries a “LESS ACCURATE” label.")));
        _timeout.Text = s.NetworkTimeoutSec.ToString(); _timeout.Width = 80;
        _mem.Text = s.MemoryLimitMb.ToString(); _mem.Width = 80;
        _top.Text = s.TopN.ToString(); _top.Width = 80;
        sp.Children.Add(Pair(L.T("Thời gian chờ ổ mạng (giây)", "Network timeout (seconds)"), _timeout));
        sp.Children.Add(Pair(L.T("Ngưỡng bộ nhớ cây kết quả (MB)", "Result-tree memory limit (MB)"), _mem));
        sp.Children.Add(Note(L.T("Vượt ngưỡng → Chế độ gọn (giữ file lớn nhất mỗi thư mục, tổng vẫn đúng).", "Exceeded → Compact mode (keep the largest files per folder, totals stay exact).")));
        sp.Children.Add(Pair(L.T("Số mục “Top lớn nhất”", "“Largest” list size"), _top));
        _live.Content = L.T("Theo dõi trực tiếp thay đổi sau khi quét (cập nhật cây tự động)", "Live watch after scanning (update the tree automatically)"); _live.IsChecked = s.LiveWatch;
        sp.Children.Add(_live);

        sp.Children.Add(H(L.T("Thư mục loại trừ khỏi quét", "Excluded folders")));
        _excl.Height = 90; foreach (var p in s.ExcludedPaths) _excl.Items.Add(p);
        sp.Children.Add(_excl);
        var eb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var add = new Button { Content = L.T("Thêm…", "Add…"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        add.Click += (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog(this) == true && !_excl.Items.Contains(d.FolderName)) _excl.Items.Add(d.FolderName); };
        var rem = new Button { Content = L.T("Bỏ", "Remove"), Padding = new Thickness(12, 4, 12, 4) };
        rem.Click += (_, _) => { if (_excl.SelectedItem != null) _excl.Items.Remove(_excl.SelectedItem); };
        eb.Children.Add(add); eb.Children.Add(rem); sp.Children.Add(eb);

        sp.Children.Add(H(L.T("Dữ liệu của app", "App data")));
        sp.Children.Add(Note(L.T($"Cài đặt, phiên, log nằm ở {AppPaths.Local} (không bao giờ ghi vào ổ đang đo). App không gửi bất kỳ dữ liệu nào ra mạng.", $"Settings, session and logs live in {AppPaths.Local} (never written to the drive being measured). The app sends no data over the network.")));
        var lb = new StackPanel { Orientation = Orientation.Horizontal };
        var openLog = new Button { Content = L.T("Mở thư mục log", "Open log folder"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        openLog.Click += (_, _) => { try { Directory.CreateDirectory(AppPaths.Logs); System.Diagnostics.Process.Start("explorer.exe", AppPaths.Logs); } catch { } };
        var bundle = new Button { Content = L.T("Gom log thành 1 file…", "Bundle logs into one file…"), Padding = new Thickness(12, 4, 12, 4) };
        bundle.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.SaveFileDialog { FileName = "folder-size-pro-log.txt", Filter = "Text (*.txt)|*.txt" };
            if (d.ShowDialog(this) == true) { var r = Log.Bundle(d.FileName); Dialogs.Info(this, L.T("Gom log", "Bundle logs"), r != null ? L.T("Đã lưu: ", "Saved: ") + r + L.T("\nBạn tự quyết định có gửi file này hay không.", "\nYou decide whether to send it.") : L.T("Không ghi được file.", "Cannot write the file.")); }
        };
        lb.Children.Add(openLog); lb.Children.Add(bundle); sp.Children.Add(lb);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var ok = new Button { Content = L.T("Lưu", "Save"), Padding = new Thickness(22, 6, 22, 6), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        ok.Style = (Style?)Application.Current.TryFindResource("AccentButtonStyle");
        var cancel = new Button { Content = L.T("Huỷ", "Cancel"), Padding = new Thickness(18, 6, 18, 6), IsCancel = true };
        ok.Click += (_, _) => { Apply(); Saved = true; Close(); }; cancel.Click += (_, _) => Close();
        row.Children.Add(cancel); row.Children.Add(ok); sp.Children.Add(row);
        Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    void Apply()
    {
        string newLang = _lang.SelectedIndex == 1 ? "en" : "vi";
        LanguageChanged = newLang != _s.Language; _s.Language = newLang;
        _s.Theme = _theme.SelectedIndex == 1 ? "light" : _theme.SelectedIndex == 2 ? "dark" : "system";
        _s.Unit = _unit.SelectedIndex == 1 ? UnitSystem.Si : UnitSystem.Binary;
        _s.ScanModePref = _mode.SelectedIndex == 1 ? "normal" : _mode.SelectedIndex == 2 ? "mft" : "auto";
        _s.ShowHiddenSystem = _hidden.IsChecked == true; _s.IncludeAds = _ads.IsChecked == true; _s.FastMode = _fast.IsChecked == true; _s.LiveWatch = _live.IsChecked == true;
        if (int.TryParse(_timeout.Text, out var t) && t is >= 3 and <= 600) _s.NetworkTimeoutSec = t;
        if (int.TryParse(_mem.Text, out var m) && m is >= 256 and <= 65536) _s.MemoryLimitMb = m;
        if (int.TryParse(_top.Text, out var n) && n is >= 10 and <= 1000) _s.TopN = n;
        _s.ExcludedPaths = _excl.Items.Cast<string>().ToList();
    }
}
