using FolderSizePro.Model;
using FolderSizePro.Scan;
using FolderSizePro.Storage;
using FolderSizePro.Watch;

namespace FolderSizePro.App.ViewModels;

public sealed partial class MainViewModel
{
    // ----------------------------------------------------------------- theo dõi trực tiếp (UC-50)
    ChangeWatcher? _watcher;
    readonly Queue<int> _liveQueue = new();
    bool _liveRescan;
    const int LiveRescanMaxFiles = 60_000;

    void StartWatcherIfEnabled()
    {
        StopWatcher();
        if (!Settings.LiveWatch || Result == null || IsScanning || Job == null) return;
        try
        {
            _watcher = new ChangeWatcher(Result.Roots.Select(r => r.Path));
            _watcher.DirectoriesChanged += dirs => Application.Current.Dispatcher.BeginInvoke(() => OnDirsChanged(dirs));
            _watcher.Lost += msg => Application.Current.Dispatcher.BeginInvoke(() => OnWatchLost(msg));
            Log.Info("Bật theo dõi trực tiếp: " + string.Join(" | ", Result.Roots.Select(r => r.Path)));
        }
        catch (Exception ex) { Log.Warn("Không bật được theo dõi: " + ex.Message); }
    }

    void StopWatcher()
    {
        try { _watcher?.Dispose(); } catch { }
        _watcher = null; _liveQueue.Clear();
    }

    void OnWatchLost(string msg)
    {
        StopWatcher();
        Log.Warn("Mất theo dõi trực tiếp: " + msg);
        if (Result == null) return;
        var roots = Result.Roots.Select(r => r.Path).ToList();
        Banners.Insert(0, new BannerItem(L.T("MẤT THEO DÕI: có quá nhiều thay đổi cùng lúc (bộ đệm tràn) nên số liệu hiện tại có thể đã cũ — hãy quét lại.", "LOST TRACKING: too many changes at once (buffer overflow), so current figures may be stale — rescan."), BannerLevel.Warn,
            L.T("Quét lại", "Rescan"), () => { PathText = string.Join(" | ", roots); StartScan(roots); }));     // ER-40
    }

    System.Windows.Threading.DispatcherTimer? _driveTimer;

    public void RefreshDrivesSoon()
    {
        _driveTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _driveTimer.Stop(); _driveTimer.Tick -= DriveTick; _driveTimer.Tick += DriveTick; _driveTimer.Start();
    }

    void DriveTick(object? s, EventArgs e) { _driveTimer!.Stop(); RefreshDrives(); }

    int FindNearestNode(string path)
    {
        string? p = path;
        while (!string.IsNullOrEmpty(p))
        {
            int n = FindNodeByPath(p);
            if (n > 0) return n;
            p = Path.GetDirectoryName(p);
        }
        return 0;
    }

    void OnDirsChanged(IReadOnlyList<string> dirs)
    {
        if (Result == null || Tree == null || Job == null) return;
        var t = Tree; var nodes = new HashSet<int>();
        foreach (var d in dirs) { int n = FindNearestNode(d); if (n > 0 && t[n].IsDir) nodes.Add(n); }
        // bỏ nút có tổ tiên cũng nằm trong tập (quét nhánh trên sẽ bao luôn)
        foreach (var n in nodes.ToList()) { for (int p = t[n].Parent; p > 0; p = t[p].Parent) if (nodes.Contains(p)) { nodes.Remove(n); break; } }
        foreach (var n in nodes) if (!_liveQueue.Contains(n)) _liveQueue.Enqueue(n);
        TryRunLive();
    }

    void TryRunLive()
    {
        if (IsScanning || Result == null || Tree == null) return;
        var t = Tree;
        while (_liveQueue.Count > 0)
        {
            int n = _liveQueue.Dequeue();
            if (n <= 0 || n >= t.Count || t[n].Parent == -2 || !t[n].IsDir) continue;
            if (t[n].FileCount > LiveRescanMaxFiles)
            {
                Interlocked.Or(ref t[n].FlagsRaw, (uint)NodeFlags.Changed);
                Banners.Add(new BannerItem(L.T($"Thư mục lớn đã thay đổi: {t.FullPath(n)} — quá nhiều file để cập nhật tự động. Chọn nó rồi 'Quét lại nhánh'.", $"A large folder changed: {t.FullPath(n)} — too many files to update automatically. Select it and use 'Rescan branch'."), BannerLevel.Info));
                RefreshRows();
                continue;
            }
            _liveRescan = true;
            Job = ScanJob.StartRescan(Result, n);
            IsScanning = true; IsPaused = false;
            StatusText = L.T("Cập nhật trực tiếp…", "Live update…");
            _autosaveAt = DateTime.MaxValue; _rootsBuilt = true;
            _timer.Start();
            return;
        }
    }
}
