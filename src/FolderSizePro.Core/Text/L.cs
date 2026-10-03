namespace FolderSizePro.Text;

/// <summary>
/// Chữ hai thứ tiếng: mỗi chỗ viết cả hai bản ngay tại chỗ — L.T("Quét", "Scan"). Ngôn ngữ chọn một lần lúc khởi động
/// (đổi ngôn ngữ = mở lại app). Nhật ký (log) luôn tiếng Việt.
/// </summary>
public static class L
{
    public static bool English { get; set; }
    public static string T(string vi, string en) => English ? en : vi;
    public static void Use(string? language) => English = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
}
