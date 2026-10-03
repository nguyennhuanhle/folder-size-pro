using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;
using FolderSizePro.Safety;
using FolderSizePro.Scan;
using FolderSizePro.Storage;

namespace FolderSizePro.App.ViewModels;

public sealed partial class MainViewModel : Observable
{
    public AppSettings Settings { get; }
    public IShell Shell { get; set; } = null!;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public ObservableCollection<DriveItem> Drives { get; } = new();
    public ObservableCollection<string> RecentPaths { get; } = new();
    public ObservableCollection<BannerItem> Banners { get; } = new();
    public ObservableCollection<FileRow> TopFiles { get; } = new();
    public ObservableCollection<DirRow> TopDirs { get; } = new();
    public ObservableCollection<GroupRow> Groups { get; } = new();
    public ObservableCollection<ExtRow> Extensions { get; } = new();
    public ObservableCollection<IssueRow> Issues { get; } = new();
    public ObservableCollection<ReconcileRowVm> ReconcileRows { get; } = new();
    public ObservableCollection<ConsistencyRowVm> ConsistencyRows { get; } = new();

    public IScanJob? Job { get; private set; }
    public ScanResult? Result { get; private set; }
    public ScanTree? Tree => Result?.Tree;
    public bool IsAdmin { get; } = Reconciler.IsAdmin();

    // ----------------------------------------------------------------- trạng thái
    string _pathText = "";
    public string PathText { get => _pathText; set { if (Set(ref _pathText, value)) PathError = null; } }
    string? _pathError;
    public string? PathError { get => _pathError; set { if (Set(ref _pathError, value)) Raise(nameof(HasPathError)); } }
    public bool HasPathError => !string.IsNullOrEmpty(_pathError);

    bool _isScanning, _isPaused, _hasResult;
    public bool IsScanning { get => _isScanning; private set { if (Set(ref _isScanning, value)) { Raise(nameof(CanStart)); Raise(nameof(IsIdle)); CommandsChanged(); } } }
    public bool IsPaused { get => _isPaused; private set { if (Set(ref _isPaused, value)) { Raise(nameof(PauseResumeText)); } } }
    public bool HasResult { get => _hasResult; private set { if (Set(ref _hasResult, value)) { Raise(nameof(IsEmptyState)); CommandsChanged(); } } }
    public bool CanStart => true;
    public bool IsIdle => !IsScanning;
    public bool IsEmptyState => !HasResult && !IsScanning;
    public string PauseResumeText => IsPaused ? L.T("Tiếp tục", "Resume") : L.T("Tạm dừng", "Pause");

    string _statusText = "";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    string _progressText = "";
    public string ProgressText { get => _progressText; set => Set(ref _progressText, value); }
    string _scanTimeText = "";
    public string ScanTimeText { get => _scanTimeText; set => Set(ref _scanTimeText, value); }
    string _modeText = "";
    public string ModeText { get => _modeText; set => Set(ref _modeText, value); }
    string _totalsText = "";
    public string TotalsText { get => _totalsText; set => Set(ref _totalsText, value); }
    string _currentPathText = "";
    public string CurrentPathText { get => _currentPathText; set => Set(ref _currentPathText, value); }
    bool _approximate;
    public bool IsApproximate { get => _approximate; private set => Set(ref _approximate, value); }

    TreeRow? _selectedRow;
    public TreeRow? SelectedRow
    {
        get => _selectedRow;
        set { if (Set(ref _selectedRow, value)) { _ = LoadDetailsAsync(); CommandsChanged(); } }
    }

    public RelayCommand StartCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RescanBranchCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ElevateCommand { get; }
    public RelayCommand OpenInExplorerCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand PropertiesCommand { get; }
    public RelayCommand SaveSnapshotCommand { get; }
    public RelayCommand OpenSnapshotCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand ScanDriveCommand { get; }
    public RelayCommand FilterSameExtensionCommand { get; }

