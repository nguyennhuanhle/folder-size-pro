using System.Globalization;
using System.Windows.Data;
using FolderSizePro.App.ViewModels;

namespace FolderSizePro.App;

public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NullToVisibility : IValueConverter
{
    public object Convert(object? v, Type t, object p, CultureInfo c) => v == null || (v is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BoolNot : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is not true;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is not true;
}

/// <summary>Banner: Info → nền thường; Warn → nền vàng; Error → nền đỏ nhạt.</summary>
public sealed class BannerBackground : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is BannerLevel l
        ? new SolidColorBrush(l switch { BannerLevel.Warn => Color.FromArgb(48, 245, 158, 11), BannerLevel.Error => Color.FromArgb(48, 239, 68, 68), _ => Color.FromArgb(36, 59, 130, 246) })
        : Brushes.Transparent;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BannerBorder : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is BannerLevel l
        ? new SolidColorBrush(l switch { BannerLevel.Warn => Color.FromRgb(217, 119, 6), BannerLevel.Error => Color.FromRgb(220, 38, 38), _ => Color.FromRgb(37, 99, 235) })
        : Brushes.Transparent;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Mức của dòng Đối chiếu / Vấn đề → màu chữ (0 thường, 1 mờ, 2 đỏ, 3 tiêu đề nhóm, 4 đậm).</summary>
public sealed class LevelToBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is int l
        ? l switch { 2 => Brushes.IndianRed, 1 => Brushes.Gray, _ => (object)DependencyProperty.UnsetValue }
        : DependencyProperty.UnsetValue;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class LevelToWeight : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is int l && l >= 3 ? FontWeights.SemiBold : FontWeights.Normal;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class DirectionToBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is int d ? (d > 0 ? Brushes.IndianRed : d < 0 ? Brushes.SeaGreen : Brushes.Gray) : Brushes.Gray;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class IndentToThickness : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => new Thickness(v is double d ? d : 0, 0, 0, 0);
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
