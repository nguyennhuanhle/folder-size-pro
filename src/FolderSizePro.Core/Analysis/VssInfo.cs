using System.Management;
using System.Runtime.InteropServices;
using FolderSizePro.Model;
using FolderSizePro.Text;

namespace FolderSizePro.Analysis;

/// <summary>
/// UC-32 / ER-22: dung lượng Shadow Copy (VSS) do hệ thống báo, qua WMI Win32_ShadowStorage (cần Administrator).
/// Lỗi / không có quyền → <see cref="Error"/> (hiện "Không xác định", KHÔNG phải 0).
/// </summary>
public sealed record VssResult(long? UsedBytes, long? AllocatedBytes, long? MaxBytes, string? Error);

public static class VssInfo
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeNameForVolumeMountPointW(string mount, [Out] char[] name, int len);

    public static VssResult Query(string volumeRoot)
    {
        try
        {
            var buf = new char[64];
            if (!GetVolumeNameForVolumeMountPointW(volumeRoot.TrimEnd('\\') + "\\", buf, buf.Length)) return new(null, null, null, L.T("Không lấy được GUID ổ.", "Cannot get the volume GUID."));
            string guid = new string(buf).TrimEnd('\0');
            int a = guid.IndexOf('{'), b = guid.IndexOf('}');
            if (a < 0 || b < 0) return new(null, null, null, L.T("GUID ổ không hợp lệ.", "Invalid volume GUID."));
            string id = guid.Substring(a, b - a + 1).ToUpperInvariant();
            long used = 0, alloc = 0, max = 0; bool found = false;
            using var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT UsedSpace, AllocatedSpace, MaxSpace, Volume FROM Win32_ShadowStorage");
            foreach (ManagementObject o in s.Get())
            {
                string vol = (o["Volume"]?.ToString() ?? "").ToUpperInvariant();
                if (!vol.Contains(id)) continue;
                found = true;
                used += Convert.ToInt64(o["UsedSpace"] ?? 0L); alloc += Convert.ToInt64(o["AllocatedSpace"] ?? 0L); max += Convert.ToInt64(o["MaxSpace"] ?? 0L);
            }
            return found ? new(used, alloc, max, null) : new(0, 0, 0, null);        // không có vùng shadow storage cho ổ này = 0 thật
        }
        catch (Exception ex) { return new(null, null, null, ex.Message); }
    }
}

/// <summary>UC-34: cho một thư mục, cộng phần "tiết kiệm" nhờ không đếm đôi liên kết cứng (Q2) — để đặt cạnh con số kiểu Explorer.</summary>
public sealed record HardLinkStats(int NonOwnerLinks, long SavedLogical, long SavedAllocated, int DistinctFiles);

public static class HardLinkInfo
{
    public static HardLinkStats ForSubtree(ScanResult r, int node)
    {
        var t = r.Tree; int n = 0; long l = 0, a = 0; var files = new HashSet<(ulong, ulong, ulong)>();
        foreach (var c in r.HardLinkCands)
        {
            if (t[c.Node].Parent == -2 || !t[c.Node].Has(NodeFlags.HardLinkNonOwner)) continue;
            bool inside = false;
            for (int p = c.Node; p > 0; p = t[p].Parent) if (p == node) { inside = true; break; }
            if (!inside) continue;
            n++; l += c.Logical; a += c.Allocated; files.Add((c.Serial, c.IdLo, c.IdHi));
        }
        return new(n, l, a, files.Count);
    }
}
