using System.Runtime.InteropServices;

namespace FolderSizePro.Scan;

/// <summary>Một bản ghi MFT sau khi parse (chỉ giữ số liệu cần — không giữ nội dung dữ liệu thường trú: KT-22, Q10).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MftRec
{
    public byte State;              // 0 = không dùng / hỏng · 1 = bản ghi gốc đang dùng · 2 = bản ghi mở rộng (extension)
    public byte Flags;              // bit0: thư mục
    public ushort Seq;
    public ushort Links;            // LinkCount trong header (số tên Win32/POSIX)
    public ushort Unit;             // chỉ số ChunkOut chứa danh sách tên / luồng của bản ghi này
    public uint FileAttr;           // từ $STANDARD_INFORMATION
    public uint ReparseTag;
    public long MTime;
    public long IndexClusters;      // byte của $INDEX_ALLOCATION (thư mục)
    public long OtherClusters;      // byte của mọi attribute phi cư trú KHÁC $DATA / $INDEX_ALLOCATION (để đối chiếu với $Bitmap)
    public int NameStart, NameCount, StreamStart, StreamCount;
    public ulong BaseRef;           // bản ghi mở rộng: tham chiếu bản ghi gốc
    public int NextExt;             // danh sách liên kết các bản ghi mở rộng của một bản ghi gốc (-1 = hết)
    public bool Corrupt;
    public long ReparseLcn;         // $REPARSE_POINT phi cư trú: LCN của cluster đầu (để đọc thẻ thật), -1 nếu không có
}

internal struct MftName { public ulong ParentRef; public int PoolOffset; public byte Len, Type; }

internal struct MftStream
{
    public int NameOffset; public byte NameLen;
    public long Size;               // DataSize (hợp lệ khi StartVcn == 0 hoặc thường trú)
    public long Clusters;           // byte do các run không-thưa chiếm
    public long Resident;           // byte thường trú đã làm tròn 8
    public bool HasSize;            // bản ghi này chứa VCN 0 (hoặc thường trú) → có kích thước
    public ushort AttrFlags;
}

internal sealed class MftChunkOut
{
    public readonly List<MftName> Names = new();
    public readonly List<MftStream> Streams = new();
    public char[] Pool = new char[4096]; public int PoolLen;

    public int AddChars(ReadOnlySpan<char> s)
    {
        if (PoolLen + s.Length > Pool.Length) Array.Resize(ref Pool, Math.Max(Pool.Length * 2, PoolLen + s.Length));
        s.CopyTo(Pool.AsSpan(PoolLen)); int off = PoolLen; PoolLen += s.Length; return off;
    }
}

/// <summary>
/// Parser bản ghi MFT NTFS: áp dụng fixup (update sequence array), đọc $STANDARD_INFORMATION, $FILE_NAME (mỗi cái = một hard link),
/// $DATA (luồng chính + ADS; Trên đĩa = tổng các run KHÔNG thưa), $INDEX_ALLOCATION, $REPARSE_POINT; mọi attribute phi cư trú khác
/// được cộng vào OtherClusters để đối chiếu với $Bitmap. Không tin dữ liệu: mọi offset đều kiểm tra biên.
/// </summary>
internal static unsafe class MftParser
{
    public const uint AttrSI = 0x10, AttrList = 0x20, AttrName = 0x30, AttrData = 0x80, AttrIndexRoot = 0x90, AttrIndexAlloc = 0xA0, AttrBitmap = 0xB0, AttrReparse = 0xC0;

    /// <summary>Hoàn nguyên 2 byte cuối mỗi sector 512 byte. false = bản ghi hỏng.</summary>
    public static bool ApplyFixups(byte* rec, int frs)
    {
        if (rec[0] != 'F' || rec[1] != 'I' || rec[2] != 'L' || rec[3] != 'E') return false;
        int usOff = *(ushort*)(rec + 4), usCount = *(ushort*)(rec + 6);
        if (usCount < 2 || usOff < 42 || usOff + usCount * 2 > frs || usCount - 1 > frs / 512) return false;
        ushort usn = *(ushort*)(rec + usOff);
        for (int i = 1; i < usCount; i++)
        {
            int end = i * 512 - 2;
            if (end + 2 > frs) return false;
            if (*(ushort*)(rec + end) != usn) return false;
            *(ushort*)(rec + end) = *(ushort*)(rec + usOff + 2 * i);
        }
        return true;
    }

