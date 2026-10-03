namespace FolderSizePro.Model;

public enum ScanMode { Auto, Normal, Mft }

public enum IssueKind { AccessDenied, Changed, VolumeLost, NetworkTimeout, EstimatedFromListing, Excluded, CloudNotListed, Other }

public sealed record ScanIssue(string Path, IssueKind Kind, int NtStatus, string Message);

public enum Completeness { Complete, Partial }

public sealed class ScanOptions
{
    /// <summary>Exact = mở từng file lấy kích thước thật + ADS (mặc định, chính xác). Fast = tin danh sách thư mục, bỏ ADS — nhãn "kém chính xác".</summary>
    public bool FastMode { get; set; }
    public bool IncludeAds { get; set; } = true;
    public ScanMode Mode { get; set; } = ScanMode.Auto;
    public List<string> ExcludedPaths { get; set; } = new();
    public int MaxThreads { get; set; }                 // 0 = tự chọn theo loại ổ
    public int NetworkTimeoutSec { get; set; } = 30;
    public long MemoryLimitBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public int TopN { get; set; } = 100;
    public int CompactKeepPerDir { get; set; } = 200;

    public bool Approximate => FastMode || !IncludeAds;
}

public readonly record struct ScanProgress(
    long Files, long Dirs, long Logical, long Allocated, long Issues,
    string CurrentPath, TimeSpan Elapsed, double FilesPerSec, bool Paused, bool CompactMode, bool Finishing);

public sealed record TypeStat(string Extension, string Group, long Count, long Logical, long Allocated);

public sealed record TopFile(int Node, long Logical, long Allocated);

public sealed record TopDir(int Node, long DirectFiles, long DirectAllocated);

public sealed class ConsistencyItem
{
    public string Name { get; init; } = "";
    public string Expected { get; init; } = "";
    public string Actual { get; init; } = "";
    public bool Ok { get; init; }
}

public sealed class RootInfo
{
    public int Node { get; init; }
    public string Path { get; init; } = "";
    public Native.VolumeInfo Volume { get; init; } = null!;
    public bool IsVolumeRoot { get; init; }
}

public sealed class ScanResult
{
    public ScanTree Tree { get; init; } = null!;
    public List<RootInfo> Roots { get; } = new();
    public List<ScanIssue> Issues { get; } = new();
    public List<string> Notes { get; } = new();
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }   // KT-08: bắt buộc
    public Completeness Completeness { get; set; } = Completeness.Complete;
    public ScanOptions Options { get; set; } = new();
    public string ModeUsed { get; set; } = "Normal";
    public bool CompactModeUsed { get; set; }
    public List<TypeStat> TypeStats { get; set; } = new();
    public List<TopFile> TopFiles { get; set; } = new();
    public List<TopDir> TopDirs { get; set; } = new();
    public List<ConsistencyItem> Consistency { get; } = new();
    public List<Analysis.ReconcileResult> Reconcile { get; } = new();
    internal List<Scan.HardLinkCand> HardLinkCands { get; set; } = new();
    /// <summary>Chế độ MFT: số liệu đối chiếu $Bitmap, bản ghi mồ côi… (khoá = gốc ổ, vd "D:").</summary>
    public Dictionary<string, Scan.MftExtras> MftExtras { get; } = new(StringComparer.OrdinalIgnoreCase);
    public long HardLinkNonOwnerCount { get; set; }
    public long HardLinkSavedAllocated { get; set; }
    public TimeSpan Duration => CompletedUtc - StartedUtc;
    public long RootAllocated() => Tree[0].Allocated;
    public long RootLogical() => Tree[0].Logical;
}
