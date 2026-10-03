using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Native;

namespace FolderSizePro.Storage;

public enum FspFailure { NotFound, BadMagic, NewerVersion, Corrupt, Truncated }

public sealed class FspFormatException(FspFailure reason, string message) : Exception(message)
{
    public FspFailure Reason { get; } = reason;
}

/// <summary>
/// File snapshot .fsp (UC-14). Cấu trúc: "FSPS" · version · [Brotli: payload cột] · SHA-256 của mọi byte phía trước.
/// Ghi vào file tạm cùng thư mục rồi đổi tên (ER-13); đọc kiểm magic / version / SHA (ER-12).
/// </summary>
public static class FspFile
{
    public const ushort CurrentVersion = 1;
    static readonly byte[] Magic = "FSPS"u8.ToArray();

    // ---------------------------------------------------------------- ghi
    public static void Save(ScanResult r, string path, bool markPartial = false)
    {
        string full = Path.GetFullPath(path);
        string tmp = full + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            {
                fs.Write(Magic); var ver = BitConverter.GetBytes(CurrentVersion); fs.Write(ver);
                using (var br = new BrotliStream(fs, CompressionLevel.Fastest, leaveOpen: true))
                using (var w = new BinaryWriter(br, Encoding.UTF8, leaveOpen: true))
                    WritePayload(w, r, markPartial);
                fs.Flush(true);
                fs.Position = 0;
                byte[] hash = SHA256.HashData(fs);
                fs.Seek(0, SeekOrigin.End);
                fs.Write(hash);
                fs.Flush(true);
            }
            File.Move(tmp, full, true);
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    static void WritePayload(BinaryWriter w, ScanResult r, bool markPartial)
    {
        var (tree, map) = Compact(r.Tree);
        w.Write(DateTime.UtcNow.Ticks);
        w.Write(r.StartedUtc.Ticks); w.Write(r.CompletedUtc.Ticks);
        w.Write((byte)(markPartial ? Completeness.Partial : r.Completeness)); w.Write(r.ModeUsed); w.Write(r.CompactModeUsed);
        w.Write(r.Options.FastMode); w.Write(r.Options.IncludeAds); w.Write(r.Options.TopN);
        w.Write(r.HardLinkNonOwnerCount); w.Write(r.HardLinkSavedAllocated);
        w.Write(r.Notes.Count); foreach (var n in r.Notes) w.Write(n);

        w.Write(r.Roots.Count);
        foreach (var ri in r.Roots) { w.Write(map[ri.Node]); w.Write(ri.Path); w.Write(ri.IsVolumeRoot); WriteVolume(w, ri.Volume); }

        w.Write(r.Issues.Count);
        foreach (var i in r.Issues) { w.Write((byte)i.Kind); w.Write(i.Path); w.Write(i.NtStatus); w.Write(i.Message); }

        // nút: ghi theo cột để Brotli nén tốt
        int n0 = tree.Count; w.Write(n0);
        void Col<T>(Func<Node, T> get, Action<BinaryWriter, T> put) { for (int i = 0; i < n0; i++) put(w, get(tree[i])); }
        Col(n => n.Parent, (b, v) => b.Write(v)); Col(n => n.FirstChild, (b, v) => b.Write(v)); Col(n => n.ChildCount, (b, v) => b.Write(v));
        Col(n => n.NameLen, (b, v) => b.Write(v)); Col(n => n.LinkCount, (b, v) => b.Write(v)); Col(n => n.FlagsRaw, (b, v) => b.Write(v));
        Col(n => n.FileCount, (b, v) => b.Write(v)); Col(n => n.DirCount, (b, v) => b.Write(v));
        Col(n => n.Logical, (b, v) => b.Write(v)); Col(n => n.Allocated, (b, v) => b.Write(v)); Col(n => n.Resident, (b, v) => b.Write(v));
        Col(n => n.OwnLogical, (b, v) => b.Write(v)); Col(n => n.OwnAllocated, (b, v) => b.Write(v)); Col(n => n.OwnResident, (b, v) => b.Write(v));
        Col(n => n.MTime, (b, v) => b.Write(v));
        var sb = new StringBuilder();
        for (int i = 0; i < n0; i++) sb.Append(tree.Name(i));
        w.Write(sb.ToString());

        w.Write(r.TopFiles.Count); foreach (var f in r.TopFiles) { w.Write(map[f.Node]); w.Write(f.Logical); w.Write(f.Allocated); }
        w.Write(r.TopDirs.Count); foreach (var f in r.TopDirs) { w.Write(map[f.Node]); w.Write(f.DirectFiles); w.Write(f.DirectAllocated); }
        w.Write(r.TypeStats.Count); foreach (var t in r.TypeStats) { w.Write(t.Extension); w.Write(t.Group); w.Write(t.Count); w.Write(t.Logical); w.Write(t.Allocated); }
        w.Write(r.Consistency.Count); foreach (var c in r.Consistency) { w.Write(c.Name); w.Write(c.Expected); w.Write(c.Actual); w.Write(c.Ok); }
        w.Write(r.Reconcile.Count);
        foreach (var rc in r.Reconcile)
        {
            WriteVolume(w, rc.Volume); w.Write(rc.RootPath); w.Write(rc.Used); w.Write(rc.TreeAllocated); w.Write(rc.ExtraAccounted); w.Write(rc.InaccessibleDirs); w.Write(rc.MeasuredUtc.Ticks);
            w.Write(rc.Rows.Count);
            foreach (var row in rc.Rows) { w.Write(row.Key); w.Write(row.Label); w.Write(row.Bytes.HasValue); w.Write(row.Bytes ?? 0); w.Write((byte)row.Status); w.Write(row.Detail); }
            w.Write(rc.Hints.Count); foreach (var h in rc.Hints) w.Write(h);
        }
        // ứng viên hard link (để quét lại nhánh / xoá vẫn tính lại được chủ sau khi mở lại snapshot)
        var cands = r.HardLinkCands.Where(c => map.ContainsKey(c.Node)).ToList();
        w.Write(cands.Count);
        foreach (var c in cands) { w.Write(c.Serial); w.Write(c.IdLo); w.Write(c.IdHi); w.Write(map[c.Node]); w.Write(c.Logical); w.Write(c.Allocated); w.Write(c.Resident); w.Write(c.Links); }
    }

    static void WriteVolume(BinaryWriter w, VolumeInfo v)
    {
        w.Write(v.RootPath); w.Write(v.DriveLetter); w.Write(v.FileSystem); w.Write(v.Label); w.Write(v.ClusterSize);
        w.Write(v.TotalBytes); w.Write(v.FreeBytes); w.Write(v.Serial); w.Write((byte)v.Kind); w.Write((sbyte)(v.IsSsd == null ? -1 : v.IsSsd.Value ? 1 : 0)); w.Write(v.MftBytes ?? -1); w.Write(v.ReservedBytes ?? -1);
    }

    static VolumeInfo ReadVolume(BinaryReader r) => new VolumeInfo()
    {
        RootPath = r.ReadString(), DriveLetter = r.ReadString(), FileSystem = r.ReadString(), Label = r.ReadString(), ClusterSize = r.ReadInt64(),
        TotalBytes = r.ReadInt64(), FreeBytes = r.ReadInt64(), Serial = r.ReadUInt64(), Kind = (VolumeKind)r.ReadByte(),
        IsSsd = r.ReadSByte() switch { -1 => null, 1 => true, _ => false },
    }.WithNtfs(r.ReadInt64(), r.ReadInt64());

    /// <summary>Bỏ nút chết và đánh số lại sao cho con của mỗi thư mục liền nhau (BFS). Trả cây mới + bản đồ chỉ số cũ → mới.</summary>
    internal static (ScanTree Tree, Dictionary<int, int> Map) Compact(ScanTree src)
    {
        var dst = new ScanTree();
        var map = new Dictionary<int, int> { [0] = 0 };
        var nw = new NameWriter(dst);
        var queue = new Queue<int>(); queue.Enqueue(0);
        ref var sr = ref src[0]; ref var dr = ref dst[0];
        dr = sr; dr.NameLen = 0;
        while (queue.Count > 0)
        {
            int s = queue.Dequeue(); int d = map[s];
            ref var sn = ref src[s];
            var live = new List<int>(sn.IsDir ? sn.ChildCount : 0);
            if (sn.IsDir) for (int k = 0; k < sn.ChildCount; k++) if (src[sn.FirstChild + k].Parent != -2) live.Add(sn.FirstChild + k);   // bỏ nút đã xoá / thay thế
            int cc = live.Count;
            if (cc == 0) { dst[d].FirstChild = 0; dst[d].ChildCount = 0; continue; }
            int first = dst.AllocRange(cc);
            dst[d].FirstChild = first; dst[d].ChildCount = cc;
            for (int k = 0; k < cc; k++)
            {
                int so = live[k], dn = first + k;
                ref var o = ref src[so]; ref var nd = ref dst[dn];
                nd = o; nd.Parent = d;
                var nm = src.Name(so); nd.NameOffset = nw.Write(nm); nd.NameLen = (ushort)nm.Length;
                map[so] = dn;
                if (o.IsDir && o.ChildCount > 0) queue.Enqueue(so);
                else { nd.FirstChild = 0; nd.ChildCount = 0; }
            }
        }
        return (dst, map);
    }

    // ---------------------------------------------------------------- đọc
    public static ScanResult Load(string path)
    {
        if (!File.Exists(path)) throw new FspFormatException(FspFailure.NotFound, $"Không tìm thấy file: {path}");
        byte[] all;
        try { all = File.ReadAllBytes(path); }
        catch (IOException ex) { throw new IOException($"Không đọc được {path}: {ex.Message}", ex); }
        if (all.Length < 4 + 2 + 32) throw new FspFormatException(FspFailure.Truncated, "File snapshot bị cắt cụt (quá ngắn).");
        if (!all.AsSpan(0, 4).SequenceEqual(Magic)) throw new FspFormatException(FspFailure.BadMagic, "Không phải file snapshot Folder Size Pro (.fsp) — sai định danh đầu file.");
        ushort ver = BitConverter.ToUInt16(all, 4);
        if (ver > CurrentVersion) throw new FspFormatException(FspFailure.NewerVersion, $"Snapshot được tạo bởi phiên bản mới hơn (định dạng v{ver}, app hiểu tới v{CurrentVersion}). Hãy cập nhật Folder Size Pro.");
        var expect = all.AsSpan(all.Length - 32, 32);
        var actual = SHA256.HashData(all.AsSpan(0, all.Length - 32));
        if (!expect.SequenceEqual(actual)) throw new FspFormatException(FspFailure.Corrupt, "File snapshot bị hỏng (sai mã SHA-256) — có thể do tải dở hoặc ghi lỗi.");
        try
        {
            using var ms = new MemoryStream(all, 6, all.Length - 6 - 32);
            using var br = new BrotliStream(ms, CompressionMode.Decompress);
            using var r = new BinaryReader(br, Encoding.UTF8);
            return ReadPayload(r);
        }
        catch (Exception ex) when (ex is not FspFormatException)
        {
            throw new FspFormatException(FspFailure.Corrupt, "File snapshot không đọc được: " + ex.Message);
        }
    }

    static ScanResult ReadPayload(BinaryReader r)
    {
        r.ReadInt64();                                     // createdUtc
        var tree = new ScanTree();
        var res = new ScanResult { Tree = tree };
        res.StartedUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc); res.CompletedUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
        res.Completeness = (Completeness)r.ReadByte(); res.ModeUsed = r.ReadString(); res.CompactModeUsed = r.ReadBoolean();
        res.Options = new ScanOptions { FastMode = r.ReadBoolean(), IncludeAds = r.ReadBoolean(), TopN = r.ReadInt32() };
        res.HardLinkNonOwnerCount = r.ReadInt64(); res.HardLinkSavedAllocated = r.ReadInt64();
        for (int i = r.ReadInt32(); i > 0; i--) res.Notes.Add(r.ReadString());

        int rc = r.ReadInt32();
        var rootNodes = new List<(int Node, string Path, bool IsVol, VolumeInfo V)>();
        for (int i = 0; i < rc; i++) rootNodes.Add((r.ReadInt32(), r.ReadString(), r.ReadBoolean(), ReadVolume(r)));
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            var kind = (IssueKind)r.ReadByte(); var p = r.ReadString(); int st = r.ReadInt32(); var msg = r.ReadString();
            res.Issues.Add(new ScanIssue(p, kind, st, msg));
        }

