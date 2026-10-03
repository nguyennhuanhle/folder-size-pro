using FolderSizePro.Model;
using FolderSizePro.Native;
using FolderSizePro.Text;

namespace FolderSizePro.Analysis;

public enum RowStatus { Measured, Unknown, Inaccessible }

public sealed record ReconcileRow(string Key, string Label, long? Bytes, RowStatus Status, string Detail);

/// <summary>Bảng Đối chiếu ổ đĩa (Q9): chia số "Used" thành các phần và nói thẳng phần chưa giải thích được.</summary>
public sealed class ReconcileResult
{
    public VolumeInfo Volume { get; init; } = null!;
    public string RootPath { get; init; } = "";
    public long Used { get; init; }
    public long TreeAllocated { get; init; }
    /// <summary>Phần đã giải thích nhưng KHÔNG nằm trong cây: $MFT (FSCTL) và cluster dự trữ của NTFS.</summary>
    public long ExtraAccounted { get; init; }
    public long Unexplained => Used - TreeAllocated - ExtraAccounted;
    public double UnexplainedFraction => Used > 0 ? (double)Unexplained / Used : 0;
    public bool Warn => Math.Abs(UnexplainedFraction) > 0.01;           // ER-08
    public int InaccessibleDirs { get; init; }
    public List<ReconcileRow> Rows { get; init; } = new();
    public List<string> Hints { get; init; } = new();
    public DateTime MeasuredUtc { get; init; }
}

internal static class Reconciler
{
    static readonly string[] PageFiles = { "pagefile.sys", "hiberfil.sys", "swapfile.sys" };

