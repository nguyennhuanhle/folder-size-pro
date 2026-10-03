using System.Windows.Markup;

namespace FolderSizePro.App;

/// <summary>Chữ hai thứ tiếng trong XAML: Text="{app:T 'Quét|Scan'}".</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string pair) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        int bar = pair.IndexOf('|');
        return bar < 0 ? pair : L.T(pair[..bar], pair[(bar + 1)..]);
    }
}
