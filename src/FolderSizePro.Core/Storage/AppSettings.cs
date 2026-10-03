using System.Text.Json;
using System.Text.Json.Serialization;
using FolderSizePro.Model;
using FolderSizePro.Text;

namespace FolderSizePro.Storage;

public sealed class AppSettings
{
    public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi" ? "vi" : "en";
    public string Theme { get; set; } = "system";                 // system | light | dark
    [JsonConverter(typeof(JsonStringEnumConverter))] public UnitSystem Unit { get; set; } = UnitSystem.Binary;
    public bool IncludeAds { get; set; } = true;
    public bool FastMode { get; set; }
    /// <summary>auto = MFT khi là Administrator + NTFS + gốc ổ; normal = luôn quét thường (đối chứng, UC-33); mft = luôn MFT.</summary>
    public string ScanModePref { get; set; } = "auto";
    public bool ShowHiddenSystem { get; set; } = true;
    public List<string> ExcludedPaths { get; set; } = new();
    public int NetworkTimeoutSec { get; set; } = 30;
    public int MemoryLimitMb { get; set; } = 2048;
    public int TopN { get; set; } = 100;
    public bool LiveWatch { get; set; }
    public List<string> RecentPaths { get; set; } = new();
    public double WindowWidth { get; set; } = 1320;
    public double WindowHeight { get; set; } = 820;

    public ScanOptions ToScanOptions() => new()
    {
        FastMode = FastMode, IncludeAds = IncludeAds && !FastMode, ExcludedPaths = new List<string>(ExcludedPaths),
        NetworkTimeoutSec = NetworkTimeoutSec, MemoryLimitBytes = (long)Math.Max(256, MemoryLimitMb) * 1024 * 1024, TopN = TopN,
    };
}

/// <summary>ER-41: file cài đặt hỏng → đổi tên .bad và tạo lại mặc định; ER-42: không ghi được → chạy chỉ-bộ-nhớ.</summary>
public static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string? LastWarning { get; private set; }

    public static AppSettings Load()
    {
        LastWarning = null;
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json);
            return s ?? throw new JsonException("rỗng");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastWarning = $"File cài đặt hỏng ({ex.Message}); đã đổi tên thành .bad và dùng cài đặt mặc định.";
            try { File.Move(AppPaths.SettingsFile, AppPaths.SettingsFile + ".bad", true); } catch { }
            return new AppSettings();
        }
    }

    public static bool Save(AppSettings s)
    {
        if (!AppPaths.Writable) return false;
        try
        {
            string tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
            File.Move(tmp, AppPaths.SettingsFile, true);
            return true;
        }
        catch { return false; }
    }
}
