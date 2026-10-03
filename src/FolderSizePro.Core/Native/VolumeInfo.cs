using System.Runtime.InteropServices;

namespace FolderSizePro.Native;

public enum VolumeKind { Fixed, Removable, Network, Optical, Ram, Unknown }

/// <summary>Thông tin ổ chứa một đường dẫn: file system, cluster, tổng/trống, loại ổ, HDD hay SSD.</summary>
public sealed class VolumeInfo
{
    public string RootPath { get; init; } = "";       // "D:\"
    public string DriveLetter { get; init; } = "";    // "D:" hoặc "" nếu UNC
    public string FileSystem { get; init; } = "";
    public string Label { get; init; } = "";
    public long ClusterSize { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public ulong Serial { get; init; }
    public VolumeKind Kind { get; init; }
    public bool? IsSsd { get; init; }
    /// <summary>NTFS: kích thước $MFT (MftValidDataLength) và số cluster dự trữ, đọc qua FSCTL_GET_NTFS_VOLUME_DATA — không cần Administrator.</summary>
    public long? MftBytes { get; init; }
    public long? ReservedBytes { get; init; }
    public bool IsNtfs => FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
    public bool SupportsHardLinks => IsNtfs || FileSystem.Equals("ReFS", StringComparison.OrdinalIgnoreCase);

    public VolumeInfo WithNtfs(long mft, long reserved) => new()
    {
        RootPath = RootPath, DriveLetter = DriveLetter, FileSystem = FileSystem, Label = Label, ClusterSize = ClusterSize,
        TotalBytes = TotalBytes, FreeBytes = FreeBytes, Serial = Serial, Kind = Kind, IsSsd = IsSsd,
        MftBytes = mft < 0 ? null : mft, ReservedBytes = reserved < 0 ? null : reserved,
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumePathNameW(string file, [Out] char[] buf, int len);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetVolumeInformationW(string root, [Out] char[] label, int labelLen, out uint serial, out uint maxComp, out uint flags, [Out] char[] fs, int fsLen);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetDiskFreeSpaceW(string root, out uint spc, out uint bps, out uint free, out uint total);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetDiskFreeSpaceExW(string root, out long freeToCaller, out long total, out long totalFree);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetDriveTypeW(string root);

    public static VolumeInfo For(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        var buf = new char[1024];
        string root;
        if (GetVolumePathNameW(full, buf, buf.Length)) root = new string(buf).TrimEnd('\0');
        else root = System.IO.Path.GetPathRoot(full) ?? full;
        if (!root.EndsWith('\\')) root += "\\";

        var label = new char[261]; var fs = new char[261];
        string fsName = "", lab = "";
        uint serial = 0;
        if (GetVolumeInformationW(root, label, label.Length, out serial, out _, out _, fs, fs.Length))
        { fsName = new string(fs).TrimEnd('\0'); lab = new string(label).TrimEnd('\0'); }

        long cluster = 0;
        if (GetDiskFreeSpaceW(root, out var spc, out var bps, out _, out _)) cluster = (long)spc * bps;
        long total = 0, free = 0;
        GetDiskFreeSpaceExW(root, out _, out total, out free);

        var kind = GetDriveTypeW(root) switch
        {
            3 => VolumeKind.Fixed, 2 => VolumeKind.Removable, 4 => VolumeKind.Network, 5 => VolumeKind.Optical, 6 => VolumeKind.Ram, _ => VolumeKind.Unknown,
        };
        string letter = root.Length >= 2 && root[1] == ':' ? root[..2] : "";
        bool? ssd = kind == VolumeKind.Fixed && letter != "" ? QuerySsd(letter) : null;
        long? mft = null, reserved = null;
        if (fsName.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) QueryNtfs(root, cluster, out mft, out reserved);
        return new VolumeInfo
        {
            RootPath = root, DriveLetter = letter, FileSystem = fsName, Label = lab, ClusterSize = cluster,
            TotalBytes = total, FreeBytes = free, Serial = serial, Kind = kind, IsSsd = ssd, MftBytes = mft, ReservedBytes = reserved,
        };
    }

    public static List<VolumeInfo> ListAll()
    {
        var list = new List<VolumeInfo>();
        foreach (var d in System.IO.DriveInfo.GetDrives())
        {
            try { if (d.IsReady || d.DriveType == System.IO.DriveType.Network) list.Add(For(d.RootDirectory.FullName)); } catch { }
        }
        return list;
    }

    static unsafe void QueryNtfs(string root, long cluster, out long? mft, out long? reserved)
    {
        mft = null; reserved = null;
        var h = NtApi.CreateFileW(root, NtApi.FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, NtApi.OPEN_EXISTING, NtApi.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h == NtApi.INVALID_HANDLE_VALUE) return;
        try
        {
            byte* b = stackalloc byte[128];
            if (NtApi.DeviceIoControl(h, 0x90064, null, 0, b, 128, out var ret, IntPtr.Zero) && ret >= 96)
            {
                reserved = *(long*)(b + 32) * cluster;
                mft = *(long*)(b + 56);
            }
        }
        finally { NtApi.CloseHandle(h); }
    }

    /// <summary>true = SSD (không có seek penalty), false = HDD, null = không rõ. Mở handle KHÔNG quyền → không cần Administrator.</summary>
    static unsafe bool? QuerySsd(string letter)
    {
        var h = NtApi.CreateFileW(@"\\.\" + letter, 0, 7, IntPtr.Zero, NtApi.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == NtApi.INVALID_HANDLE_VALUE) return null;
        try
        {
            // STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceSeekPenaltyProperty (7), QueryType = PropertyStandardQuery (0) }
            byte* q = stackalloc byte[12]; for (int i = 0; i < 12; i++) q[i] = 0; *(int*)q = 7;
            byte* o = stackalloc byte[16];
            if (NtApi.DeviceIoControl(h, 0x2D1400, q, 12, o, 16, out var ret, IntPtr.Zero) && ret >= 9)
                return *(byte*)(o + 8) == 0;   // DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty
            return null;
        }
        finally { NtApi.CloseHandle(h); }
    }
}
