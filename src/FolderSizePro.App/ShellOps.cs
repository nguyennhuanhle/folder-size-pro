using System.Runtime.InteropServices;

namespace FolderSizePro.App;

internal static class ShellOps
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize; public uint fMask; public IntPtr hwnd; public string lpVerb; public string lpFile; public string? lpParameters; public string? lpDirectory;
        public int nShow; public IntPtr hInstApp; public IntPtr lpIDList; public string? lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon; public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO info);

    /// <summary>Mở hộp thoại Properties của Windows cho một đường dẫn.</summary>
    public static void ShowProperties(string path)
    {
        var sei = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), fMask = 0x0000000C /* INVOKEIDLIST */ | 0x00000100 /* NOASYNC? */, lpVerb = "properties", lpFile = path, nShow = 5 };
        ShellExecuteExW(ref sei);
    }
}
