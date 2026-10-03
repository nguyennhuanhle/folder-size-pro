using System.Runtime.InteropServices;

namespace FolderSizePro.Native;

/// <summary>
/// Khai báo P/Invoke tối thiểu cho ntdll/kernel32 mà bộ quét cần. Tự viết (không dùng CsWin32)
/// vì các hàm Nt* không nằm trong Win32 metadata và cần bố cục struct chính xác.
/// Mọi handle ở đây chỉ mở với FILE_READ_ATTRIBUTES / FILE_LIST_DIRECTORY — không bao giờ FILE_READ_DATA (Q4, Q10).
/// </summary>
internal static unsafe class NtApi
{
    // ---- NTSTATUS ----
    public const int STATUS_SUCCESS = 0;
    public const int STATUS_NO_MORE_FILES = unchecked((int)0x80000006);
    public const int STATUS_BUFFER_OVERFLOW = unchecked((int)0x80000005);
    public const int STATUS_ACCESS_DENIED = unchecked((int)0xC0000022);
    public const int STATUS_OBJECT_NAME_NOT_FOUND = unchecked((int)0xC0000034);
    public const int STATUS_OBJECT_PATH_NOT_FOUND = unchecked((int)0xC000003A);
    public const int STATUS_SHARING_VIOLATION = unchecked((int)0xC0000043);
    public const int STATUS_NO_SUCH_DEVICE = unchecked((int)0xC000000E);
    public const int STATUS_NO_SUCH_FILE = unchecked((int)0xC000000F);
    public const int STATUS_DELETE_PENDING = unchecked((int)0xC0000056);
    public const int STATUS_VOLUME_DISMOUNTED = unchecked((int)0xC000026E);
    public const int STATUS_DEVICE_NOT_CONNECTED = unchecked((int)0xC000009D);
    public const int STATUS_NETWORK_UNREACHABLE = unchecked((int)0xC000023C);
    public const int STATUS_BAD_NETWORK_PATH = unchecked((int)0xC00000BE);
    public const int STATUS_NETWORK_NAME_DELETED = unchecked((int)0xC00000C9);
    public const int STATUS_IO_TIMEOUT = unchecked((int)0xC00000B5);
    public const int STATUS_CANCELLED = unchecked((int)0xC0000120);
    public const int STATUS_NOT_A_DIRECTORY = unchecked((int)0xC0000103);
    public const int STATUS_INVALID_PARAMETER = unchecked((int)0xC000000D);
    public const int STATUS_UNSUCCESSFUL = unchecked((int)0xC0000001);
    public const int STATUS_NOT_SUPPORTED = unchecked((int)0xC00000BB);
    public const int STATUS_INVALID_INFO_CLASS = unchecked((int)0xC0000003);

    // ---- Access / options ----
    public const uint FILE_LIST_DIRECTORY = 0x0001;
    public const uint FILE_READ_ATTRIBUTES = 0x0080;
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint FILE_SHARE_ALL = 7;
    public const uint FILE_OPEN = 1;
    public const uint FILE_DIRECTORY_FILE = 0x00000001;
    public const uint FILE_NON_DIRECTORY_FILE = 0x00000040;
    public const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;
    public const uint FILE_OPEN_FOR_BACKUP_INTENT = 0x00004000;
    public const uint FILE_OPEN_REPARSE_POINT = 0x00200000;
    public const uint OBJ_CASE_INSENSITIVE = 0x40;

    // ---- File attributes ----
    public const uint ATTR_READONLY = 0x1;
    public const uint ATTR_HIDDEN = 0x2;
    public const uint ATTR_SYSTEM = 0x4;
    public const uint ATTR_DIRECTORY = 0x10;
    public const uint ATTR_SPARSE = 0x200;
    public const uint ATTR_REPARSE_POINT = 0x400;
    public const uint ATTR_COMPRESSED = 0x800;
    public const uint ATTR_OFFLINE = 0x1000;
    public const uint ATTR_ENCRYPTED = 0x4000;
    public const uint ATTR_RECALL_ON_OPEN = 0x40000;
    public const uint ATTR_PINNED = 0x80000;
    public const uint ATTR_UNPINNED = 0x100000;
    public const uint ATTR_RECALL_ON_DATA_ACCESS = 0x400000;

    // ---- Reparse tags ----
    public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
    public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
    public const uint IO_REPARSE_TAG_WOF = 0x80000017;
    public const uint IO_REPARSE_TAG_DEDUP = 0x80000013;
    public const uint IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;
    public const uint IO_REPARSE_TAG_CLOUD_MASK = 0x9000001A; // 0x9000001A..0x9000F01A: cloud files (mẫu 0x9000X01A)