    void CommandsChanged()
    {
        foreach (var c in new[] { StartCommand, PauseResumeCommand, StopCommand, RescanBranchCommand, DeleteCommand, OpenInExplorerCommand, CopyPathCommand, PropertiesCommand, SaveSnapshotCommand, ExportCommand, FilterSameExtensionCommand })
            c.RaiseCanExecuteChanged();
    }

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        Fmt.Unit = settings.Unit;
        foreach (var p in settings.RecentPaths) RecentPaths.Add(p);
        _timer.Tick += (_, _) => OnTick();

        StartCommand = new RelayCommand(() => StartScan(SplitPaths(PathText)), () => true);
        PauseResumeCommand = new RelayCommand(() => { if (Job == null) return; if (Job.IsPaused) { Job.Resume(); IsPaused = false; } else { Job.Pause(); IsPaused = true; } }, () => IsScanning);
        StopCommand = new RelayCommand(() => Job?.Cancel(), () => IsScanning);
        BrowseCommand = new RelayCommand(() => { var p = Shell.PickFolder(); if (p != null) { PathText = p; StartScan(new[] { p }); } });
        RescanBranchCommand = new RelayCommand(RescanSelected, () => !IsScanning && SelectedRow is { IsDir: true } && Result != null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => !IsScanning && Result != null && SelectedRow is { IsMore: false });
        ElevateCommand = new RelayCommand(() => Elevate(), () => !IsAdmin);
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, () => SelectedPath() != null);
        CopyPathCommand = new RelayCommand(() => { var p = SelectedPath(); if (p != null) Shell.CopyText(p); }, () => SelectedPath() != null);
        PropertiesCommand = new RelayCommand(() => { var p = SelectedPath(); if (p != null) ShellOps.ShowProperties(p); }, () => SelectedPath() != null);
        SaveSnapshotCommand = new RelayCommand(SaveSnapshot, () => Result != null && !IsScanning);
        OpenSnapshotCommand = new RelayCommand(() => { var f = Shell.PickOpenFsp(); if (f != null) OpenSnapshot(f); });
        ExportCommand = new RelayCommand(p => Export(p as string ?? "json"), _ => Result != null && !IsScanning);
        RefreshDrivesCommand = new RelayCommand(RefreshDrives);
        ScanDriveCommand = new RelayCommand(p => { if (p is DriveItem d) { PathText = d.V.RootPath.TrimEnd('\\') + "\\"; StartScan(new[] { PathText }); } });
        FilterSameExtensionCommand = new RelayCommand(FilterSameExtension, () => SelectedRow is { IsDir: false, IsMore: false });
        RefreshDrives();
        if (SettingsStore.LastWarning != null) Banners.Add(new BannerItem(SettingsStore.LastWarning, BannerLevel.Warn));
        if (!AppPaths.Writable) Banners.Add(new BannerItem(L.T("Không ghi được thư mục dữ liệu của app (", "Cannot write the app data folder (") + AppPaths.WritableError + L.T("). App chạy ở chế độ chỉ-bộ-nhớ: không lưu cài đặt, log, phiên.", "). Running in memory-only mode: settings, logs and session are not saved."), BannerLevel.Warn));
    }

    static List<string> SplitPaths(string text) =>
        text.Split(new[] { '|', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    public void RefreshDrives()
    {
        Drives.Clear();
        foreach (var v in VolumeInfo.ListAll()) Drives.Add(new DriveItem(v));
    }

    // ----------------------------------------------------------------- quét
    public bool RequestMft { get; set; }

    public void StartScan(IReadOnlyList<string> inputs, ScanMode? forceMode = null)
    {
        if (inputs.Count == 0) { PathError = L.T("Đường dẫn trống. Hãy nhập hoặc chọn một thư mục / ổ đĩa.", "Path is empty. Enter or pick a folder / drive."); return; }
        var roots = new List<string>();
        foreach (var p in inputs)
        {
            var chk = PathValidator.Validate(p);
            if (!chk.Ok) { PathError = chk.Error; return; }                            // ER-01
            roots.Add(chk.Path);
        }
        PathError = null;
        if (IsScanning)                                                                // KT-07
        {
            int c = Shell.Choose(L.T("Đang có phiên quét", "A scan is running"),
                L.T("Một phiên quét đang chạy. Quét song song các gốc chồng nhau sẽ đếm trùng và tranh I/O.", "A scan is already running. Running scans with overlapping roots would double-count and compete for I/O."),
                L.T("Huỷ phiên cũ, quét mới", "Cancel the old scan, start new"), L.T("Chờ phiên cũ xong", "Wait for it to finish"));
            if (c != 0) return;
            Job?.Cancel(); try { Job?.Completion.Wait(5000); } catch { }
            OnCompleted();
        }
        var opt = Settings.ToScanOptions();
        opt.Mode = forceMode ?? (Settings.ScanModePref == "normal" ? ScanMode.Normal : Settings.ScanModePref == "mft" ? ScanMode.Mft : ScanMode.Auto);
        var plan = ScanPlanner.Plan(roots, opt);
        if (plan.ExitCode4Reason != null)
        {
            int c = Shell.Choose(L.T("Cần quyền Administrator", "Administrator required"), plan.ExitCode4Reason,
                L.T("Chạy với quyền Administrator", "Run as Administrator"), L.T("Quét thường", "Normal scan"));
            if (c == 0) { Elevate(roots); return; }
            if (c != 1) return;
            opt.Mode = ScanMode.Normal; plan = ScanPlanner.Plan(roots, opt);
        }
        RememberPath(roots);
        ClearViews();
        Job = ScanPlanner.Start(roots, opt, plan);
        Result = Job.Result;
        IsApproximate = opt.Approximate;
        IsScanning = true; IsPaused = false; HasResult = false;
        StatusText = L.T("Đang quét…", "Scanning…");
        ModeText = plan.Mode == ScanMode.Mft ? L.T("Chế độ MFT (nhanh)", "MFT mode (fast)") : opt.FastMode ? L.T("Quét nhanh — kém chính xác", "Fast scan — less accurate") : L.T("Quét chính xác (từng file)", "Exact scan (per file)");
        Log.Info($"Bắt đầu quét: {string.Join(" | ", roots)} · mode={plan.Mode} · fast={opt.FastMode} · ads={opt.IncludeAds}");
        SessionStore.MarkScanning(roots);
        _autosaveAt = DateTime.UtcNow.AddSeconds(60);
        _timer.Start();
    }

    DateTime _autosaveAt;
    bool _rootsBuilt;

    void OnTick()
    {
        if (Job == null) { _timer.Stop(); return; }
        var p = Job.Snapshot();
        ProgressText = $"{Fmt.Count(p.Files)} {L.T("file", "files")} · {Fmt.Count(p.Dirs)} {L.T("thư mục", "folders")} · {Fmt.Size(p.Allocated)} {L.T("trên đĩa", "on disk")} · {Fmt.Count((long)p.FilesPerSec)} {L.T("file/giây", "files/s")} · {p.Elapsed:mm\\:ss}";
        CurrentPathText = p.Paused ? L.T("Đã tạm dừng", "Paused") : p.CurrentPath;
        if (p.Issues > 0) StatusText = $"{L.T("Đang quét…", "Scanning…")} {Fmt.Count(p.Issues)} {L.T("mục có vấn đề", "issues")}";
        else StatusText = p.Finishing ? L.T("Đang chốt số liệu…", "Finalizing…") : p.Paused ? L.T("Đã tạm dừng", "Paused") : L.T("Đang quét…", "Scanning…");
        if (p.CompactMode && !_compactBanner) { _compactBanner = true; Banners.Add(new BannerItem(L.T("Vượt ngưỡng bộ nhớ: chuyển Chế độ gọn — từ giờ chỉ giữ file lớn nhất mỗi thư mục, các file nhỏ được gộp thành một dòng; tổng vẫn đúng.", "Memory limit exceeded: switched to Compact mode — from now on only the largest files per folder are kept, the rest are grouped; totals stay exact."), BannerLevel.Warn)); }
        if (Result != null && Result.Roots.Count > 0 && !_rootsBuilt) { _rootsBuilt = true; BuildRootRows(); }
        RefreshRows();
        if (DateTime.UtcNow >= _autosaveAt && !Job.Completion.IsCompleted) { _autosaveAt = DateTime.UtcNow.AddSeconds(60); SessionStore.Autosave(Result!, Job); }
        if (Job.Completion.IsCompleted) OnCompleted();
    }

    bool _compactBanner;

    void OnCompleted()
    {
        _timer.Stop();
        var job = Job; if (job == null) return;
        try { job.Completion.Wait(); } catch { }
        IsScanning = false; IsPaused = false;
        SessionStore.Clear();
        var r = job.Result;
        bool wasLive = _liveRescan; _liveRescan = false;
        Result = r; HasResult = true; _rootsBuilt = false;
        FillAfterScan(rescanSummary: wasLive ? L.T("Đã cập nhật trực tiếp lúc ", "Live-updated at ") + DateTime.Now.ToString("HH:mm:ss") : null);
        if (!wasLive && job is not null) StartWatcherIfEnabled();
        TryRunLive();
        if (_crossPending) { _crossPending = false; _cmpB = r; CompareBText = L.T("Kết quả vừa quét", "Just-scanned result"); Raise(nameof(CompareBText)); Shell.SelectTab("compare"); _ = RunCompareAsync(); }
        if (SelectWhenReady != null) { int n = FindNodeByPath(SelectWhenReady); SelectWhenReady = null; if (n > 0) SelectNode(n, switchTab: false); }
        Log.Info($"Quét xong: {Fmt.Count(r.Tree[0].FileCount)} file, {Fmt.Size(r.Tree[0].Allocated)} trên đĩa, {r.Duration.TotalSeconds:N1}s, {r.Completeness}, issues={r.Issues.Count}");
    }

    void ClearViews()
    {
        StopWatcher(); _liveRescan = false;
        Rows.Clear(); _expanded.Clear(); _rootsBuilt = false; _compactBanner = false;
        TopFiles.Clear(); TopDirs.Clear(); Groups.Clear(); Extensions.Clear(); Issues.Clear(); ReconcileRows.Clear(); ConsistencyRows.Clear();
        SearchResults.Clear(); SearchSummary = ""; DiffRows.Clear(); DiffSummary = "";
        Banners.Clear(); SelectedRow = null; Details = null;
        TotalsText = ""; ProgressText = ""; ScanTimeText = "";
        BumpViews();
    }

    /// <summary>Điền mọi khung nhìn từ kết quả (sau quét / sau mở snapshot / sau quét lại nhánh / sau xoá).</summary>
    void FillAfterScan(string? rescanSummary)
    {
        var r = Result!; var t = r.Tree;
        Banners.Clear();
        RebuildRows();
        FillLists();
        long files = t[0].FileCount, dirs = t[0].DirCount;
        TotalsText = $"{Fmt.Size(t[0].Allocated)} {L.T("trên đĩa", "on disk")} · {Fmt.Size(t[0].Logical)} {L.T("kích thước", "size")} · {Fmt.Count(files)} {L.T("file", "files")} · {Fmt.Count(dirs)} {L.T("thư mục", "folders")}";
        ScanTimeText = $"{L.T("Quét lúc", "Scanned at")} {r.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({r.Duration.TotalSeconds:N1}s)";
        ModeText = (r.ModeUsed == "Mft" ? L.T("Chế độ MFT (nhanh)", "MFT mode (fast)") : r.Options.Approximate ? L.T("Quét nhanh — kém chính xác", "Fast scan — less accurate") : L.T("Quét chính xác (từng file)", "Exact scan (per file)"));
        IsApproximate = r.Options.Approximate;
        StatusText = r.Completeness == Completeness.Complete ? L.T("Hoàn tất", "Done") : L.T("Dở dang — chưa hoàn tất", "Partial — incomplete");
        ProgressText = rescanSummary ?? "";
        CurrentPathText = "";
        BuildBanners();
        BumpViews();
        CommandsChanged();
    }

    void FillLists()
    {
        var r = Result!; var t = r.Tree;
        TopFiles.Clear(); TopDirs.Clear(); Groups.Clear(); Extensions.Clear(); Issues.Clear(); ReconcileRows.Clear(); ConsistencyRows.Clear();
        foreach (var f in r.TopFiles.Take(Settings.TopN))
            TopFiles.Add(new FileRow(f.Node, t.NameString(f.Node), t.FullPath(f.Node), Fmt.Size(f.Logical), Fmt.Size(f.Allocated), Fmt.Date(t[f.Node].MTime), Fmt.Badges(t[f.Node])));
        long maxDir = r.TopDirs.Count > 0 ? r.TopDirs[0].DirectAllocated : 1;
        foreach (var d in r.TopDirs.Take(Settings.TopN))
            TopDirs.Add(new DirRow(d.Node, t.FullPath(d.Node), Fmt.Size(d.DirectFiles), Fmt.Size(d.DirectAllocated), maxDir > 0 ? (double)d.DirectAllocated / maxDir : 0));
        long totalA = Math.Max(1, r.TypeStats.Sum(x => x.Allocated));
        foreach (var g in r.TypeStats.GroupBy(x => x.Group).Select(g => (g.Key, c: g.Sum(x => x.Count), l: g.Sum(x => x.Logical), a: g.Sum(x => x.Allocated))).OrderByDescending(x => x.a))
            Groups.Add(new GroupRow(g.Key, Palette.Label(g.Key), g.c, Fmt.Size(g.l), Fmt.Size(g.a), (double)g.a / totalA, Palette.BrushOf(g.Key)));
        foreach (var e in r.TypeStats.Take(300))
            Extensions.Add(new ExtRow(e.Extension == "" ? L.T("(không đuôi)", "(none)") : "." + e.Extension, Palette.Label(e.Group), e.Count, Fmt.Size(e.Logical), Fmt.Size(e.Allocated), (double)e.Allocated / totalA));
        foreach (var i in r.Issues.OrderBy(i => i.Kind).ThenBy(i => i.Path))
            Issues.Add(new IssueRow(KindText(i.Kind), i.Path, i.Message, i.Kind is IssueKind.AccessDenied or IssueKind.VolumeLost or IssueKind.NetworkTimeout or IssueKind.Other ? 2 : i.Kind == IssueKind.Changed ? 1 : 0));
        foreach (var rc in r.Reconcile) FillReconcile(rc);
        foreach (var c in r.Consistency) ConsistencyRows.Add(new ConsistencyRowVm(c.Name, $"{L.T("kỳ vọng", "expected")} {c.Expected} · {L.T("thực tế", "actual")} {c.Actual}", c.Ok));
    }

    void FillReconcile(ReconcileResult rc)
    {
        ReconcileRows.Add(new ReconcileRowVm($"{L.T("Ổ", "Volume")} {rc.Volume.RootPath} — {rc.Volume.FileSystem}, {L.T("cluster", "cluster")} {Fmt.Count(rc.Volume.ClusterSize)} B", "", L.T("đo lúc ", "measured ") + rc.MeasuredUtc.ToLocalTime().ToString("HH:mm:ss"), 3));
        ReconcileRows.Add(new ReconcileRowVm(L.T("Used của ổ (Tổng − Trống)", "Volume used (Total − Free)"), Fmt.Size(rc.Used), Fmt.Exact(rc.Used), 4));
        foreach (var row in rc.Rows)
            ReconcileRows.Add(new ReconcileRowVm("   " + row.Label,
                row.Bytes is long b ? Fmt.Size(b) : row.Status == RowStatus.Inaccessible ? L.T("không truy cập được", "inaccessible") : L.T("không xác định", "unknown"),
                row.Detail, row.Status == RowStatus.Measured ? 0 : 1));
        ReconcileRows.Add(new ReconcileRowVm(L.T("Tổng đã đo trong cây", "Measured in tree"), Fmt.Size(rc.TreeAllocated), Fmt.Exact(rc.TreeAllocated), 4));
        ReconcileRows.Add(new ReconcileRowVm(L.T("CHƯA GIẢI THÍCH = Used − đã đo", "UNEXPLAINED = Used − measured"), Fmt.Size(rc.Unexplained),
            $"{ByteFormatter.Percent(rc.UnexplainedFraction)} {L.T("của Used", "of Used")} · {Fmt.Exact(rc.Unexplained)}", rc.Warn ? 2 : 4));
        foreach (var h in rc.Hints) ReconcileRows.Add(new ReconcileRowVm("→ " + h, "", "", 1));
    }

    static string KindText(IssueKind k) => k switch
    {
        IssueKind.AccessDenied => L.T("Không truy cập được", "Access denied"),
        IssueKind.Changed => L.T("Thay đổi khi quét", "Changed during scan"),
        IssueKind.VolumeLost => L.T("Mất kết nối", "Connection lost"),
        IssueKind.NetworkTimeout => L.T("Hết thời gian chờ", "Timed out"),
        IssueKind.EstimatedFromListing => L.T("Ước lượng từ danh sách", "Estimated from listing"),
        IssueKind.Excluded => L.T("Đã loại trừ", "Excluded"),
        IssueKind.CloudNotListed => L.T("Đám mây chưa nạp", "Cloud not listed"),
        _ => L.T("Lỗi khác", "Other"),
    };

    void BuildBanners()
    {
        var r = Result!;
        if (r.Completeness == Completeness.Partial)
            Banners.Add(new BannerItem(L.T("Kết quả CHƯA HOÀN TẤT (dở dang): có nhánh chưa quét hoặc bị mất kết nối — tổng chỉ gồm phần đã đo.", "Result is INCOMPLETE (partial): some branches were not scanned or lost — totals only include what was measured."), BannerLevel.Warn));
        if (r.Options.Approximate)
            Banners.Add(new BannerItem(L.T("Quét nhanh — KÉM CHÍNH XÁC: dùng kích thước trong danh sách thư mục (có thể cũ với hard link / file nén CompactOS) và bỏ ADS.", "Fast scan — LESS ACCURATE: uses directory-listing sizes (can be stale for hard links / CompactOS files) and skips ADS."), BannerLevel.Warn));
        if (r.CompactModeUsed)
            Banners.Add(new BannerItem(L.T("Chế độ gọn: một số thư mục chỉ hiện file lớn nhất, phần còn lại gộp thành dòng '<N file khác>'. Tổng vẫn đúng.", "Compact mode: some folders show only their largest files, the rest grouped as '<N more files>'. Totals remain exact."), BannerLevel.Info));
        int denied = r.Issues.Count(i => i.Kind == IssueKind.AccessDenied);
        if (denied > 0)
            Banners.Add(new BannerItem(L.T($"{denied:N0} mục không truy cập được — kích thước không xác định, không bị đoán.", $"{denied:N0} items could not be accessed — size unknown, not guessed."), BannerLevel.Warn,
                IsAdmin ? null : L.T("Chạy với quyền Administrator", "Run as Administrator"), IsAdmin ? null : () => Elevate()));
        int changed = r.Issues.Count(i => i.Kind == IssueKind.Changed);
        if (changed > 0) Banners.Add(new BannerItem(L.T($"{changed:N0} mục thay đổi/biến mất trong lúc quét — số liệu là ảnh chụp tại thời điểm quét. Chọn thư mục rồi 'Quét lại nhánh' nếu cần.", $"{changed:N0} items changed/disappeared during the scan — figures are a snapshot. Select a folder and use 'Rescan branch' if needed."), BannerLevel.Info));
        int cloud = r.Issues.Count(i => i.Kind == IssueKind.CloudNotListed);
        if (cloud > 0) Banners.Add(new BannerItem(L.T($"{cloud:N0} thư mục đám mây chưa nạp danh sách — không liệt kê để tránh kích hoạt tải (có thể trông như rỗng).", $"{cloud:N0} cloud folders have no downloaded listing — not listed to avoid triggering a download (may look empty)."), BannerLevel.Info));
        foreach (var rc in r.Reconcile.Where(x => x.Warn))
            Banners.Add(new BannerItem(L.T($"Đối chiếu ổ {rc.Volume.RootPath}: chưa giải thích được {Fmt.Size(rc.Unexplained)} ({ByteFormatter.Percent(rc.UnexplainedFraction)} của Used) — vượt 1%. Xem tab 'Đối chiếu ổ'.", $"Reconciliation of {rc.Volume.RootPath}: {Fmt.Size(rc.Unexplained)} unexplained ({ByteFormatter.Percent(rc.UnexplainedFraction)} of Used) — over 1%. See the 'Reconcile' tab."), BannerLevel.Warn));
        foreach (var n in r.Notes.Where(n => n.Contains("MFT") || n.Contains("Quét lại nhánh") && n.Contains("huỷ")))
            Banners.Add(new BannerItem(n, BannerLevel.Info));
        if (SettingsStore.LastWarning != null) Banners.Add(new BannerItem(SettingsStore.LastWarning, BannerLevel.Warn));
    }

    void RememberPath(IEnumerable<string> roots)
    {
        string joined = string.Join(" | ", roots);
        var keep = joined;
        RecentPaths.Remove(joined); RecentPaths.Insert(0, joined);
        while (RecentPaths.Count > 12) RecentPaths.RemoveAt(RecentPaths.Count - 1);
        Settings.RecentPaths = RecentPaths.ToList();
        SettingsStore.Save(Settings);
        Application.Current.Dispatcher.BeginInvoke(() => { if (PathText != keep) PathText = keep; });      // ComboBox xoá chữ khi danh sách thay đổi
    }

    // ----------------------------------------------------------------- UAC (UC-20)
    public void Elevate(IEnumerable<string>? roots = null)
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
        var args = new List<string>();
        var rs = roots?.ToList() ?? (Result?.Roots.Select(x => x.Path).ToList()) ?? SplitPaths(PathText);
        foreach (var r in rs) { args.Add("--scan"); args.Add($"\"{r}\""); }
        var sel = SelectedPath(); if (sel != null) { args.Add("--select"); args.Add($"\"{sel}\""); }
        var res = UacRelaunch.Run(exe, string.Join(' ', args), out var err);
        switch (res)
        {
            case RelaunchResult.Started: Log.Info("Khởi chạy lại với quyền Administrator"); Application.Current.Shutdown(); break;
            case RelaunchResult.Cancelled: Shell.Info(L.T("Chưa nâng quyền", "Not elevated"), L.T("Bạn đã từ chối hộp thoại UAC — phiên hiện tại vẫn chạy ở quyền thường.", "You declined the UAC prompt — the current session keeps running without elevation.")); break;   // ER-14
            default: Shell.Info(L.T("Không nâng quyền được", "Cannot elevate"), err ?? ""); break;
        }
    }

    public string? SelectedPath() => SelectedRow is { IsMore: false } r && Tree != null && r.Node > 0 ? Tree.FullPath(r.Node) : null;

    void OpenInExplorer()
    {
        var p = SelectedPath(); if (p == null) return;
        try
        {
            if (File.Exists(NtApi.ToExtendedPath(p)) || Directory.Exists(NtApi.ToExtendedPath(p)))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true });
            else Shell.Info(L.T("Mục không còn tồn tại", "Item no longer exists"), p);
        }
        catch (Exception ex) { Shell.Info(L.T("Không mở được Explorer", "Cannot open Explorer"), ex.Message); }
    }
}
