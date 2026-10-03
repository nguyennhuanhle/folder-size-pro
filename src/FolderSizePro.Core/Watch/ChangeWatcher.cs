using System.Collections.Concurrent;
using FolderSizePro.Native;

namespace FolderSizePro.Watch;

/// <summary>
/// UC-50: theo dõi thay đổi bằng cơ chế của Windows (ReadDirectoryChangesW qua FileSystemWatcher; mọi file system, không cần Administrator).
/// Gom sự kiện 500 ms → báo các thư mục bị đổi. Tràn bộ đệm → <see cref="Lost"/> (ER-40): không im lặng giữ số cũ.
/// Chỉ ĐỌC: không bao giờ ghi vào ổ đang theo dõi (KT-43).
/// </summary>
public sealed class ChangeWatcher : IDisposable
{
    readonly List<FileSystemWatcher> _w = new();
    readonly ConcurrentDictionary<string, byte> _dirty = new(StringComparer.OrdinalIgnoreCase);
    readonly Timer _timer;
    readonly int _debounceMs;
    volatile bool _lost;

    public event Action<IReadOnlyList<string>>? DirectoriesChanged;
    public event Action<string>? Lost;

    public ChangeWatcher(IEnumerable<string> roots, int debounceMs = 500)
    {
        _debounceMs = debounceMs;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        foreach (var root in roots)
        {
            try
            {
                var fsw = new FileSystemWatcher(root.EndsWith('\\') ? root : root + "\\")
                {
                    IncludeSubdirectories = true, InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Attributes | NotifyFilters.CreationTime,
                };
                fsw.Changed += (_, e) => Mark(e.FullPath, isChange: true);
                fsw.Created += (_, e) => Mark(e.FullPath, false);
                fsw.Deleted += (_, e) => Mark(e.FullPath, false);
                fsw.Renamed += (_, e) => { Mark(e.OldFullPath, false); Mark(e.FullPath, false); };
                fsw.Error += (_, e) => { _lost = true; Lost?.Invoke($"{root}: {e.GetException().Message}"); };       // ER-40
                fsw.EnableRaisingEvents = true;
                _w.Add(fsw);
            }
            catch (Exception ex) { Lost?.Invoke($"{root}: {ex.Message}"); }
        }
    }

    public bool IsLost => _lost;

    void Mark(string fullPath, bool isChange)
    {
        // thư mục bị đổi = thư mục chứa mục đó (với sự kiện Changed của chính một thư mục thì bỏ qua: nội dung không đổi)
        string? dir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(dir)) return;
        _dirty[dir] = 1;
        _timer.Change(_debounceMs, Timeout.Infinite);
    }

    void Flush()
    {
        var list = new List<string>();
        foreach (var k in _dirty.Keys) if (_dirty.TryRemove(k, out _)) list.Add(k);
        if (list.Count > 0) DirectoriesChanged?.Invoke(list);
    }

    public void Dispose()
    {
        _timer.Dispose();
        foreach (var w in _w) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
        _w.Clear();
    }
}
