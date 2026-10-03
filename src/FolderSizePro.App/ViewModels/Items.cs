using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;

namespace FolderSizePro.App.ViewModels;

public sealed class DriveItem : Observable
{
    public VolumeInfo V { get; }
    public DriveItem(VolumeInfo v) { V = v; }
    public string Title => (V.DriveLetter != "" ? V.DriveLetter : V.RootPath) + (V.Label != "" ? "  " + V.Label : "");
    public string Sub => $"{V.FileSystem} · {(V.Kind == VolumeKind.Network ? L.T("ổ mạng", "network") : V.Kind == VolumeKind.Removable ? L.T("ổ rời", "removable") : V.IsSsd == true ? "SSD" : V.IsSsd == false ? "HDD" : L.T("ổ cục bộ", "local"))}";
    public string UsageText => $"{Fmt.Size(V.UsedBytes)} / {Fmt.Size(V.TotalBytes)} · {L.T("trống", "free")} {Fmt.Size(V.FreeBytes)}";
    public double Fraction => V.TotalBytes > 0 ? (double)V.UsedBytes / V.TotalBytes : 0;
    public bool IsCritical => Fraction > 0.9;
    public string Glyph => V.Kind == VolumeKind.Network ? "" : V.Kind == VolumeKind.Removable ? "" : "";
}

public enum BannerLevel { Info, Warn, Error }

public sealed record BannerItem(string Text, BannerLevel Level, string? ActionText = null, Action? Action = null);

public sealed record FileRow(int Node, string Name, string Path, string Logical, string Allocated, string Modified, string Badges);
public sealed record DirRow(int Node, string Path, string DirectLogical, string DirectAllocated, double Fraction);
public sealed record GroupRow(string Group, string Label, long Count, string Logical, string Allocated, double Fraction, Brush Color);
public sealed record ExtRow(string Extension, string Group, long Count, string Logical, string Allocated, double Fraction);
public sealed record IssueRow(string Kind, string Path, string Message, int Level);
public sealed record ReconcileRowVm(string Label, string Value, string Detail, int Level);
public sealed record ConsistencyRowVm(string Name, string Detail, bool Ok);
public sealed record SearchRow(int Node, string Name, string Path, string Logical, string Allocated, string Modified, bool IsDir);
public sealed record DiffRow(string Status, string Path, string Delta, string A, string B, int Direction);

public static class Palette
{
    public static readonly Dictionary<string, Color> Groups = new()
    {
        ["video"] = Color.FromRgb(0xE5, 0x4B, 0x4B), ["audio"] = Color.FromRgb(0xF2, 0x8C, 0x28), ["image"] = Color.FromRgb(0xE8, 0xB4, 0x00),
        ["document"] = Color.FromRgb(0x3B, 0x82, 0xF6), ["archive"] = Color.FromRgb(0x8B, 0x5C, 0xF6), ["code"] = Color.FromRgb(0x10, 0xB9, 0x81),
        ["app"] = Color.FromRgb(0x64, 0x74, 0x8B), ["database"] = Color.FromRgb(0x06, 0xB6, 0xD4), ["log"] = Color.FromRgb(0xA8, 0xA2, 0x9E),
        ["font"] = Color.FromRgb(0xEC, 0x48, 0x99), ["other"] = Color.FromRgb(0x94, 0xA3, 0xB8), ["noext"] = Color.FromRgb(0xCB, 0xD5, 0xE1),
    };

    public static Color Of(string group) => Groups.TryGetValue(group, out var c) ? c : Groups["other"];
    public static Brush BrushOf(string group) { var b = new SolidColorBrush(Of(group)); b.Freeze(); return b; }

    public static string Label(string group) => group switch
    {
        "video" => L.T("Video", "Video"), "audio" => L.T("Âm thanh", "Audio"), "image" => L.T("Hình ảnh", "Images"),
        "document" => L.T("Tài liệu", "Documents"), "archive" => L.T("Nén / đĩa ảnh", "Archives / images"), "code" => L.T("Mã nguồn / dữ liệu", "Code / data"),
        "app" => L.T("Ứng dụng / thư viện", "Apps / libraries"), "database" => L.T("Cơ sở dữ liệu", "Databases"), "log" => L.T("Log / tạm", "Logs / temp"),
        "font" => L.T("Phông chữ", "Fonts"), "noext" => L.T("Không đuôi", "No extension"), _ => L.T("Khác", "Other"),
    };
}