        int n0 = r.ReadInt32();
        if (n0 < 1) throw new InvalidDataException("Số nút không hợp lệ.");
        tree.AllocRange(n0 - 1);
        for (int i = 0; i < n0; i++) tree[i].Parent = r.ReadInt32();
        for (int i = 0; i < n0; i++) tree[i].FirstChild = r.ReadInt32();
        for (int i = 0; i < n0; i++) tree[i].ChildCount = r.ReadInt32();
        for (int i = 0; i < n0; i++) tree[i].NameLen = r.ReadUInt16();
        for (int i = 0; i < n0; i++) tree[i].LinkCount = r.ReadUInt16();
        for (int i = 0; i < n0; i++) tree[i].FlagsRaw = r.ReadUInt32();
        for (int i = 0; i < n0; i++) tree[i].FileCount = r.ReadInt32();
        for (int i = 0; i < n0; i++) tree[i].DirCount = r.ReadInt32();
        for (int i = 0; i < n0; i++) tree[i].Logical = r.ReadInt64();
        for (int i = 0; i < n0; i++) tree[i].Allocated = r.ReadInt64();
        for (int i = 0; i < n0; i++) tree[i].Resident = r.ReadInt64();
        for (int i = 0; i < n0; i++) tree[i].OwnLogical = r.ReadInt64();
        for (int i = 0; i < n0; i++) tree[i].OwnAllocated = r.ReadInt64();
        for (int i = 0; i < n0; i++) tree[i].OwnResident = r.ReadInt64();
        for (int i = 0; i < n0; i++) tree[i].MTime = r.ReadInt64();
        string names = r.ReadString();
        var nw = new NameWriter(tree);
        int pos = 0;
        for (int i = 0; i < n0; i++)
        {
            int len = tree[i].NameLen;
            if (pos + len > names.Length) throw new InvalidDataException("Kho tên không khớp.");
            if (len > 0) tree[i].NameOffset = nw.Write(names.AsSpan(pos, len));
            pos += len;
        }
        foreach (var (node, path, isVol, vol) in rootNodes)
        {
            if (node < 0 || node >= n0) throw new InvalidDataException("Gốc quét trỏ ngoài bảng nút.");
            res.Roots.Add(new RootInfo { Node = node, Path = path, IsVolumeRoot = isVol, Volume = vol });
        }

