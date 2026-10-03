using System.Globalization;

namespace FolderSizePro.Text;

public enum UnitSystem { Binary, Si }

/// <summary>Q7: mặc định nhị phân (KiB… 1024), có thể đổi sang SI (kB… 1000). Số byte chính xác luôn lấy được bằng Exact().</summary>
public static class ByteFormatter
{
    static readonly string[] Bin = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
    static readonly string[] Si = { "B", "kB", "MB", "GB", "TB", "PB", "EB" };

    public static string Format(long bytes, UnitSystem unit = UnitSystem.Binary, int decimals = 2)
    {
        bool neg = bytes < 0;
        double v = Math.Abs((double)bytes);
        double step = unit == UnitSystem.Binary ? 1024 : 1000;
        var names = unit == UnitSystem.Binary ? Bin : Si;
        int i = 0;
        while (v >= step && i < names.Length - 1) { v /= step; i++; }
        string s = i == 0 ? ((long)v).ToString("N0", CultureInfo.CurrentCulture) + " B"
                          : v.ToString("N" + decimals, CultureInfo.CurrentCulture) + " " + names[i];
        return neg ? "-" + s : s;
    }

    /// <summary>"1.234.567.890 byte" — số chính xác theo ngôn ngữ hiện tại.</summary>
    public static string Exact(long bytes) => bytes.ToString("N0", CultureInfo.CurrentCulture) + " B";

    public static string Percent(double fraction) => (fraction * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";
}
