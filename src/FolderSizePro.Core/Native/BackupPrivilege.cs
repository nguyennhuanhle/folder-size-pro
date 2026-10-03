using System.Runtime.InteropServices;

namespace FolderSizePro.Native;

/// <summary>
/// UC-31: khi app ĐÃ chạy Administrator, bật SeBackupPrivilege (vốn có sẵn nhưng tắt trong token) để FILE_OPEN_FOR_BACKUP_INTENT
/// đọc được các thư mục chỉ SYSTEM được vào (System Volume Information…). Chỉ bật quyền sẵn có — không tự nâng quyền (KT-06);
/// mọi handle vẫn chỉ FILE_READ_ATTRIBUTES / FILE_LIST_DIRECTORY (Q10).
/// </summary>
internal static class BackupPrivilege
{
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }

    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValueW(string? system, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, uint len, IntPtr prev, IntPtr retLen);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();

    static int _done;

    /// <summary>Trả true nếu quyền đã bật được (ERROR_NOT_ALL_ASSIGNED = token không có quyền này → false, không lỗi).</summary>
    public static bool TryEnable()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1) return true;
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var tok)) return false;     // ADJUST_PRIVILEGES | QUERY
        try
        {
            if (!LookupPrivilegeValueW(null, "SeBackupPrivilege", out var luid)) return false;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 0x2 };                // SE_PRIVILEGE_ENABLED
            bool ok = AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            return ok && Marshal.GetLastWin32Error() == 0;
        }
        finally { NtApi.CloseHandle(tok); }
    }
}
