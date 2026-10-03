using System.Runtime.InteropServices;

namespace FolderSizePro.Model;

[Flags]
public enum NodeFlags : uint
{
    None = 0,
    Dir = 1u << 0,
    Reparse = 1u << 1,          // junction/symlink/mount point/khác — không đi theo (Q3)
    MountPoint = 1u << 2,
    Symlink = 1u << 3,
    OtherReparse = 1u << 4,
    Cloud = 1u << 5,            // file placeholder đám mây (Q4)
    CloudOnly = 1u << 6,        // chưa tải về: trên đĩa = 0
    CloudNotListed = 1u << 7,   // thư mục RECALL_ON_OPEN: không liệt kê (Q4)
    Compressed = 1u << 8,
    Sparse = 1u << 9,
    Wof = 1u << 10,             // CompactOS
    Hidden = 1u << 11,
    System = 1u << 12,
    HardLinkNonOwner = 1u << 13,// liên kết cứng không phải chủ → không cộng (Q2)
    MultiLink = 1u << 14,       // NumberOfLinks > 1
    AccessDenied = 1u << 15,
    Changed = 1u << 16,         // thay đổi khi quét (ER-05)
    TimedOut = 1u << 17,
    Lost = 1u << 18,
    Aggregate = 1u << 19,       // nút ảo "<N file khác>" của Chế độ gọn (ER-07)
    HasAds = 1u << 20,
    Resident = 1u << 21,        // dữ liệu nằm trong MFT (0 cluster)
    Estimated = 1u << 22,       // kích thước lấy từ danh sách thư mục vì không mở được file
    Excluded = 1u << 23,
    Encrypted = 1u << 24,
    Dedup = 1u << 25,
    Unknown = 1u << 26,         // kích thước không xác định (thư mục không liệt kê được…)
    Pending = 1u << 27,         // thư mục đã thấy nhưng CHƯA quét xong (snapshot dở dang / huỷ)
}

/// <summary>
/// Một nút của cây kết quả. Con của một thư mục luôn liền nhau trong arena: [FirstChild, FirstChild+ChildCount).
/// Own* = riêng nút; Logical/Allocated/Resident/FileCount/DirCount = tổng cây con (chính thức sau Rollup).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Node
{
    public int Parent;            // -1: nút gốc ảo; -2: nút đã bị thay thế (chết)
    public int FirstChild;
    public int ChildCount;
    public int NameOffset;
    public ushort NameLen;
    public ushort LinkCount;
    public uint FlagsRaw;
    public int FileCount;
    public int DirCount;
    public long Logical;
    public long Allocated;
    public long Resident;
    public long OwnLogical;
    public long OwnAllocated;
    public long OwnResident;
    public long MTime;            // FILETIME

    public NodeFlags Flags { readonly get => (NodeFlags)FlagsRaw; set => FlagsRaw = (uint)value; }
    public readonly bool IsDir => (FlagsRaw & (uint)NodeFlags.Dir) != 0;
    public readonly bool Has(NodeFlags f) => (FlagsRaw & (uint)f) != 0;
}
