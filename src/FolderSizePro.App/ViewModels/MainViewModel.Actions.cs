using System.Collections.ObjectModel;
using System.Globalization;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;
using FolderSizePro.Safety;
using FolderSizePro.Scan;
using FolderSizePro.Storage;

namespace FolderSizePro.App.ViewModels;

public sealed record FieldRow(string Label, string Value);

public sealed class DetailsVm : Observable
{
    public string Title { get; init; } = "";
    public string Path { get; init; } = "";
    public ObservableCollection<FieldRow> Fields { get; } = new();
    public ObservableCollection<string> Links { get; } = new();
    public ObservableCollection<string> Streams { get; } = new();
    bool _loading = true; public bool Loading { get => _loading; set => Set(ref _loading, value); }
    string? _error; public string? Error { get => _error; set { if (Set(ref _error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(_error);
    public bool HasLinks => Links.Count > 0;
    public bool HasStreams => Streams.Count > 0;
    public void NotifyLists() { Raise(nameof(HasLinks)); Raise(nameof(HasStreams)); }
}

public sealed partial class MainViewModel
{
    // ----------------------------------------------------------------- khung nhìn phụ
    public ObservableCollection<SearchRow> SearchResults { get; } = new();
    public ObservableCollection<DiffRow> DiffRows { get; } = new();
    string _searchSummary = ""; public string SearchSummary { get => _searchSummary; set => Set(ref _searchSummary, value); }
    string _diffSummary = ""; public string DiffSummary { get => _diffSummary; set => Set(ref _diffSummary, value); }
    DetailsVm? _details; public DetailsVm? Details { get => _details; set { Set(ref _details, value); Raise(nameof(HasDetails)); } }
    public bool HasDetails => _details != null;

    /// <summary>Báo cho cửa sổ (treemap, biểu đồ) vẽ lại.</summary>
    public event Action? ViewsChanged;
    void BumpViews() => ViewsChanged?.Invoke();

    // ----------------------------------------------------------------- chi tiết (UC-10)
    CancellationTokenSource? _detailsCts;

    async Task LoadDetailsAsync()
    {
        _detailsCts?.Cancel();
        var t = Tree; var row = SelectedRow;
        if (t == null || row == null || row.IsMore || row.Node <= 0) { Details = null; return; }
        var cts = _detailsCts = new CancellationTokenSource();
        int node = row.Node;
        string path = t.FullPath(node);
        var vm = new DetailsVm { Title = t.NameString(node), Path = path };
        ref var n = ref t[node];
        void F(string l, string v) => vm.Fields.Add(new FieldRow(l, v));
        F(L.T("Đường dẫn", "Path"), path);
        F(L.T("Kích thước", "Size"), $"{Fmt.Size(n.Logical)}  ({Fmt.Exact(n.Logical)})");
        F(L.T("Trên đĩa", "On disk"), $"{Fmt.Size(n.Allocated)}  ({Fmt.Exact(n.Allocated)})");
        if (n.Resident > 0) F(L.T("Nằm trong MFT", "Stored in MFT"), $"{Fmt.Exact(n.Resident)} — {L.T("0 cluster", "0 clusters")}");
        if (n.IsDir) { F(L.T("File / thư mục con", "Files / subfolders"), $"{Fmt.Count(n.FileCount)} / {Fmt.Count(n.DirCount)}"); }
        if (n.Parent > 0 && t[n.Parent].Allocated > 0) F(L.T("% của thư mục cha", "% of parent"), ByteFormatter.Percent((double)n.Allocated / t[n.Parent].Allocated));
        string badges = Fmt.Badges(n); if (badges != "") F(L.T("Trạng thái", "State"), badges);
        if (n.IsDir && Result != null)
        {
            var hs = HardLinkInfo.ForSubtree(Result, node);
            if (hs.NonOwnerLinks > 0)
                F(L.T("Liên kết cứng trong thư mục", "Hard links in this folder"),
                    L.T($"{hs.NonOwnerLinks:N0} liên kết (của {hs.DistinctFiles:N0} file) không được cộng thêm. Đếm kiểu Explorer sẽ ra {Fmt.Size(n.Allocated + hs.SavedAllocated)} trên đĩa; số thật (Q2) là {Fmt.Size(n.Allocated)} — chênh {Fmt.Size(hs.SavedAllocated)}.",
                        $"{hs.NonOwnerLinks:N0} links (of {hs.DistinctFiles:N0} files) are not double-counted. Counting Explorer-style would give {Fmt.Size(n.Allocated + hs.SavedAllocated)} on disk; the true figure (Q2) is {Fmt.Size(n.Allocated)} — a difference of {Fmt.Size(hs.SavedAllocated)}."));
        }
        if (n.Has(NodeFlags.HardLinkNonOwner)) F(L.T("Ghi chú", "Note"), L.T("Liên kết cứng này không phải bản chủ nên không cộng vào tổng (Q2).", "This hard link is not the owner, so it is not added to totals (Q2)."));
        F(L.T("Quét lúc", "Scanned at"), Result != null ? Result.CompletedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "");
        Details = vm;
        try
        {
            var d = await Task.Run(() => NodeDetails.Load(path), cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(Details, vm)) return;
            vm.Loading = false;
            if (!d.Exists) { vm.Error = L.T("Mục không còn tồn tại hoặc không đọc được: ", "Item no longer exists or cannot be read: ") + d.Error; return; }
            F(L.T("Luồng dữ liệu chính hiện tại (hỏi hệ thống)", "Main data stream now (asked from OS)"), $"{Fmt.Exact(d.EndOfFile)} · {L.T("trên đĩa", "on disk")} {Fmt.Exact(d.AllocationSize)}");
            F(L.T("Cluster", "Cluster"), Fmt.Count(d.ClusterSize) + " B");
            F(L.T("Ngày tạo", "Created"), Fmt.Date(d.Created)); F(L.T("Ngày sửa", "Modified"), Fmt.Date(d.Modified)); F(L.T("Ngày truy cập", "Accessed"), Fmt.Date(d.Accessed));
            F(L.T("Thuộc tính", "Attributes"), AttrText(d.Attributes));
            if (!d.IsDir) F(L.T("Số liên kết cứng", "Hard links"), d.Links.ToString());
            if (d.Owner != null) F(L.T("Chủ sở hữu", "Owner"), d.Owner);
            if (d.ReparseTarget != null) F(L.T("Đích của liên kết", "Link target"), d.ReparseTarget);
            foreach (var l in d.OtherLinkPaths) vm.Links.Add(l);
            foreach (var s in d.Streams.Where(s => !s.Name.StartsWith("(", StringComparison.Ordinal))) vm.Streams.Add($"{s.Name}  ·  {Fmt.Exact(s.Size)}  ·  {L.T("trên đĩa", "on disk")} {Fmt.Exact(s.Allocated)}");
            vm.NotifyLists();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { vm.Loading = false; vm.Error = ex.Message; }
    }

    static string AttrText(uint a)
    {
        var l = new List<string>();
        if ((a & NtApi.ATTR_READONLY) != 0) l.Add("ReadOnly"); if ((a & NtApi.ATTR_HIDDEN) != 0) l.Add("Hidden"); if ((a & NtApi.ATTR_SYSTEM) != 0) l.Add("System");
        if ((a & NtApi.ATTR_DIRECTORY) != 0) l.Add("Directory"); if ((a & NtApi.ATTR_COMPRESSED) != 0) l.Add("Compressed"); if ((a & NtApi.ATTR_SPARSE) != 0) l.Add("Sparse");
        if ((a & NtApi.ATTR_REPARSE_POINT) != 0) l.Add("ReparsePoint"); if ((a & NtApi.ATTR_ENCRYPTED) != 0) l.Add("Encrypted"); if ((a & NtApi.ATTR_OFFLINE) != 0) l.Add("Offline");
        if ((a & NtApi.ATTR_RECALL_ON_DATA_ACCESS) != 0) l.Add("CloudRecallOnDataAccess"); if ((a & NtApi.ATTR_RECALL_ON_OPEN) != 0) l.Add("CloudRecallOnOpen");
        return l.Count == 0 ? "Normal" : string.Join(", ", l);
    }

    // ----------------------------------------------------------------- tìm / lọc (UC-09)
    public NodeFilter Filter { get; } = new();
    string _filterText = ""; public string FilterText { get => _filterText; set { if (Set(ref _filterText, value)) { Filter.NameText = value; ScheduleSearch(); } } }
    bool _filterRegex; public bool FilterRegex { get => _filterRegex; set { if (Set(ref _filterRegex, value)) { Filter.UseRegex = value; ScheduleSearch(); } } }
    string _filterExt = ""; public string FilterExt { get => _filterExt; set { if (Set(ref _filterExt, value)) { Filter.Extensions = value; ScheduleSearch(); } } }
    string _filterMin = ""; public string FilterMin { get => _filterMin; set { if (Set(ref _filterMin, value)) ScheduleSearch(); } }
    string _filterMax = ""; public string FilterMax { get => _filterMax; set { if (Set(ref _filterMax, value)) ScheduleSearch(); } }
    DateTime? _filterAfter; public DateTime? FilterAfter { get => _filterAfter; set { if (Set(ref _filterAfter, value)) { Filter.ModifiedAfter = value; ScheduleSearch(); } } }
    DateTime? _filterBefore; public DateTime? FilterBefore { get => _filterBefore; set { if (Set(ref _filterBefore, value)) { Filter.ModifiedBefore = value; ScheduleSearch(); } } }
    bool _fOnDisk = true; public bool FilterOnDisk { get => _fOnDisk; set { if (Set(ref _fOnDisk, value)) { Filter.UseAllocated = value; ScheduleSearch(); } } }
    bool _fFiles; public bool FilterFilesOnly { get => _fFiles; set { if (Set(ref _fFiles, value)) { Filter.FilesOnly = value; if (value) FilterDirsOnly = false; ScheduleSearch(); } } }
    bool _fDirs; public bool FilterDirsOnly { get => _fDirs; set { if (Set(ref _fDirs, value)) { Filter.DirsOnly = value; if (value) FilterFilesOnly = false; ScheduleSearch(); } } }
    bool _fHidden, _fSystem, _fCloud, _fComp, _fLink, _fAds, _fReparse, _fDenied;
    public bool FlagHidden { get => _fHidden; set { if (Set(ref _fHidden, value)) FlagsChanged(); } }
    public bool FlagSystem { get => _fSystem; set { if (Set(ref _fSystem, value)) FlagsChanged(); } }
    public bool FlagCloud { get => _fCloud; set { if (Set(ref _fCloud, value)) FlagsChanged(); } }
    public bool FlagCompressed { get => _fComp; set { if (Set(ref _fComp, value)) FlagsChanged(); } }
    public bool FlagHardLink { get => _fLink; set { if (Set(ref _fLink, value)) FlagsChanged(); } }
    public bool FlagAds { get => _fAds; set { if (Set(ref _fAds, value)) FlagsChanged(); } }
    public bool FlagReparse { get => _fReparse; set { if (Set(ref _fReparse, value)) FlagsChanged(); } }
    public bool FlagDenied { get => _fDenied; set { if (Set(ref _fDenied, value)) FlagsChanged(); } }
    bool _filterOpen; public bool FilterPanelOpen { get => _filterOpen; set => Set(ref _filterOpen, value); }
    bool _searching; public bool IsFilterActive { get => _searching; private set { if (Set(ref _searching, value)) Raise(nameof(ShowTree)); } }
    public bool ShowTree => !IsFilterActive;
    string? _filterError; public string? FilterError { get => _filterError; set { if (Set(ref _filterError, value)) Raise(nameof(HasFilterError)); } }
    public bool HasFilterError => !string.IsNullOrEmpty(_filterError);

    void FlagsChanged()
    {
        var f = NodeFlags.None;
        if (_fHidden) f |= NodeFlags.Hidden; if (_fSystem) f |= NodeFlags.System; if (_fCloud) f |= NodeFlags.Cloud | NodeFlags.CloudOnly;
        if (_fComp) f |= NodeFlags.Compressed; if (_fLink) f |= NodeFlags.MultiLink; if (_fAds) f |= NodeFlags.HasAds;
        if (_fReparse) f |= NodeFlags.Reparse; if (_fDenied) f |= NodeFlags.AccessDenied;
        Filter.RequireAny = f; ScheduleSearch();
    }

    public void ClearFilter()
    {
        FilterText = ""; FilterExt = ""; FilterMin = ""; FilterMax = ""; FilterAfter = null; FilterBefore = null; FilterRegex = false;
        FilterFilesOnly = false; FilterDirsOnly = false;
        FlagHidden = FlagSystem = FlagCloud = FlagCompressed = FlagHardLink = FlagAds = FlagReparse = FlagDenied = false;
    }

    CancellationTokenSource? _searchCts;
    System.Windows.Threading.DispatcherTimer? _debounce;

    void ScheduleSearch()
    {
        _debounce ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Stop(); _debounce.Tick -= DebounceTick; _debounce.Tick += DebounceTick; _debounce.Start();
    }

    void DebounceTick(object? s, EventArgs e) { _debounce!.Stop(); _ = RunSearchAsync(); }

    async Task RunSearchAsync()
    {
        _searchCts?.Cancel();
        var r = Result;
        Filter.MinBytes = SizeParser.TryParse(FilterMin, Settings.Unit, out var mn) ? mn : null;
        Filter.MaxBytes = SizeParser.TryParse(FilterMax, Settings.Unit, out var mx) ? mx : null;
        if (r == null || Filter.IsEmpty) { IsFilterActive = false; SearchResults.Clear(); SearchSummary = ""; FilterError = null; return; }
        var (_, err) = Filter.CompileName();
        FilterError = err;                                          // ô lọc báo lỗi ngay, không chạy tìm
        if (err != null) return;
        var cts = _searchCts = new CancellationTokenSource();
        IsFilterActive = true; SearchSummary = L.T("Đang tìm…", "Searching…");
        var snapshot = new NodeFilter
        {
            NameText = Filter.NameText, UseRegex = Filter.UseRegex, Extensions = Filter.Extensions, MinBytes = Filter.MinBytes, MaxBytes = Filter.MaxBytes,
            UseAllocated = Filter.UseAllocated, ModifiedAfter = Filter.ModifiedAfter, ModifiedBefore = Filter.ModifiedBefore, RequireAny = Filter.RequireAny,
            FilesOnly = Filter.FilesOnly, DirsOnly = Filter.DirsOnly,
        };
        try
        {
            var (hits, total) = await Task.Run(() => { var h = NodeFilter.Search(r, snapshot, 5000, cts.Token, out int tot, out _); return (h, tot); }, cts.Token);
            if (cts.IsCancellationRequested) return;
            var t = r.Tree;
            SearchResults.Clear();
            foreach (var n in hits) SearchResults.Add(new SearchRow(n, t.NameString(n), t.FullPath(n), Fmt.Size(t[n].Logical), Fmt.Size(t[n].Allocated), Fmt.Date(t[n].MTime), t[n].IsDir));
            SearchSummary = total > hits.Count ? L.T($"{Fmt.Count(total)} kết quả — hiện {Fmt.Count(hits.Count)} mục lớn nhất", $"{Fmt.Count(total)} results — showing the {Fmt.Count(hits.Count)} largest") : L.T($"{Fmt.Count(total)} kết quả", $"{Fmt.Count(total)} results");
        }
        catch (OperationCanceledException) { }
    }

    void FilterSameExtension()
    {
        if (SelectedRow is not { IsDir: false, IsMore: false } r || Tree == null) return;
        var nm = Tree.NameString(r.Node); int dot = nm.LastIndexOf('.');
        ClearFilter();
        if (dot > 0) FilterExt = nm[(dot + 1)..]; else FilterText = nm;
        Shell.SelectTab("tree");
    }

    // ----------------------------------------------------------------- quét lại nhánh (UC-17)
    void RescanSelected()
    {
        if (Result == null || SelectedRow is not { IsDir: true, IsMore: false } r || r.Node <= 0) return;
        if (IsScanning) return;
        Job = ScanJob.StartRescan(Result, r.Node);
        IsScanning = true; IsPaused = false;
        StatusText = L.T("Đang quét lại nhánh…", "Rescanning branch…");
        _autosaveAt = DateTime.MaxValue; _rootsBuilt = true;
        _timer.Start();
    }

    // ----------------------------------------------------------------- xoá vào Thùng rác (UC-13)
    void DeleteSelected()
    {
        var t = Tree; var res = Result;
        if (t == null || res == null) return;
        var rows = Shell.SelectedRows().Where(r => !r.IsMore && r.Node > 0).ToList();
        if (rows.Count == 0 && SelectedRow is { IsMore: false, Node: > 0 } sr) rows.Add(sr);
        if (rows.Count == 0) return;
        // bỏ nút con nếu cha cũng được chọn
        var nodes = rows.Select(r => r.Node).Distinct().ToList();
        nodes = nodes.Where(n => { for (int p = t[n].Parent; p > 0; p = t[p].Parent) if (nodes.Contains(p)) return false; return true; }).ToList();
        if (nodes.Any(n => t[n].Has(NodeFlags.Aggregate)))
        { Shell.Info(L.T("Không thể xoá", "Cannot delete"), L.T("Dòng '<N file khác>' của Chế độ gọn là nút gộp, không phải một mục thật — hãy chọn thư mục chứa nó hoặc quét lại không dùng Chế độ gọn.", "The '<N more files>' row of Compact mode is a grouped node, not a real item — select its folder or rescan without Compact mode.")); return; }
        var paths = nodes.Select(n => t.FullPath(n)).ToList();

        var prot = ProtectionPolicy.CheckAll(paths);                                  // KT-03 / KT-09 / KT-20
        if (!prot.Allowed) { Shell.Info(L.T("Không thể xoá — vị trí được bảo vệ", "Cannot delete — protected location"), prot.Reason ?? ""); return; }
        var est = RecycleBin.Estimate(res, nodes);
        var (ok, reason) = RecycleBin.CheckEligibility(paths, est.SelectedLogical, est.MaxPathLength);
        if (!ok) { Shell.Info(L.T("Không thể đưa vào Thùng rác", "Cannot move to Recycle Bin"), reason ?? ""); return; }   // ER-11
        if (!Shell.ConfirmDelete(new DeleteRequest { Paths = paths, Estimate = est })) return;

        var results = RecycleBin.Delete(paths, Shell.Hwnd);
        var gone = new List<int>(); var problems = new List<string>();
        for (int i = 0; i < results.Count; i++)
        {
            var rr = results[i];
            if (rr.Outcome is DeleteOutcome.Recycled or DeleteOutcome.NotFound) gone.Add(nodes[i]);           // ER-09: mục đã mất → gỡ khỏi cây
            if (rr.Outcome != DeleteOutcome.Recycled) problems.Add($"{rr.Path}\n   → {OutcomeText(rr.Outcome)}: {rr.Detail}");
            Log.Info($"Xoá vào Thùng rác: {rr.Path} → {rr.Outcome} {rr.Detail}");
        }
        if (gone.Count > 0)
        {
            TreeEditor.RemoveNodes(res, gone, L.T("Đã đưa vào Thùng rác / gỡ khỏi cây", "Moved to Recycle Bin / removed from tree"));
            _expanded.RemoveWhere(n => t[n].Parent == -2);
            SelectedRow = null;
            FillAfterScan(L.T($"Đã xử lý {gone.Count} mục", $"Processed {gone.Count} items"));
        }
        if (problems.Count > 0) Shell.Info(L.T("Một số mục chưa xoá được", "Some items were not deleted"), string.Join("\n\n", problems));
    }

    static string OutcomeText(DeleteOutcome o) => o switch
    {
        DeleteOutcome.NotFound => L.T("Không còn tồn tại", "No longer exists"), DeleteOutcome.Locked => L.T("Đang bị khoá", "Locked"),
        DeleteOutcome.Refused => L.T("Bị chặn", "Refused"), DeleteOutcome.Failed => L.T("Lỗi", "Failed"), _ => L.T("Đã xoá", "Done"),
    };

    // ----------------------------------------------------------------- snapshot (UC-14, UC-15)
    string? _snapshotFile;

    /// <summary>KT-43: cảnh báo khi file xuất / snapshot nằm trong vùng đang đo (ghi vào đó sẽ làm số liệu lệch).</summary>
    bool ConfirmOutsideScan(string file)
    {
        if (Result == null) return true;
        string full = Path.GetFullPath(file);
        foreach (var r in Result.Roots)
        {
            string root = r.Path.TrimEnd((char)92) + (char)92;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Shell.Confirm(L.T("File nằm trong vùng đang đo", "File is inside the measured area"),
                    L.T($"File sẽ được ghi vào {r.Path} — chính vùng bạn đang đo, nên lần quét sau sẽ tính thêm file này. Tiếp tục?", $"The file will be written inside {r.Path} — the very area you are measuring, so the next scan will include it. Continue?"),
                    L.T("Tiếp tục", "Continue"), L.T("Chọn nơi khác", "Choose elsewhere"));
        }
        return true;
    }

    void SaveSnapshot()
    {
        if (Result == null) return;
        var f = Shell.PickSave("Folder Size Pro snapshot (*.fsp)|*.fsp", $"scan-{DateTime.Now:yyyyMMdd-HHmm}.fsp");
        if (f == null || !ConfirmOutsideScan(f)) return;
        var r = Result;
        StatusText = L.T("Đang lưu snapshot…", "Saving snapshot…");
        Task.Run(() => { try { FspFile.Save(r, f); return (string?)null; } catch (Exception ex) { return ex.Message; } }).ContinueWith(t =>
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (t.Result != null) Shell.Info(L.T("Không lưu được snapshot", "Cannot save snapshot"), t.Result);        // ER-13
                else StatusText = L.T("Đã lưu snapshot: ", "Snapshot saved: ") + f;
            }));
    }

    public void OpenSnapshot(string file)
    {
        if (IsScanning) { Shell.Info(L.T("Đang quét", "Scan running"), L.T("Hãy dừng phiên quét hiện tại trước khi mở snapshot.", "Stop the current scan before opening a snapshot.")); return; }
        try
        {
            var r = FspFile.Load(file);
            ClearViews();
            Job = null; Result = r; _snapshotFile = file; HasResult = true; IsScanning = false;
            FillAfterScan(null);
            Banners.Insert(0, new BannerItem(L.T($"Đang xem snapshot đã lưu: {file} — chụp lúc {r.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}. Đây là số liệu CŨ, không phải hiện tại.", $"Viewing a saved snapshot: {file} — taken {r.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}. These are OLD figures, not current."), BannerLevel.Info));
            PathText = string.Join(" | ", r.Roots.Select(x => x.Path));
            RebuildRows();
        }
        catch (FspFormatException ex) { Shell.Info(L.T("Không mở được snapshot", "Cannot open snapshot"), ex.Message); }          // ER-12
        catch (Exception ex) { Shell.Info(L.T("Không mở được snapshot", "Cannot open snapshot"), ex.Message); }
    }

    bool _crossPending;

    /// <summary>UC-33 / ER-23: quét lại cùng gốc bằng chế độ còn lại (MFT ↔ thường) rồi so hai kết quả theo nhánh — không chọn bên nào.</summary>
    public void CrossCheckModes()
    {
        if (Result == null || IsScanning) return;
        if (!IsAdmin) { Shell.Info(L.T("Cần Administrator", "Administrator required"), L.T("Đối chứng MFT ↔ quét thường cần quyền Administrator (để quét MFT).", "MFT ↔ normal cross-check needs Administrator (for the MFT scan).")); return; }
        var roots = Result.Roots.Where(r => r.IsVolumeRoot && r.Volume.IsNtfs).Select(r => r.Path).ToList();
        if (roots.Count == 0) { Shell.Info(L.T("Không áp dụng", "Not applicable"), L.T("Đối chứng chỉ áp dụng khi đã quét cả một ổ NTFS (gốc ổ).", "Cross-check applies only after scanning a whole NTFS drive (drive root).")); return; }
        bool wasMft = Result.ModeUsed == "Mft";
        _cmpA = Result; CompareAText = (wasMft ? "MFT" : L.T("Quét thường", "Normal scan")) + " — " + Result.CompletedUtc.ToLocalTime().ToString("HH:mm:ss"); Raise(nameof(CompareAText));
        _crossPending = true;
        StartScan(roots, wasMft ? ScanMode.Normal : ScanMode.Mft);
        if (!IsScanning) _crossPending = false;
    }

    public string CompareAText { get; private set; } = "";
    public string CompareBText { get; private set; } = "";
    ScanResult? _cmpA, _cmpB;

    public void PickCompare(bool isA)
    {
        var f = Shell.PickOpenFsp(); if (f == null) return;
        try
        {
            var r = FspFile.Load(f);
            if (isA) { _cmpA = r; CompareAText = f; Raise(nameof(CompareAText)); } else { _cmpB = r; CompareBText = f; Raise(nameof(CompareBText)); }
        }
        catch (Exception ex) { Shell.Info(L.T("Không mở được snapshot", "Cannot open snapshot"), ex.Message); }
    }

    public void UseCurrentAs(bool isA)
    {
        if (Result == null) { Shell.Info(L.T("Chưa có kết quả", "No result"), L.T("Hãy quét hoặc mở một snapshot trước.", "Scan or open a snapshot first.")); return; }
        if (isA) { _cmpA = Result; CompareAText = L.T("Kết quả hiện tại", "Current result"); Raise(nameof(CompareAText)); } else { _cmpB = Result; CompareBText = L.T("Kết quả hiện tại", "Current result"); Raise(nameof(CompareBText)); }
    }

    public async Task RunCompareAsync()
    {
        if (_cmpA == null || _cmpB == null) { Shell.Info(L.T("Thiếu dữ liệu", "Missing data"), L.T("Hãy chọn đủ hai bên A và B.", "Pick both A and B.")); return; }
        var a = _cmpA; var b = _cmpB;
        var diff = await Task.Run(() => SnapshotComparer.Compare(a, b));
        DiffRows.Clear();
        foreach (var e in diff.Entries.Take(2000))
            DiffRows.Add(new DiffRow(DiffStatusText(e.Status), e.Path, (e.DeltaAllocated >= 0 ? "+" : "") + Fmt.Size(e.DeltaAllocated), Fmt.Size(e.AllocatedA), Fmt.Size(e.AllocatedB), Math.Sign(e.DeltaAllocated)));
        DiffSummary = L.T($"Tổng thay đổi trên đĩa: {(diff.TotalDeltaAllocated >= 0 ? "+" : "")}{Fmt.Size(diff.TotalDeltaAllocated)}  ·  A {a.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm} → B {b.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {diff.Entries.Count:N0} khác biệt",
            $"Total on-disk change: {(diff.TotalDeltaAllocated >= 0 ? "+" : "")}{Fmt.Size(diff.TotalDeltaAllocated)}  ·  A {a.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm} → B {b.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {diff.Entries.Count:N0} differences");
    }

    static string DiffStatusText(DiffStatus s) => s switch
    {
        DiffStatus.Added => L.T("Mới", "Added"), DiffStatus.Removed => L.T("Mất", "Removed"), DiffStatus.Grew => L.T("Tăng", "Grew"), DiffStatus.Shrank => L.T("Giảm", "Shrank"), _ => L.T("Cùng cỡ", "Same size"),
    };

    // ----------------------------------------------------------------- xuất (UC-16)
    void Export(string kind)
    {
        var r = Result; if (r == null) return;
        string ext = kind == "csv" ? "csv" : kind == "html" ? "html" : "json";
        var f = Shell.PickSave(kind == "csv" ? "CSV (*.csv)|*.csv" : kind == "html" ? "HTML (*.html)|*.html" : "JSON (*.json)|*.json", $"folder-size-{DateTime.Now:yyyyMMdd-HHmm}.{ext}");
        if (f == null || !ConfirmOutsideScan(f)) return;
        int top = Math.Max(Settings.TopN, 20);
        StatusText = L.T("Đang xuất…", "Exporting…");
        var unit = Settings.Unit;
        Task.Run(() =>
        {
            string tmp = f + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    if (kind == "csv") ResultExporter.WriteCsv(r, fs, 8);
                    else if (kind == "html") HtmlReport.Write(r, fs, 3, top, unit);
                    else ResultExporter.WriteJson(r, fs, 8, top);
                    fs.Flush(true);
                }
                File.Move(tmp, f, true);
                return (string?)null;
            }
            catch (Exception ex) { try { File.Delete(tmp); } catch { } return ex.Message; }                 // ER-13
        }).ContinueWith(t => Application.Current.Dispatcher.Invoke(() =>
        {
            if (t.Result != null) { Shell.Info(L.T("Không xuất được file", "Cannot export"), t.Result); StatusText = L.T("Xuất thất bại", "Export failed"); }
            else StatusText = L.T("Đã xuất: ", "Exported: ") + f;
        }));
    }

    // ----------------------------------------------------------------- tiện ích
    public string? SelectWhenReady { get; set; }

    public int FindNodeByPath(string path)
    {
        var t = Tree; var r = Result; if (t == null || r == null) return -1;
        string p = path.TrimEnd((char)92);
        foreach (var root in r.Roots)
        {
            string rp = root.Path.TrimEnd((char)92);
            if (!p.StartsWith(rp, StringComparison.OrdinalIgnoreCase)) continue;
            int cur = root.Node;
            var rest = p.Length > rp.Length ? p[(rp.Length + 1)..].Split((char)92, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
            foreach (var part in rest)
            {
                int found = -1;
                foreach (var c in LiveChildren(cur)) if (t.NameString(c).Equals(part, StringComparison.OrdinalIgnoreCase)) { found = c; break; }
                if (found < 0) return -1; cur = found;
            }
            return cur;
        }
        return -1;
    }

    public void ShowRecovered(ScanResult r, string[] roots)
    {
        ClearViews();
        Job = null; Result = r; HasResult = true; IsScanning = false;
        FillAfterScan(null);
        Banners.Insert(0, new BannerItem(L.T("Đã khôi phục kết quả dở dang của phiên quét bị gián đoạn. Thư mục đánh dấu “chưa quét” chưa được đo — hãy quét lại để có số liệu đầy đủ.", "Restored the partial result of an interrupted scan. Folders marked “not scanned” were not measured — rescan for complete figures."), BannerLevel.Warn, L.T("Quét lại", "Rescan"), () => { PathText = string.Join(" | ", roots); StartScan(roots); }));
        PathText = string.Join(" | ", roots);
        BuildRootRows();
    }

    public void ApplySettings()
    {
        Fmt.Unit = Settings.Unit;
        if (Result != null && !IsScanning) { FillAfterScan(null); if (Settings.LiveWatch && _watcher == null) StartWatcherIfEnabled(); else if (!Settings.LiveWatch) StopWatcher(); }
        RaiseAll();
    }

    public void Shutdown()
    {
        _timer.Stop(); StopWatcher();
        try { Job?.Cancel(); } catch { }
        SessionStore.Clear();
    }

    public IEnumerable<int> RootNodes => Result?.Roots.Select(r => r.Node) ?? Enumerable.Empty<int>();
}

/// <summary>"100 MB", "1,5 GiB", "300k" → byte. Dùng cho ô lọc kích thước.</summary>
public static class SizeParser
{
    public static bool TryParse(string? text, UnitSystem unit, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().Replace(" ", "");
        int i = 0; while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == ',')) i++;
        if (i == 0) return false;
        string num = s[..i].Replace(',', '.'); string suf = s[i..].ToLowerInvariant();
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
        double step = unit == UnitSystem.Binary ? 1024 : 1000;
        double mul = suf switch
        {
            "" or "b" => 1,
            "k" or "kb" or "kib" => suf == "kib" ? 1024 : step,
            "m" or "mb" or "mib" => Math.Pow(suf == "mib" ? 1024 : step, 2),
            "g" or "gb" or "gib" => Math.Pow(suf == "gib" ? 1024 : step, 3),
            "t" or "tb" or "tib" => Math.Pow(suf == "tib" ? 1024 : step, 4),
            _ => double.NaN,
        };
        if (double.IsNaN(mul)) return false;
        bytes = (long)(v * mul); return true;
    }
}
