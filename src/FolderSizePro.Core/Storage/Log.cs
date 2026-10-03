namespace FolderSizePro.Storage;

/// <summary>UC-54: log chẩn đoán cục bộ, xoay vòng (10 file × 2 MB). Không bao giờ gửi đi đâu (KT-40). Nội dung tiếng Việt.</summary>
public static class Log
{
    const long MaxBytes = 2 * 1024 * 1024; const int MaxFiles = 10;
    static readonly object Gate = new();
    static string Current => Path.Combine(AppPaths.Logs, "fsp.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : msg + " :: " + ex);

    static void Write(string level, string msg)
    {
        if (!AppPaths.Writable) return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var fi = new FileInfo(Current);
                if (fi.Exists && fi.Length > MaxBytes) Rotate();
                File.AppendAllText(Current, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
            catch { }
        }
    }

    static void Rotate()
    {
        for (int i = MaxFiles - 1; i >= 1; i--)
        {
            string from = i == 1 ? Current : Path.Combine(AppPaths.Logs, $"fsp.{i - 1}.log"), to = Path.Combine(AppPaths.Logs, $"fsp.{i}.log");
            if (File.Exists(from)) File.Move(from, to, true);
        }
    }

    /// <summary>Gom log thành một file để người dùng tự quyết định gửi (không tự gửi).</summary>
    public static string? Bundle(string destination)
    {
        try
        {
            using var w = new StreamWriter(destination, false, new System.Text.UTF8Encoding(true));
            foreach (var f in Directory.GetFiles(AppPaths.Logs, "fsp*.log").OrderBy(x => x)) { w.WriteLine("=== " + Path.GetFileName(f)); w.Write(File.ReadAllText(f)); }
            return destination;
        }
        catch { return null; }
    }
}