        for (int i = r.ReadInt32(); i > 0; i--) res.TopFiles.Add(new TopFile(r.ReadInt32(), r.ReadInt64(), r.ReadInt64()));
        for (int i = r.ReadInt32(); i > 0; i--) res.TopDirs.Add(new TopDir(r.ReadInt32(), r.ReadInt64(), r.ReadInt64()));
        for (int i = r.ReadInt32(); i > 0; i--) res.TypeStats.Add(new TypeStat(r.ReadString(), r.ReadString(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64()));
        for (int i = r.ReadInt32(); i > 0; i--) res.Consistency.Add(new ConsistencyItem { Name = r.ReadString(), Expected = r.ReadString(), Actual = r.ReadString(), Ok = r.ReadBoolean() });
        for (int k = r.ReadInt32(); k > 0; k--)
        {
            var vol = ReadVolume(r); string rp = r.ReadString(); long used = r.ReadInt64(), treeA = r.ReadInt64(), extra = r.ReadInt64(); int denied = r.ReadInt32(); long ticks = r.ReadInt64();
            var rows = new List<ReconcileRow>();
            for (int i = r.ReadInt32(); i > 0; i--)
            {
                string key = r.ReadString(), label = r.ReadString(); bool has = r.ReadBoolean(); long b = r.ReadInt64(); var st = (RowStatus)r.ReadByte(); string detail = r.ReadString();
                rows.Add(new ReconcileRow(key, label, has ? b : null, st, detail));
            }
            var hints = new List<string>(); for (int i = r.ReadInt32(); i > 0; i--) hints.Add(r.ReadString());
            res.Reconcile.Add(new ReconcileResult { Volume = vol, RootPath = rp, Used = used, TreeAllocated = treeA, ExtraAccounted = extra, InaccessibleDirs = denied, MeasuredUtc = new DateTime(ticks, DateTimeKind.Utc), Rows = rows, Hints = hints });
        }
        for (int i = r.ReadInt32(); i > 0; i--)
            res.HardLinkCands.Add(new Scan.HardLinkCand(r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadUInt16()));
        return res;
    }
}
