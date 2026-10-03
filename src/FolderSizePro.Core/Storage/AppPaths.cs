namespace FolderSizePro.Storage;

/// <summary>
/// KT-43: mọi file của app (cài đặt, phiên, log, cache) chỉ ghi vào %LOCALAPPDATA%\FolderSizePro — không bao giờ vào ổ đang đo,
/// trừ file xuất do người dùng chọn. ER-42: nếu không ghi được thì app chạy "chỉ bộ nhớ".
/// </summary>
public static class AppPaths
{
    public static string Local { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderSizePro");
    public static string Logs => Path.Combine(Local, "logs");
    public static string Session => Path.Combine(Local, "session");
    public static string SettingsFile => Path.Combine(Local, "settings.json");
    public static bool Writable { get; private set; } = true;
    public static string? WritableError { get; private set; }

    /// <summary>Cho kiểm thử: đổi gốc lưu trữ.</summary>
    public static void Override(string dir) { Local = dir; Probe(); }

    /// <summary>Thăm dò quyền ghi (ER-42). Gọi một lần khi khởi động.</summary>
    public static bool Probe()
    {
        try
        {
            Directory.CreateDirectory(Local);
            string t = Path.Combine(Local, ".probe");
            File.WriteAllText(t, "ok"); File.Delete(t);
            Directory.CreateDirectory(Logs); Directory.CreateDirectory(Session);
            Writable = true; WritableError = null;
        }
        catch (Exception ex) { Writable = false; WritableError = ex.Message; }
        return Writable;
    }
}
