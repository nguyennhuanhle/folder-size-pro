using System.Runtime.InteropServices;
using System.Text;
using FolderSizePro.Native;

namespace FolderSizePro.Analysis;

public sealed record StreamDetail(string Name, long Size, long Allocated);

/// <summary>UC-10: chi tiết một mục, hỏi trực tiếp hệ thống (luôn mới, không lưu trong arena). Chỉ mở handle FILE_READ_ATTRIBUTES (Q4, Q10).</summary>
public sealed unsafe class NodeDetails
{
    public string Path { get; private set; } = "";
    public bool Exists { get; private set; }
    public string? Error { get; private set; }
    public bool IsDir { get; private set; }
    public long EndOfFile { get; private set; }
    public long AllocationSize { get; private set; }
    public uint Links { get; private set; }
    public DateTime? Created { get; private set; }
    public DateTime? Accessed { get; private set; }
    public DateTime? Modified { get; private set; }
    public uint Attributes { get; private set; }
    public long ClusterSize { get; private set; }
    public string? ReparseTarget { get; private set; }
    public uint ReparseTag { get; private set; }
    public string? Owner { get; private set; }
    public List<StreamDetail> Streams { get; } = new();
    public List<string> OtherLinkPaths { get; } = new();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr FindFirstFileNameW(string file, uint flags, ref uint len, [Out] char[] name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool FindNextFileNameW(IntPtr h, ref uint len, [Out] char[] name);
    [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);

    public static NodeDetails Load(string path)
    {
        var d = new NodeDetails { Path = path };
        try
        {
            var vol = VolumeInfo.For(path); d.ClusterSize = vol.ClusterSize;
            int st = NtApi.Open(NtApi.ToNtPath(NtApi.ToExtendedPath(path)), IntPtr.Zero, NtApi.FILE_READ_ATTRIBUTES | NtApi.SYNCHRONIZE,
                NtApi.FILE_SYNCHRONOUS_IO_NONALERT | NtApi.FILE_OPEN_FOR_BACKUP_INTENT | NtApi.FILE_OPEN_REPARSE_POINT, out var h);
            if (st < 0) { d.Error = NtApi.StatusText(st); return d; }
            d.Exists = true;
            try
            {
                NtApi.FILE_BASIC_INFORMATION bi; NtApi.FILE_STANDARD_INFORMATION si;
                if (NtApi.NtQueryInformationFile(h, out _, &bi, (uint)sizeof(NtApi.FILE_BASIC_INFORMATION), NtApi.FileBasicInformation) >= 0)
                {
                    d.Created = Ft(bi.CreationTime); d.Accessed = Ft(bi.LastAccessTime); d.Modified = Ft(bi.LastWriteTime); d.Attributes = bi.FileAttributes;
                    d.IsDir = (bi.FileAttributes & NtApi.ATTR_DIRECTORY) != 0;
                }
                if (NtApi.NtQueryInformationFile(h, out _, &si, (uint)sizeof(NtApi.FILE_STANDARD_INFORMATION), NtApi.FileStandardInformation) >= 0)
                { d.EndOfFile = si.EndOfFile; d.AllocationSize = si.AllocationSize; d.Links = si.NumberOfLinks; }
                NtApi.FILE_ATTRIBUTE_TAG_INFORMATION ti;
                if ((d.Attributes & NtApi.ATTR_REPARSE_POINT) != 0 &&
                    NtApi.NtQueryInformationFile(h, out _, &ti, (uint)sizeof(NtApi.FILE_ATTRIBUTE_TAG_INFORMATION), NtApi.FileAttributeTagInformation) >= 0)
                    d.ReparseTag = ti.ReparseTag;
                if (!d.IsDir)
                {
                    var buf = (byte*)NativeMemory.Alloc(64 * 1024);
                    try
                    {
                        if (NtApi.NtQueryInformationFile(h, out _, buf, 64 * 1024, NtApi.FileStreamInformation) >= 0)
                            for (byte* p = buf; ;)
                            {
                                uint next = *(uint*)p, nl = *(uint*)(p + 4); long sz = *(long*)(p + 8), al = *(long*)(p + 16);
                                string nm = new((char*)(p + 24), 0, (int)nl / 2);
                                d.Streams.Add(new StreamDetail(nm == "::$DATA" ? "(dữ liệu chính)" : nm.Replace(":$DATA", ""), sz, al));
                                if (next == 0) break; p += next;
                            }
                    }
                    finally { NativeMemory.Free(buf); }
                }
            }
            finally { NtApi.NtClose(h); }

            if (d.Links > 1 && !d.IsDir) d.LoadLinks(vol);
            if ((d.Attributes & NtApi.ATTR_REPARSE_POINT) != 0) d.ReparseTarget = ReadReparseTarget(path);
            try
            {
                var sec = d.IsDir ? (object)new DirectoryInfo(NtApi.ToExtendedPath(path)).GetAccessControl() : new FileInfo(NtApi.ToExtendedPath(path)).GetAccessControl();
                d.Owner = (sec as System.Security.AccessControl.FileSystemSecurity)?.GetOwner(typeof(System.Security.Principal.NTAccount))?.ToString();
            }
            catch { d.Owner = null; }
        }
        catch (Exception ex) { d.Error = ex.Message; }
        return d;
    }

    static DateTime? Ft(long ft) => ft > 0 ? DateTime.FromFileTime(ft) : null;

    void LoadLinks(VolumeInfo vol)
    {
        var buf = new char[32768]; uint len = (uint)buf.Length;
        var h = FindFirstFileNameW(NtApi.ToExtendedPath(Path), 0, ref len, buf);
        if (h == NtApi.INVALID_HANDLE_VALUE) return;
        try
        {
            do
            {
                string rel = new string(buf, 0, (int)len).TrimEnd('\0');
                string full = (vol.DriveLetter != "" ? vol.DriveLetter : vol.RootPath.TrimEnd('\\')) + rel;
                if (!full.Equals(Path, StringComparison.OrdinalIgnoreCase)) OtherLinkPaths.Add(full);
                len = (uint)buf.Length;
            } while (FindNextFileNameW(h, ref len, buf));
        }
        finally { FindClose(h); }
    }

    static string? ReadReparseTarget(string path)
    {
        var h = NtApi.CreateFileW(NtApi.ToExtendedPath(path), NtApi.FILE_READ_ATTRIBUTES, 7, IntPtr.Zero, NtApi.OPEN_EXISTING,
            NtApi.FILE_FLAG_BACKUP_SEMANTICS | NtApi.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (h == NtApi.INVALID_HANDLE_VALUE) return null;
        try
        {
            var buf = (byte*)NativeMemory.Alloc(16 * 1024);
            try
            {
                if (!NtApi.DeviceIoControl(h, 0x900A8, null, 0, buf, 16 * 1024, out _, IntPtr.Zero)) return null;
                uint tag = *(uint*)buf;
                int hdr = tag == NtApi.IO_REPARSE_TAG_SYMLINK ? 20 : tag == NtApi.IO_REPARSE_TAG_MOUNT_POINT ? 16 : -1;
                if (hdr < 0) return $"(reparse 0x{tag:X8})";
                ushort subOff = *(ushort*)(buf + 8), subLen = *(ushort*)(buf + 10), prOff = *(ushort*)(buf + 12), prLen = *(ushort*)(buf + 14);
                string print = new((char*)(buf + hdr + prOff), 0, prLen / 2);
                string sub = new((char*)(buf + hdr + subOff), 0, subLen / 2);
                return print.Length > 0 ? print : sub;
            }
            finally { NativeMemory.Free(buf); }
        }
        finally { NtApi.CloseHandle(h); }
    }
}