    public static ReconcileResult Build(ScanResult r, RootInfo ri)
    {
        var t = r.Tree; var v = ri.Volume;
        ref var root = ref t[ri.Node];
        long tree = root.Allocated;
        long meta = 0, page = 0, recycle = 0, svi = 0; bool sviMeasured = false, sviDenied = false, recycleDenied = false, extendDenied = false;
        var metaNames = new List<string>(); int pagesSeen = 0; bool mftVisible = false;
        for (int k = 0; k < root.ChildCount; k++)
        {
            int c = root.FirstChild + k; ref var cn = ref t[c];
            string name = t.NameString(c);
            if (name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) { recycle = cn.Allocated; recycleDenied = cn.Has(NodeFlags.AccessDenied); continue; }
            if (name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) { svi = cn.Allocated; sviDenied = cn.Has(NodeFlags.AccessDenied); sviMeasured = !sviDenied; continue; }
            if (!cn.IsDir && PageFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) { page += cn.Allocated; pagesSeen++; continue; }
            if (name.StartsWith('$') && (v.IsNtfs))
            {
                meta += cn.Allocated; metaNames.Add(name);
                if (name.Equals("$MFT", StringComparison.OrdinalIgnoreCase)) mftVisible = true;
                if (name.Equals("$Extend", StringComparison.OrdinalIgnoreCase) && cn.Has(NodeFlags.AccessDenied)) extendDenied = true;
            }
        }
        long plain = tree - meta - page - recycle - (sviMeasured ? svi : 0);
        long mftExtra = v.IsNtfs && !mftVisible ? (v.MftBytes ?? 0) : 0;
        long reservedExtra = v.IsNtfs ? (v.ReservedBytes ?? 0) : 0;
        long vssExtra = 0, attrExtra = 0;
        int denied = 0; long deniedDirs = 0, cloudFiles = 0;
        int n = t.Count;
        for (int i = 1; i < n; i++)
        {
            ref var nd = ref t[i];
            if (nd.Parent < 0) continue;
            bool isDenied = nd.Has(NodeFlags.AccessDenied), isCloud = !nd.IsDir && nd.Has(NodeFlags.Cloud);
            if (!isDenied && !isCloud) continue;
            // chỉ tính nút thuộc gốc này
            int top = i; while (t[top].Parent > 0) top = t[top].Parent;
            if (top != ri.Node) continue;
            if (isDenied) denied++;
            if (isCloud) cloudFiles++;
        }
        deniedDirs = denied;

        var rows = new List<ReconcileRow>
        {
            new("data", L.T("Dữ liệu đã đo (file + thư mục thường)", "Measured data (regular files + folders)"), plain, RowStatus.Measured, L.T("Phần đo được trong cây, chưa gồm các dòng đặc biệt bên dưới.", "What was measured in the tree, excluding the special rows below.")),
            new("meta", L.T("NTFS metadata ($MFT, $LogFile, $Bitmap…)", "NTFS metadata ($MFT, $LogFile, $Bitmap…)"), v.IsNtfs ? meta + mftExtra : null, v.IsNtfs ? RowStatus.Measured : RowStatus.Unknown,
                v.IsNtfs ? (mftExtra > 0 ? $"$MFT = {ByteFormatter.Format(mftExtra)} (đọc qua FSCTL_GET_NTFS_VOLUME_DATA, không cần Administrator). " : "") + (metaNames.Count > 0 ? string.Join(", ", metaNames.OrderBy(x => x)) + ". " : "")
                         + "$LogFile, $Bitmap, $Extend (USN journal, quota…) chỉ đo được khi chạy Administrator."
                         : L.T("Không phải NTFS.", "Not NTFS.")),
            new("reserved", L.T("Cluster dự trữ của NTFS (TotalReserved)", "NTFS reserved clusters (TotalReserved)"), v.IsNtfs ? reservedExtra : null, v.IsNtfs ? RowStatus.Measured : RowStatus.Unknown, v.IsNtfs ? L.T("Hệ thống giữ chỗ cho $MFT và metadata; đọc qua FSCTL_GET_NTFS_VOLUME_DATA.", "Reserved by the system for $MFT and metadata; read via FSCTL_GET_NTFS_VOLUME_DATA.") : L.T("Không phải NTFS.", "Not NTFS.")),
            new("pagefile", L.T("pagefile / hiberfil / swapfile", "pagefile / hiberfil / swapfile"), page, pagesSeen > 0 ? RowStatus.Measured : RowStatus.Measured, pagesSeen > 0 ? $"{pagesSeen} file." : L.T("Không có file nào ở gốc ổ.", "No such file at the drive root.")),
            new("recycle", L.T("Thùng rác ($Recycle.Bin)", "Recycle Bin ($Recycle.Bin)"), recycle, recycleDenied ? RowStatus.Inaccessible : RowStatus.Measured,
                recycleDenied ? L.T("Không truy cập được — thư mục con của người dùng khác cần Administrator.", "Inaccessible — other users' subfolders need Administrator.") : L.T("Chỉ phần quyền hiện tại đọc được; thư mục con của người dùng khác có thể bị chặn.", "Only what the current rights can read; other users' subfolders may be blocked.")),
            sviMeasured
                ? new ReconcileRow("svi", L.T("System Volume Information (VSS, điểm khôi phục)", "System Volume Information (VSS, restore points)"), svi, RowStatus.Measured, L.T("Đọc được (đang chạy Administrator).", "Readable (running as Administrator)."))
                : new ReconcileRow("svi", L.T("System Volume Information (VSS, điểm khôi phục)", "System Volume Information (VSS, restore points)"), null, RowStatus.Inaccessible, sviDenied ? L.T("Không truy cập được nếu không chạy Administrator — thường là phần lớn chênh lệch.", "Inaccessible without Administrator — usually most of the difference.") : L.T("Không có thư mục này.", "This folder does not exist.")),
            VssRow(v, sviMeasured ? svi : 0, sviMeasured, ref vssExtra),
            new("denied", L.T("Thư mục không truy cập được", "Inaccessible folders"), null, deniedDirs > 0 ? RowStatus.Inaccessible : RowStatus.Measured, deniedDirs > 0 ? $"{deniedDirs:N0} thư mục — kích thước không xác định." : L.T("Không có.", "None.")),
        };

        bool haveMft = r.MftExtras.TryGetValue(ri.Path.TrimEnd((char)92), out var mx);
        if (!haveMft && v.IsNtfs && cloudFiles > 0)
            rows.Add(new("cfmeta", L.T("Siêu dữ liệu ẩn của OneDrive (Cloud Files)", "Hidden OneDrive metadata (Cloud Files)"), null, RowStatus.Unknown,
                L.T($"{cloudFiles:N0} file đám mây. Bộ lọc Cloud Files giấu luồng siêu dữ liệu nội bộ (vài KB mỗi file), nên phần này nằm trong 'Chưa giải thích'. Chế độ MFT (Administrator) đo được chính xác.",
                    $"{cloudFiles:N0} cloud files. The Cloud Files filter hides its internal metadata stream (a few KB per file), so this part stays in 'Unexplained'. MFT mode (Administrator) measures it exactly.")));
        if (haveMft && mx != null)
        {
            long diff = mx.BitmapUsedBytes - mx.AttributeBytes;
            rows.Add(new("bitmap", L.T("Cluster đánh dấu đã cấp phát trong $Bitmap", "Clusters marked allocated in $Bitmap"), mx.BitmapUsedBytes, RowStatus.Measured, L.T("Đếm bit trực tiếp trong $Bitmap — độc lập với cây thư mục.", "Bits counted directly in $Bitmap — independent of the folder tree.")));
            rows.Add(new("attrs", L.T("Tổng cluster của mọi attribute NTFS (mọi bản ghi MFT)", "Clusters of all NTFS attributes (all MFT records)"), mx.AttributeBytes, RowStatus.Measured, $"Từ {mx.Records:N0} bản ghi MFT, gồm cả $BITMAP, $INDEX_ALLOCATION, luồng ADS."));
            rows.Add(new("bitdiff", L.T("Chênh lệch $Bitmap − attribute", "$Bitmap − attributes difference"), diff, Math.Abs(diff) <= 4L * 1024 * 1024 ? RowStatus.Measured : RowStatus.Unknown, L.T("Cluster được đánh dấu dùng nhưng không thuộc attribute nào (cluster hỏng, ghi dở, mồ côi) — gần 0 là tốt.", "Clusters marked used but owned by no attribute (bad clusters, in-flight writes, orphans) — near 0 is good.")));
            attrExtra = mx.OtherAttributeBytes;
            rows.Add(new("otherattrs", L.T("Attribute NTFS ngoài luồng dữ liệu", "NTFS attributes outside data streams"), attrExtra, RowStatus.Measured,
                L.T("$REPARSE_POINT lớn (placeholder OneDrive), $ATTRIBUTE_LIST, $BITMAP của thư mục, $EA… — cluster thật trong $Bitmap nhưng không thuộc luồng dữ liệu nào nên không nằm trong cây. Chế độ thường không đo được phần này.",
                    "Large $REPARSE_POINT (OneDrive placeholders), $ATTRIBUTE_LIST, folder $BITMAP, $EA… — real clusters in $Bitmap that belong to no data stream, so they are not in the tree. Normal mode cannot measure this.")));
            if (mx.CloudInternalFiles > 0)
                rows.Add(new("cfmeta", L.T("Siêu dữ liệu ẩn của OneDrive (Cloud Files)", "Hidden OneDrive metadata (Cloud Files)"), mx.CloudInternalBytes, RowStatus.Measured,
                    L.T($"Luồng nội bộ của {mx.CloudInternalFiles:N0} file — đã nằm trong Trên đĩa của từng file. Chế độ thường không thấy vì bộ lọc Cloud Files giấu luồng này.",
                        $"Internal stream of {mx.CloudInternalFiles:N0} files — already included in each file's Size on disk. Normal mode cannot see it because the Cloud Files filter hides it.")));
            rows.Add(new("orphans",L.T("Bản ghi MFT mồ côi / hỏng", "Orphaned / corrupt MFT records"), mx.OrphanBytes, mx.OrphanRecords + mx.CorruptRecords > 0 ? RowStatus.Unknown : RowStatus.Measured, $"{mx.OrphanRecords:N0} mồ côi (không dựng được đường dẫn) · {mx.CorruptRecords:N0} hỏng — nằm trong 'Chưa giải thích', không bị đoán (ER-21)."));
        }

        var res = new ReconcileResult
        {
            Volume = v, RootPath = ri.Path, Used = v.UsedBytes, TreeAllocated = tree, ExtraAccounted = mftExtra + reservedExtra + vssExtra + attrExtra, InaccessibleDirs = (int)deniedDirs,
            Rows = rows, MeasuredUtc = DateTime.UtcNow,
        };
        if (res.Warn || deniedDirs > 0 || !sviMeasured)
        {
            if (deniedDirs > 0 && !IsAdmin()) res.Hints.Add(L.T("Có thư mục không truy cập được — chạy Folder Size Pro bằng Administrator để đo thêm.", "Some folders are inaccessible — run Folder Size Pro as Administrator to measure more."));
            if (sviDenied) res.Hints.Add(L.T("System Volume Information (VSS/điểm khôi phục) bị chặn: đây thường là phần lớn của số 'Chưa giải thích'.", "System Volume Information (VSS/restore points) is blocked: this is usually most of the 'Unexplained' amount."));
            if (r.Issues.Any(i => i.Kind == IssueKind.Changed)) res.Hints.Add(L.T("Ổ đang thay đổi trong lúc quét — số liệu là ảnh chụp tại thời điểm quét (Q8).", "The drive changed during the scan — figures are a snapshot at scan time (Q8)."));
        }
        if (r.Options.Approximate) res.Hints.Add(L.T("Đang dùng Quét nhanh — kém chính xác, nên chạy lại ở chế độ chính xác khi cần đối chiếu.", "Fast scan in use — less accurate; rescan in exact mode when you need reconciliation."));
        return res;
    }