    // ---- FILE_INFORMATION_CLASS ----
    public const int FileBasicInformation = 4;
    public const int FileStandardInformation = 5;
    public const int FileStreamInformation = 22;
    public const int FileCompressionInformation = 28;
    public const int FileAttributeTagInformation = 35;
    public const int FileIdInformation = 59;
    public const int FileIdExtdDirectoryInformation = 60;

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public UNICODE_STRING* ObjectName;
        public uint Attributes;
        public void* SecurityDescriptor;
        public void* SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    /// <summary>FILE_ID_EXTD_DIR_INFORMATION (header 88 byte, tên theo sau).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ID_EXTD_DIR_INFORMATION
    {
        public uint NextEntryOffset;
        public uint FileIndex;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public long EndOfFile;
        public long AllocationSize;
        public uint FileAttributes;
        public uint FileNameLength;
        public uint EaSize;
        public uint ReparsePointTag;
        public ulong FileIdLow;
        public ulong FileIdHigh;
        // WCHAR FileName[]  ở offset 88
    }
    public const int ExtdDirHeaderSize = 88;

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_STANDARD_INFORMATION
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_COMPRESSION_INFORMATION
    {
        public long CompressedFileSize;
        public ushort CompressionFormat;
        public byte CompressionUnitShift;
        public byte ChunkShift;
        public byte ClusterShift;
        public fixed byte Reserved[3];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_BASIC_INFORMATION
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ATTRIBUTE_TAG_INFORMATION
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_ID_INFORMATION
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [DllImport("ntdll.dll")]
    public static extern int NtCreateFile(
        out IntPtr FileHandle, uint DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes,
        out IO_STATUS_BLOCK IoStatusBlock, long* AllocationSize, uint FileAttributes,
        uint ShareAccess, uint CreateDisposition, uint CreateOptions, void* EaBuffer, uint EaLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryDirectoryFile(
        IntPtr FileHandle, IntPtr Event, IntPtr ApcRoutine, IntPtr ApcContext,
        out IO_STATUS_BLOCK IoStatusBlock, void* FileInformation, uint Length,
        int FileInformationClass, byte ReturnSingleEntry, UNICODE_STRING* FileName, byte RestartScan);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationFile(
        IntPtr FileHandle, out IO_STATUS_BLOCK IoStatusBlock, void* FileInformation,
        uint Length, int FileInformationClass);

    [DllImport("ntdll.dll")]
    public static extern int NtClose(IntPtr Handle);

    [DllImport("ntdll.dll")]
    public static extern uint RtlNtStatusToDosError(int status);

    // ---- kernel32 ----
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        void* lpInBuffer, uint nInBufferSize, void* lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

    public const uint GENERIC_READ = 0x80000000;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    public const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    public static bool IsCloudTag(uint tag) => (tag & 0xFFFF0FFF) == 0x9000001A;

    /// <summary>Chuyển đường dẫn Win32 thành dạng "\\?\" (tránh giới hạn 260 ký tự, giữ nguyên dấu cách/chấm cuối).</summary>
    public static string ToExtendedPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    /// <summary>"\\?\C:\x" → "\??\C:\x" (đường dẫn NT cho NtCreateFile).</summary>
    public static string ToNtPath(string extended)
    {
        if (extended.StartsWith(@"\\?\", StringComparison.Ordinal)) return @"\??\" + extended[4..];
        return extended;
    }

    /// <summary>Mở tuyệt đối (path NT) hoặc tương đối (root != 0, name là tên con).</summary>
    public static int Open(string ntName, IntPtr root, uint access, uint options, out IntPtr handle)
    {
        fixed (char* p = ntName)
        {
            var us = new UNICODE_STRING { Buffer = p, Length = (ushort)(ntName.Length * 2), MaximumLength = (ushort)(ntName.Length * 2) };
            var oa = new OBJECT_ATTRIBUTES
            {
                Length = sizeof(OBJECT_ATTRIBUTES), RootDirectory = root, ObjectName = &us, Attributes = OBJ_CASE_INSENSITIVE,
            };
            return NtCreateFile(out handle, access, ref oa, out _, null, 0, FILE_SHARE_ALL, FILE_OPEN, options, null, 0);
        }
    }

    /// <summary>Mở theo tên con dưới thư mục cha (không cần ghép đường dẫn dài). name không null-terminated.</summary>
    public static int OpenRelative(IntPtr parent, ReadOnlySpan<char> name, uint access, uint options, out IntPtr handle)
    {
        fixed (char* p = name)
        {
            var us = new UNICODE_STRING { Buffer = p, Length = (ushort)(name.Length * 2), MaximumLength = (ushort)(name.Length * 2) };
            var oa = new OBJECT_ATTRIBUTES
            {
                Length = sizeof(OBJECT_ATTRIBUTES), RootDirectory = parent, ObjectName = &us, Attributes = OBJ_CASE_INSENSITIVE,
            };
            return NtCreateFile(out handle, access, ref oa, out _, null, 0, FILE_SHARE_ALL, FILE_OPEN, options, null, 0);
        }
    }

    public static string StatusText(int status)
    {
        uint dos = RtlNtStatusToDosError(status);
        try { return new System.ComponentModel.Win32Exception((int)dos).Message + $" (NTSTATUS 0x{status:X8})"; }
        catch { return $"NTSTATUS 0x{status:X8}"; }
    }
}
