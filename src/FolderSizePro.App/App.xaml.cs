using System.Diagnostics;
using FolderSizePro.App.ViewModels;
using FolderSizePro.App.Views;
using FolderSizePro.Storage;

namespace FolderSizePro.App;

public partial class App : Application
{
    MainViewModel? _vm;
    MainWindow? _window;

    public static void ApplyTheme(string theme)
    {
        Current.ThemeMode = theme switch { "light" => ThemeMode.Light, "dark" => ThemeMode.Dark, _ => ThemeMode.System };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.Probe();                                      // ER-42
        var settings = SettingsStore.Load();                   // ER-41
        var args = ParseArgs(e.Args);
        if (args.Lang != null) settings.Language = args.Lang;
        L.Use(settings.Language);
        ApplyTheme(args.Theme ?? settings.Theme);
        DispatcherUnhandledException += (_, ev) => { Log.Error("Lỗi giao diện chưa bắt", ev.Exception); Dialogs.Info(_window, L.T("Có lỗi", "Error"), ev.Exception.Message + "\n\n" + L.T("Chi tiết đã ghi vào log.", "Details were written to the log.")); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Error("Lỗi chưa bắt", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Error("Lỗi tác vụ nền", ev.Exception); ev.SetObserved(); };

        _vm = new MainViewModel(settings);
        _window = new MainWindow(_vm);
        if (args.Size is { } sz) { _window.Width = sz.W; _window.Height = sz.H; _window.WindowStartupLocation = WindowStartupLocation.Manual; _window.Left = 20; _window.Top = 20; }
        MainWindow = _window;
        _window.Show();

        if (args.SelfTest != null) { _ = SelfTest.Run(_vm, args.SelfTest, Environment.GetEnvironmentVariable("FSP_SELFTEST_ROOT") ?? @"D:\GitHub\folder-size-pro\_fixtures"); return; }
        if (args.Scan.Count == 0 && args.Open == null && args.Screenshot == null) CheckInterruptedSession();

        if (args.Open != null) _vm.OpenSnapshot(args.Open);
        else if (args.Scan.Count > 0)
        {
            _vm.PathText = string.Join(" | ", args.Scan);
            _vm.StartScan(args.Scan);
            if (args.Select != null) _vm.SelectWhenReady = args.Select;
        }
        if (args.Screenshot != null) RunScreenshot(args);
    }

    void CheckInterruptedSession()
    {
        var s = SessionStore.Check();
        if (s == null) return;
        int c = Dialogs.Choose(_window, L.T("Phiên quét bị gián đoạn", "Interrupted scan"),
            L.T($"Lần chạy trước thoát đột ngột khi đang quét:\n{string.Join("\n", s.Roots)}\n(bắt đầu {s.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm})\n\n{(s.HasPartial ? "Có kết quả dở dang đã tự lưu. Khôi phục để xem?" : "Chưa có kết quả nào được tự lưu.")}",
                $"The previous run exited unexpectedly while scanning:\n{string.Join("\n", s.Roots)}\n(started {s.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm})\n\n{(s.HasPartial ? "A partial result was auto-saved. Restore it?" : "No result had been auto-saved.")}"),
            s.HasPartial ? L.T("Khôi phục kết quả dở dang", "Restore partial result") : L.T("Quét lại", "Rescan"), L.T("Bỏ qua", "Discard"));
        if (c == 0)
        {
            if (s.HasPartial) { var r = SessionStore.LoadPartial(); if (r != null) { _vm!.ShowRecovered(r, s.Roots); SessionStore.Clear(); return; } }
            SessionStore.Clear(); _vm!.PathText = string.Join(" | ", s.Roots); _vm.StartScan(s.Roots);
        }
        else SessionStore.Clear();
    }

    // ---------------------------------------------------------------- dev: chụp ảnh cửa sổ
    void RunScreenshot(Args a)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (_vm!.IsScanning && ticks < 600) return;
            timer.Stop();
            _window!.PrepareForScreenshot(a);
            Window? shot = _window;
            if (a.Tab == "settings") { var sw = new SettingsWindow(_vm.Settings, _window) { WindowStartupLocation = WindowStartupLocation.Manual, Left = 40, Top = 40 }; sw.Show(); sw.UpdateLayout(); shot = sw; }
            Dispatcher.BeginInvoke(() =>
            {
                try { Views.ScreenshotUtil.Save(shot!, a.Screenshot!); } catch (Exception ex) { File.WriteAllText(a.Screenshot + ".err.txt", ex.ToString()); }
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
        timer.Start();
    }

    public sealed class Args
    {
        public List<string> Scan = new(); public string? Open, Select, Screenshot, Lang, Theme, Tab, Expand, SelfTest; public (double W, double H)? Size;
    }

    static Args ParseArgs(string[] a)
    {
        var r = new Args();
        for (int i = 0; i < a.Length; i++)
        {
            string Next() => i + 1 < a.Length ? a[++i] : "";
            switch (a[i])
            {
                case "--scan": r.Scan.Add(Next()); break;
                case "--open": r.Open = Next(); break;
                case "--select": r.Select = Next(); break;
                case "--screenshot": r.Screenshot = Next(); break;
                case "--lang": r.Lang = Next(); break;
                case "--theme": r.Theme = Next(); break;
                case "--tab": r.Tab = Next(); break;
                case "--expand": r.Expand = Next(); break;
                case "--selftest": r.SelfTest = Next(); break;
                case "--size": { var p = Next().Split('x'); if (p.Length == 2 && double.TryParse(p[0], out var w) && double.TryParse(p[1], out var h)) r.Size = (w, h); break; }
                default:
                    if (a[i].EndsWith(".fsp", StringComparison.OrdinalIgnoreCase) && File.Exists(a[i])) r.Open = a[i];
                    else if (!a[i].StartsWith("--")) r.Scan.Add(a[i]);
                    break;
            }
        }
        return r;
    }
}
