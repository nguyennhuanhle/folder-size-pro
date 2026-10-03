using FolderSizePro.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace FolderSizePro.Safety;

public readonly record struct ProtectionVerdict(bool Allowed, string? Reason)
{
    public static ProtectionVerdict Ok => new(true, null);
    public static ProtectionVerdict Block(string reason) => new(false, reason);
}

/// <summary>
/// KT-03 / KT-09 / KT-20: các vị trí không bao giờ được xoá từ app (kể cả khi chạy Administrator).
/// Chặn: chính vị trí đó VÀ mọi thư mục chứa nó (vd xoá "C:\Users" sẽ chứa hồ sơ hiện tại → chặn).
/// Chặn cả bên trong vùng lõi Windows (System32, WinSxS…). Chặn junction / mount point / symlink-thư mục (KT-09).
/// </summary>
public static class ProtectionPolicy
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetLongPathNameW(string shortPath, StringBuilder buf, uint len);

    static readonly string[] WindowsCore = { "System32", "SysWOW64", "WinSxS", "servicing", "Boot", "Fonts", "Installer", "assembly", "Microsoft.NET", "SystemApps", "WinSxS" };
    static readonly string[] RootSpecial = { "System Volume Information", "$Recycle.Bin", "Recovery", "pagefile.sys", "hiberfil.sys", "swapfile.sys" };

    public static string Normalize(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        try { path = Path.GetFullPath(path); } catch { }
        try
        {
            var sb = new StringBuilder(1024);
            uint n = GetLongPathNameW(path, sb, (uint)sb.Capacity);
            if (n > 0 && n < sb.Capacity) path = sb.ToString();
        }
        catch { }
        return path.Length > 3 ? path.TrimEnd('\\') : path;
    }

    static IEnumerable<string> ProtectedFolders()
    {
        foreach (var d in DriveInfo.GetDrives()) yield return d.Name;
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return win;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return profile;
        var usersRoot = Path.GetDirectoryName(profile);
        if (!string.IsNullOrEmpty(usersRoot)) yield return usersRoot;
        string sysDrive = Path.GetPathRoot(win) ?? "C:\\";
        yield return Path.Combine(sysDrive, "Users");
        yield return Path.Combine(sysDrive, "Program Files");
        yield return Path.Combine(sysDrive, "ProgramData");
    }

    public static ProtectionVerdict Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ProtectionVerdict.Block(L.T("Đường dẫn trống.", "Path is empty."));
        string target = Normalize(path);
        string t = target.TrimEnd('\\');
        string tSlash = t + "\\";

        // KT-09: reparse point thư mục
        try
        {
            var attr = File.GetAttributes(Native.NtApi.ToExtendedPath(target));
            if ((attr & FileAttributes.Directory) != 0 && (attr & FileAttributes.ReparsePoint) != 0)
                return ProtectionVerdict.Block(L.T("Đây là junction / mount point / symlink thư mục — hãy xoá trong Explorer (app không xoá liên kết để tránh động vào nội dung đích).", "This is a junction / mount point / directory symlink — delete it in Explorer (the app never deletes links, to avoid touching the target's content)."));
        }
        catch { }

        // vùng lõi Windows: chặn cả bên trong
        string win = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        foreach (var core in WindowsCore.Distinct())
        {
            string cp = Path.Combine(win, core);
            if (t.Equals(cp, StringComparison.OrdinalIgnoreCase) || tSlash.StartsWith(cp + "\\", StringComparison.OrdinalIgnoreCase))
                return ProtectionVerdict.Block(L.T($"Vùng lõi của Windows ({cp}) được bảo vệ — không thể xoá từ app.", $"The Windows core area ({cp}) is protected — it cannot be deleted from the app."));
        }

        foreach (var f in ProtectedFolders())
        {
            string pf = Normalize(f);
            if (pf.Length == 0) continue;
            string pfSlash = pf.TrimEnd('\\') + "\\";
            // chính nó, hoặc target là cha/ông của vị trí được bảo vệ
            if (t.Equals(pf.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || pfSlash.StartsWith(tSlash, StringComparison.OrdinalIgnoreCase))
                return ProtectionVerdict.Block(pf.Length <= 3
                    ? L.T("Gốc ổ đĩa được bảo vệ — không thể xoá.", "A drive root is protected — it cannot be deleted.")
                    : L.T($"'{pf}' (hoặc thư mục chứa nó) là vị trí hệ thống / hồ sơ người dùng được bảo vệ — không thể xoá từ app.", $"'{pf}' (or a folder containing it) is a protected system / user-profile location — it cannot be deleted from the app."));
        }

        // gốc ổ: System Volume Information, $Recycle.Bin, các file hệ thống, metafile $…
        string root = Path.GetPathRoot(target) ?? "";
        if (root.Length > 0)
        {
            string rel = target.Length > root.Length ? target[root.Length..] : "";
            string first = rel.Split('\\', 2)[0];
            if (first.Length > 0 && (RootSpecial.Contains(first, StringComparer.OrdinalIgnoreCase) || first.StartsWith('$')))
                return ProtectionVerdict.Block(L.T($"'{first}' là dữ liệu hệ thống của ổ đĩa — được bảo vệ, không thể xoá từ app.", $"'{first}' is drive system data — protected, it cannot be deleted from the app."));
        }
        return ProtectionVerdict.Ok;
    }

    public static ProtectionVerdict CheckAll(IEnumerable<string> paths)
    {
        foreach (var p in paths) { var v = Check(p); if (!v.Allowed) return v with { Reason = $"{p}: {v.Reason}" }; }
        return ProtectionVerdict.Ok;
    }
}
