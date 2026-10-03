using FolderSizePro.Text;
using FolderSizePro.Native;

namespace FolderSizePro.Scan;

public readonly record struct PathCheck(bool Ok, string Path, string? Error);

/// <summary>ER-01: kiểm tra đường dẫn người dùng nhập TRƯỚC khi tạo phiên quét.</summary>
public static class PathValidator
{
    public static PathCheck Validate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new(false, "", L.T("Đường dẫn trống. Hãy nhập hoặc chọn một thư mục / ổ đĩa.", "Path is empty. Enter or pick a folder / drive."));
        string p = input.Trim().Trim('"').Trim();
        try { p = Environment.ExpandEnvironmentVariables(p); } catch { }
        if (p.Length == 2 && p[1] == ':') p += "\\";                      // "D:" → "D:\"
        if (p.IndexOfAny(new[] { '<', '>', '|', '"', '*', '?' }) >= 0 || p.Contains('\0'))
            return new(false, p, L.T("Đường dẫn chứa ký tự không hợp lệ ( < > | \" * ? ).", "Path contains invalid characters ( < > | \" * ? )."));
        string full;
        try
        {
            if (!(p.Length >= 3 && p[1] == ':') && !p.StartsWith(@"\\", StringComparison.Ordinal) && !Path.IsPathRooted(p))
                full = Path.GetFullPath(p);                                // đường dẫn tương đối: tính theo thư mục hiện tại
            else full = Path.GetFullPath(p);
        }
        catch (Exception) { return new(false, p, L.T("Đường dẫn sai cú pháp.", "Invalid path syntax.")); }
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full[4..];
        string ext = NtApi.ToExtendedPath(full);
        if (File.Exists(ext)) return new(false, full, L.T("Đây là một file, không phải thư mục. Hãy chọn thư mục hoặc ổ đĩa.", "This is a file, not a folder. Pick a folder or drive."));
        if (!Directory.Exists(ext)) return new(false, full, L.T($"Không tìm thấy đường dẫn: {full}", $"Path not found: {full}"));
        if (full.Length > 3) full = full.TrimEnd('\\');
        return new(true, full, null);
    }
}
