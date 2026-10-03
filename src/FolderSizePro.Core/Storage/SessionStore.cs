using FolderSizePro.Text;
using System.Text.Json;
using FolderSizePro.Analysis;
using FolderSizePro.Model;
using FolderSizePro.Scan;

namespace FolderSizePro.Storage;

public sealed record InterruptedSession(string[] Roots, DateTime StartedUtc, bool HasPartial);

/// <summary>
/// UC-53 / ER-15: marker "đang quét" + tự lưu kết quả dở dang mỗi 60 giây. Nếu app thoát đột ngột, lần mở sau hỏi có khôi phục không.
/// Mọi file nằm trong %LOCALAPPDATA%\FolderSizePro\session (KT-43). Không ghi được → bỏ qua im lặng (ER-42 đã có banner).
/// </summary>
public static class SessionStore
{
    static string Marker => Path.Combine(AppPaths.Session, "scanning.json");
    static string Partial => Path.Combine(AppPaths.Session, "partial.fsp");
    static int _saving;

    sealed record MarkerData(string[] Roots, DateTime StartedUtc);

    public static void MarkScanning(IEnumerable<string> roots)
    {
        if (!AppPaths.Writable) return;
        try
        {
            Directory.CreateDirectory(AppPaths.Session);
            File.WriteAllText(Marker, JsonSerializer.Serialize(new MarkerData(roots.ToArray(), DateTime.UtcNow)));
            if (File.Exists(Partial)) File.Delete(Partial);
        }
        catch { }
    }

    public static void Clear()
    {
        try { if (File.Exists(Marker)) File.Delete(Marker); if (File.Exists(Partial)) File.Delete(Partial); } catch { }
    }

    public static void Autosave(ScanResult r, IScanJob job)
    {
        if (!AppPaths.Writable || Interlocked.Exchange(ref _saving, 1) == 1) return;
        Task.Run(() =>
        {
            try { FspFile.Save(r, Partial, markPartial: true); }
            catch (Exception ex) { Log.Warn("Autosave phiên thất bại: " + ex.Message); }
            finally { Interlocked.Exchange(ref _saving, 0); }
        });
    }

    public static InterruptedSession? Check()
    {
        try
        {
            if (!File.Exists(Marker)) return null;
            var m = JsonSerializer.Deserialize<MarkerData>(File.ReadAllText(Marker));
            if (m == null) return null;
            return new InterruptedSession(m.Roots, m.StartedUtc, File.Exists(Partial));
        }
        catch
        {
            try { File.Move(Marker, Marker + ".bad", true); } catch { }     // ER-41
            return null;
        }
    }

    /// <summary>Mở kết quả dở dang; tổng được tính lại để nhất quán (số liệu đang cập nhật khi bị lưu).</summary>
    public static ScanResult? LoadPartial()
    {
        try
        {
            var r = FspFile.Load(Partial);
            r.Completeness = Completeness.Partial;
            r.Notes.Insert(0, L.T("Khôi phục từ phiên quét bị gián đoạn — kết quả DỞ DANG; thư mục đánh dấu 'chưa quét' chưa được đo.", "Restored from an interrupted scan — result is PARTIAL; folders marked 'not scanned' were not measured."));
            Finalizer.RunRescan(r, new List<HardLinkCand>());
            return r;
        }
        catch (Exception ex) { Log.Warn("Không khôi phục được phiên: " + ex.Message); return null; }
    }
}