    /// <summary>VSS do WMI báo. Vùng diff của VSS là file trong System Volume Information nhưng volsnap ẨN nó khỏi danh sách thư mục —
    /// nên phần VSS lớn hơn những gì cây đo được trong SVI được cộng vào "đã giải thích" (không đếm đôi).</summary>
    static ReconcileRow VssRow(VolumeInfo v, long sviInTree, bool sviReadable, ref long extra)
    {
        string label = L.T("Dung lượng Shadow Copy (VSS) hệ thống báo", "Shadow Copy (VSS) size reported by the system");
        if (!v.IsNtfs) return new("vss", label, null, RowStatus.Unknown, L.T("Không phải NTFS.", "Not NTFS."));
        if (!IsAdmin()) return new("vss", label, null, RowStatus.Unknown, L.T("Cần Administrator để hỏi (không phải 0).", "Needs Administrator to query (not 0)."));
        var q = VssInfo.Query(v.RootPath);
        if (q.Error != null) return new("vss", label, null, RowStatus.Unknown, L.T("Truy vấn WMI thất bại (không phải 0): ", "WMI query failed (not 0): ") + q.Error);       // ER-22
        long alloc = q.AllocatedBytes ?? 0;
        extra = sviReadable && alloc > sviInTree ? alloc - sviInTree : 0;
        string basis = L.T($"Đã dùng / đã cấp: {ByteFormatter.Format(q.UsedBytes ?? 0)} / {ByteFormatter.Format(alloc)}; tối đa {ByteFormatter.Format(q.MaxBytes ?? 0)}. ", $"Used / allocated: {ByteFormatter.Format(q.UsedBytes ?? 0)} / {ByteFormatter.Format(alloc)}; max {ByteFormatter.Format(q.MaxBytes ?? 0)}. ");
        string how = extra > 0
            ? L.T($"File vùng diff bị volsnap ẩn khỏi danh sách thư mục nên cây không thấy — cộng {ByteFormatter.Format(extra)} còn thiếu vào phần đã giải thích.", $"The diff-area file is hidden from directory listings by volsnap, so the tree cannot see it — the missing {ByteFormatter.Format(extra)} is counted as explained.")
            : L.T("Nằm TRONG System Volume Information và cây đã đo được — chỉ để đối chiếu, không cộng thêm.", "Lives INSIDE System Volume Information and the tree measured it — shown for comparison, not added.");
        return new("vss", label, alloc, RowStatus.Measured, basis + how);
    }

    public static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
