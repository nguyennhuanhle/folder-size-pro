using System.ComponentModel;
using System.Diagnostics;

namespace FolderSizePro.Safety;

public enum RelaunchResult { Started, Cancelled, Failed }

/// <summary>
/// KT-06: đường nâng quyền DUY NHẤT của app — chỉ gọi khi người dùng bấm nút (UC-20), qua hộp UAC của Windows.
/// ER-14: người dùng từ chối UAC → ERROR_CANCELLED (1223) → giữ nguyên phiên quyền thường.
/// </summary>
public static class UacRelaunch
{
    public static RelaunchResult Run(string exePath, string arguments, out string? error)
    {
        error = null;
        try
        {
            Process.Start(new ProcessStartInfo(exePath, arguments) { Verb = "runas", UseShellExecute = true });
            return RelaunchResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return RelaunchResult.Cancelled; }
        catch (Exception ex) { error = ex.Message; return RelaunchResult.Failed; }
    }
}
