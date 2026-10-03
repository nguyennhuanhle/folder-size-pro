using System.Runtime.CompilerServices;

namespace FolderSizePro.Model;

/// <summary>
/// Kho nút dạng khối (chunk) + kho tên UTF-16. Thiết kế để cả ổ hàng chục triệu file vẫn vừa RAM
/// và để nhiều luồng quét ghi song song không cần khoá trên đường nóng (plan 2.1).
/// </summary>
public sealed class ScanTree
{
    const int ChunkBits = 16, ChunkSize = 1 << ChunkBits, ChunkMask = ChunkSize - 1;
    const int NameChunkBits = 20, NameChunkSize = 1 << NameChunkBits, NameChunkMask = NameChunkSize - 1;

    readonly Node[]?[] _chunks = new Node[]?[1 << 15];
    readonly char[]?[] _names = new char[]?[1 << 11];
    readonly object _lock = new();
    int _count;
    int _nameChunks;

    public ScanTree()
    {
        AllocRange(1);                     // nút 0 = gốc ảo của phiên quét
        ref var r = ref this[0];
        r.Parent = -1; r.Flags = NodeFlags.Dir;
    }

    /// <summary>Số nút đã cấp phát (kể cả nút chết).</summary>
    public int Count => Volatile.Read(ref _count);

    public ref Node this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _chunks[i >> ChunkBits]![i & ChunkMask];
    }

    /// <summary>Cấp một dải n nút liền nhau, đã zero. Trả chỉ số đầu.</summary>
    public int AllocRange(int n)
    {
        if (n <= 0) return Count;
        int start = Interlocked.Add(ref _count, n) - n;
        for (int c = start >> ChunkBits, last = (start + n - 1) >> ChunkBits; c <= last; c++)
        {
            if (Volatile.Read(ref _chunks[c]) != null) continue;
            lock (_lock) { _chunks[c] ??= new Node[ChunkSize]; }
        }
        return start;
    }

    /// <summary>Cấp một khối tên riêng cho một luồng (mỗi luồng ghi vào khối của mình, không khoá).</summary>
    internal int NewNameChunk()
    {
        int id = Interlocked.Increment(ref _nameChunks) - 1;
        if (id >= _names.Length) throw new OutOfMemoryException("Kho tên đầy");
        _names[id] = new char[NameChunkSize];
        return id;
    }

    internal char[] NameChunk(int id) => _names[id]!;
    internal const int NameChunkChars = NameChunkSize;

    public ReadOnlySpan<char> Name(int node)
    {
        ref var n = ref this[node];
        if (n.NameLen == 0) return default;
        return new ReadOnlySpan<char>(_names[n.NameOffset >> NameChunkBits]!, n.NameOffset & NameChunkMask, n.NameLen);
    }

    public string NameString(int node) => Name(node).ToString();

    /// <summary>Ước lượng bộ nhớ đang dùng (byte) để so với ngưỡng Chế độ gọn.</summary>
    public long ApproxBytes => (long)Count * System.Runtime.InteropServices.Marshal.SizeOf<Node>() + (long)Volatile.Read(ref _nameChunks) * NameChunkSize * 2;

    /// <summary>Đường dẫn đầy đủ. Nút con trực tiếp của gốc ảo mang tên = đường dẫn gốc quét.</summary>
    public string FullPath(int node)
    {
        if (node <= 0) return "";
        var stack = new List<int>(16);
        for (int i = node; i > 0; i = this[i].Parent) stack.Add(i);
        var sb = new System.Text.StringBuilder(128);
        for (int k = stack.Count - 1; k >= 0; k--)
        {
            var nm = Name(stack[k]);
            if (sb.Length > 0 && sb[^1] != '\\') sb.Append('\\');
            sb.Append(nm);
        }
        return sb.ToString();
    }

    public int ParentOf(int node) => this[node].Parent;

    /// <summary>Con của một thư mục (bỏ qua nút chết).</summary>
    public IEnumerable<int> Children(int node)
    {
        ref var n = ref this[node];
        int f = n.FirstChild, c = n.ChildCount;
        for (int i = 0; i < c; i++) yield return f + i;
    }
}

/// <summary>Ghi tên vào khối riêng của một luồng.</summary>
internal sealed class NameWriter
{
    readonly ScanTree _tree;
    int _chunk = -1, _pos = ScanTree.NameChunkChars;
    char[]? _buf;
    public NameWriter(ScanTree tree) => _tree = tree;

    public int Write(ReadOnlySpan<char> name)
    {
        if (name.Length > ushort.MaxValue) name = name[..ushort.MaxValue];
        if (_pos + name.Length > ScanTree.NameChunkChars)
        {
            _chunk = _tree.NewNameChunk(); _buf = _tree.NameChunk(_chunk); _pos = 0;
        }
        name.CopyTo(_buf!.AsSpan(_pos));
        int off = (_chunk << 20) | _pos;
        _pos += name.Length;
        return off;
    }
}
