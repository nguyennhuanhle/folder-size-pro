using System.Globalization;
using FolderSizePro.Model;

namespace FolderSizePro.App.ViewModels;

/// <summary>Định dạng hiển thị dùng chung (đơn vị theo cài đặt — Q7).</summary>
public static class Fmt
{
    public static UnitSystem Unit { get; set; } = UnitSystem.Binary;

    public static string Size(long bytes) => ByteFormatter.Format(bytes, Unit);
    public static string Exact(long bytes) => ByteFormatter.Exact(bytes);
    public static string Count(long n) => n.ToString("N0", CultureInfo.CurrentCulture);
    public static string Date(long fileTime) => fileTime > 0 ? DateTime.FromFileTime(fileTime).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) : "";
    public static string Date(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) : "—";

    /// <summary>Nhãn ngắn gọn cho các cờ quan trọng, dịch vi/en.</summary>
    public static string Badges(in Node n)
    {
        var l = new List<string>();
        if (n.Has(NodeFlags.AccessDenied)) l.Add(L.T("KHÔNG TRUY CẬP ĐƯỢC", "ACCESS DENIED"));
        if (n.Has(NodeFlags.MountPoint)) l.Add(L.T("junction/mount", "junction/mount"));
        else if (n.Has(NodeFlags.Symlink)) l.Add("symlink");
        else if (n.Has(NodeFlags.Reparse) && n.IsDir && !n.Has(NodeFlags.Cloud)) l.Add("reparse");
        if (n.Has(NodeFlags.CloudNotListed)) l.Add(L.T("chưa tải danh sách (đám mây)", "listing not downloaded (cloud)"));
        if (n.Has(NodeFlags.CloudOnly)) l.Add(L.T("chỉ trên đám mây", "cloud-only"));
        if (n.Has(NodeFlags.HardLinkNonOwner)) l.Add(L.T("liên kết cứng — tính ở bản chủ", "hard link — counted at owner"));
        else if (n.Has(NodeFlags.MultiLink)) l.Add(L.T("liên kết cứng", "hard link"));
        if (n.Has(NodeFlags.HasAds)) l.Add("ADS");
        if (n.Has(NodeFlags.Compressed)) l.Add(L.T("nén", "compressed"));
        if (n.Has(NodeFlags.Sparse)) l.Add("sparse");
        if (n.Has(NodeFlags.Resident)) l.Add(L.T("trong MFT", "in MFT"));
        if (n.Has(NodeFlags.Lost)) l.Add(L.T("MẤT KẾT NỐI", "CONNECTION LOST"));
        if (n.Has(NodeFlags.TimedOut)) l.Add(L.T("HẾT THỜI GIAN", "TIMED OUT"));
        if (n.Has(NodeFlags.Changed)) l.Add(L.T("đã thay đổi khi quét", "changed during scan"));
        if (n.Has(NodeFlags.Excluded)) l.Add(L.T("đã loại trừ", "excluded"));
        if (n.Has(NodeFlags.Aggregate)) l.Add(L.T("gộp (chế độ gọn)", "grouped (compact mode)"));
        if (n.Has(NodeFlags.Pending)) l.Add(L.T("chưa quét", "not scanned"));
        if (n.Has(NodeFlags.Estimated)) l.Add(L.T("ước lượng từ danh sách thư mục", "estimated from listing"));
        return string.Join(" · ", l);
    }
}