    /// <summary>Parse một bản ghi đã qua fixup. Ghi vào r; danh sách tên / luồng thêm vào co.</summary>
    public static void Parse(byte* rec, int frs, int cluster, MftChunkOut co, ushort unit, ref MftRec r)
    {
        r = default; r.NextExt = -1; r.Unit = unit; r.ReparseLcn = -1;
        ushort flags = *(ushort*)(rec + 22);
        if ((flags & 1) == 0) return;                                        // không dùng
        uint used = *(uint*)(rec + 24);
        if (used < 48 || used > frs) { r.Corrupt = true; return; }
        r.Seq = *(ushort*)(rec + 16); r.Links = *(ushort*)(rec + 18);
        r.Flags = (byte)((flags & 2) != 0 ? 1 : 0);
        r.BaseRef = *(ulong*)(rec + 32);
        r.State = (byte)(r.BaseRef == 0 ? 1 : 2);
        r.NameStart = co.Names.Count; r.StreamStart = co.Streams.Count;

        int off = *(ushort*)(rec + 20);
        while (off + 16 <= used)
        {
            uint type = *(uint*)(rec + off);
            if (type == 0xFFFFFFFF) break;
            uint len = *(uint*)(rec + off + 4);
            if (len < 16 || len > used - off || (len & 7) != 0) { r.Corrupt = true; break; }
            byte* a = rec + off;
            bool nonRes = a[8] != 0; int nameLen = a[9]; int nameOff = *(ushort*)(a + 10); ushort aflags = *(ushort*)(a + 12);
            if (nameOff + nameLen * 2 > len && nameLen > 0) { r.Corrupt = true; break; }

            if (!nonRes)
            {
                uint vlen = *(uint*)(a + 16); int voff = *(ushort*)(a + 20);
                if (voff + vlen > len) { r.Corrupt = true; break; }
                byte* v = a + voff;
                switch (type)
                {
                    case AttrSI:
                        if (vlen >= 48) { r.MTime = *(long*)(v + 8); r.FileAttr = *(uint*)(v + 32); }
                        break;
                    case AttrName:
                        if (vlen >= 66)
                        {
                            byte nlen = v[64], ntype = v[65];
                            // thẻ reparse cũng được NTFS chép vào $FILE_NAME (offset 60) — cần khi $REPARSE_POINT là phi cư trú (placeholder đám mây)
                            if ((*(uint*)(v + 56) & 0x400) != 0 && r.ReparseTag == 0) r.ReparseTag = *(uint*)(v + 60);
                            if (66 + nlen * 2 <= vlen && ntype != 2)
                                co.Names.Add(new MftName { ParentRef = *(ulong*)v, PoolOffset = co.AddChars(new ReadOnlySpan<char>(v + 66, nlen)), Len = nlen, Type = ntype });
                        }
                        break;
                    case AttrData:
                        co.Streams.Add(new MftStream
                        {
                            NameOffset = nameLen > 0 ? co.AddChars(new ReadOnlySpan<char>(a + nameOff, nameLen)) : 0, NameLen = (byte)nameLen,
                            Size = vlen, Resident = vlen == 0 ? 0 : (vlen + 7) & ~7L, Clusters = 0, HasSize = true, AttrFlags = aflags,
                        });
                        break;
                    case AttrReparse:
                        if (vlen >= 4) r.ReparseTag = *(uint*)v;
                        break;
                }
            }
            else
            {
                if (len < 64) { r.Corrupt = true; break; }
                long startVcn = *(long*)(a + 16); int runOff = *(ushort*)(a + 32); long dataSize = *(long*)(a + 48);
                long clusters = 0;
                if (runOff >= 64 && runOff <= len) clusters = RunBytes(a + runOff, a + len, cluster);
                switch (type)
                {
                    case AttrData:
                        co.Streams.Add(new MftStream
                        {
                            NameOffset = nameLen > 0 ? co.AddChars(new ReadOnlySpan<char>(a + nameOff, nameLen)) : 0, NameLen = (byte)nameLen,
                            Size = dataSize, Clusters = clusters, Resident = 0, HasSize = startVcn == 0, AttrFlags = aflags,
                        });
                        break;
                    case AttrIndexAlloc: r.IndexClusters += clusters; break;
                    case AttrReparse:
                        r.OtherClusters += clusters;
                        if (runOff >= 64 && runOff <= len && *(long*)(a + 16) == 0)
                            foreach (var run in DecodeRuns(a + runOff, a + len)) if (run.Lcn >= 0) { r.ReparseLcn = run.Lcn; break; }
                        break;
                    default: r.OtherClusters += clusters; break;
                }
            }
            off += (int)len;
        }
        r.NameCount = co.Names.Count - r.NameStart; r.StreamCount = co.Streams.Count - r.StreamStart;
    }

