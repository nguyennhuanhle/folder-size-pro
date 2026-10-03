using FolderSizePro.Text;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;

namespace FolderSizePro.Scan;

/// <summary>Giao diện chung cho mọi loại phiên quét (thường / MFT), để GUI và CLI không phụ thuộc loại.</summary>
public interface IScanJob
{
    ScanTree Tree { get; }
    ScanResult Result { get; }
    Task Completion { get; }
    bool IsPaused { get; }
    bool CompactMode { get; }
    ScanProgress Snapshot();
    void Pause();
    void Resume();
    void Cancel();
}

public sealed record ScanPlan(ScanMode Mode, string? ExitCode4Reason, List<string> Notes);

/// <summary>KT-21 / ER-20: chọn chế độ quét. MFT chỉ cho NTFS; thiếu quyền khi buộc --mft → CLI trả mã 4, GUI hỏi nâng quyền.</summary>
public static class ScanPlanner
{
    public static ScanPlan Plan(IReadOnlyList<string> roots, ScanOptions opt)
    {
        var notes = new List<string>();
        bool admin = Reconciler.IsAdmin();
        bool allNtfsVolumeRoots = roots.All(r =>
        {
            var v = VolumeInfo.For(r);
            return v.IsNtfs && r.TrimEnd('\\').Equals(v.RootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        });
        bool anyNtfs = roots.Any(r => VolumeInfo.For(r).IsNtfs);

        switch (opt.Mode)
        {
            case ScanMode.Normal:
                return new(ScanMode.Normal, null, notes);
            case ScanMode.Mft:
                if (!anyNtfs || !allNtfsVolumeRoots)
                {
                    notes.Add(L.T("Chế độ MFT chỉ dùng cho gốc ổ NTFS (KT-21) — đã dùng quét thường.", "MFT mode only applies to NTFS drive roots (KT-21) — used a normal scan."));
                    return new(ScanMode.Normal, null, notes);
                }
                if (!admin) return new(ScanMode.Mft, L.T("Chế độ MFT cần quyền Administrator. Mở terminal bằng 'Run as administrator' rồi chạy lại, hoặc bỏ --mft.", "MFT mode needs Administrator rights. Open a terminal with 'Run as administrator' and rerun, or drop --mft."), notes);
                return new(ScanMode.Mft, null, notes);
            default:
                if (admin && allNtfsVolumeRoots && !opt.FastMode) return new(ScanMode.Mft, null, notes);
                return new(ScanMode.Normal, null, notes);
        }
    }

    public static IScanJob Start(IReadOnlyList<string> roots, ScanOptions opt, ScanPlan plan)
    {
        if (plan.Mode == ScanMode.Mft)
        {
            var mft = MftScanJobFactory.TryStart(roots, opt, plan.Notes, out var fallbackReason);
            if (mft != null) return mft;
            plan.Notes.Add("Không dùng được chế độ MFT: " + fallbackReason + " — đã dùng quét thường (ER-20).");
        }
        var job = ScanJob.Start(roots, opt);
        job.Result.Notes.InsertRange(0, plan.Notes);
        return job;
    }
}
