namespace FolderSizePro.Analysis;

/// <summary>Đuôi file → nhóm (khoá tiếng Anh ổn định; giao diện dịch sang vi/en).</summary>
public static class TypeCatalog
{
    public static readonly string[] Groups =
        { "video", "audio", "image", "document", "archive", "code", "app", "database", "log", "font", "other", "noext" };

    static readonly Dictionary<string, string> Map = Build();

    static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void A(string g, string list) { foreach (var e in list.Split(' ', StringSplitOptions.RemoveEmptyEntries)) d[e] = g; }
        A("video", "mp4 mkv avi mov wmv flv webm m4v mpg mpeg m2ts ts vob 3gp mts");
        A("audio", "mp3 wav flac aac ogg m4a wma opus aiff mid");
        A("image", "jpg jpeg png gif bmp tif tiff webp heic heif svg ico raw cr2 cr3 nef arw dng psd ai xcf avif");
        A("document", "doc docx xls xlsx ppt pptx pdf txt rtf odt ods odp md csv epub mobi azw3 tex djvu vsdx one");
        A("archive", "zip rar 7z tar gz tgz bz2 xz zst iso cab wim esd vhd vhdx vmdk img dmg pkg");
        A("code", "cs js mjs cjs ts tsx jsx py java cpp c h hpp go rs rb php html htm css scss json xml yml yaml sh ps1 psm1 bat cmd sql lua swift kt vue svelte map ipynb toml ini cfg props csproj sln");
        A("app", "exe dll sys msi msix appx msu so lib obj pdb node ocx drv cpl scr com");
        A("database", "db sqlite sqlite3 mdb accdb mdf ldf ndf bak dat idx");
        A("log", "log etl evtx dmp tmp");
        A("font", "ttf otf woff woff2 fon");
        return d;
    }

    public static string GroupOf(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return "noext";
        return Map.TryGetValue(ext, out var g) ? g : "other";
    }
}