    /// <summary>Tổng byte của các run KHÔNG thưa (đúng bằng cluster thật sự được cấp phát, như FSCTL_GET_RETRIEVAL_POINTERS).</summary>
    public static long RunBytes(byte* p, byte* end, int cluster)
    {
        long total = 0;
        while (p < end && *p != 0)
        {
            int lenSize = *p & 0xF, offSize = *p >> 4; p++;
            if (lenSize == 0 || lenSize > 8 || offSize > 8 || p + lenSize + offSize > end) break;
            long len = 0; for (int i = 0; i < lenSize; i++) len |= (long)p[i] << (8 * i);
            p += lenSize + offSize;
            if (offSize != 0) total += len * cluster;          // offSize == 0 → run thưa (sparse), không chiếm cluster
        }
        return total;
    }

    /// <summary>Giải mã đầy đủ run list → (vcn, lcn, số cluster); lcn = -1 với run thưa.</summary>
    public static List<(long Vcn, long Lcn, long Len)> DecodeRuns(byte* p, byte* end)
    {
        var list = new List<(long, long, long)>(); long vcn = 0, lcn = 0;
        while (p < end && *p != 0)
        {
            int lenSize = *p & 0xF, offSize = *p >> 4; p++;
            if (lenSize == 0 || lenSize > 8 || offSize > 8 || p + lenSize + offSize > end) break;
            long len = 0; for (int i = 0; i < lenSize; i++) len |= (long)p[i] << (8 * i);
            p += lenSize;
            if (offSize == 0) list.Add((vcn, -1, len));
            else
            {
                long delta = 0; for (int i = 0; i < offSize; i++) delta |= (long)p[i] << (8 * i);
                if ((p[offSize - 1] & 0x80) != 0) delta -= 1L << (8 * offSize);       // dấu
                lcn += delta; list.Add((vcn, lcn, len));
            }
            p += offSize; vcn += len;
        }
        return list;
    }

    /// <summary>Tìm run list của một attribute phi cư trú không tên theo loại, trong một bản ghi đã fixup (dùng cho $MFT và $Bitmap).</summary>
    public static List<(long Vcn, long Lcn, long Len)>? FindRuns(byte* rec, int frs, uint wantType, out long dataSize)
    {
        dataSize = 0;
        uint used = *(uint*)(rec + 24); if (used > frs) return null;
        int off = *(ushort*)(rec + 20);
        while (off + 16 <= used)
        {
            uint type = *(uint*)(rec + off); if (type == 0xFFFFFFFF) break;
            uint len = *(uint*)(rec + off + 4); if (len < 16 || len > used - off) return null;
            byte* a = rec + off;
            if (type == wantType && a[8] != 0 && a[9] == 0 && *(long*)(a + 16) == 0)
            {
                dataSize = *(long*)(a + 48);
                int runOff = *(ushort*)(a + 32);
                return DecodeRuns(a + runOff, a + len);
            }
            off += (int)len;
        }
        return null;
    }
}
