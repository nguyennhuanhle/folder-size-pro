using FolderSizePro.Model;

namespace FolderSizePro.Analysis;

/// <summary>Sửa cây sau khi xoá vào Thùng rác (UC-13, ER-09): gỡ nút, cập nhật tổng, tính lại chủ hard link, top, loại.</summary>
public static class TreeEditor
{
    public static void RemoveNodes(ScanResult r, IEnumerable<int> nodes, string reason)
    {
        var t = r.Tree; int removed = 0;
        foreach (var n in nodes.Distinct())
        {
            if (n <= 0 || t[n].Parent == -2) continue;
            var st = new Stack<int>(); st.Push(n);
            while (st.Count > 0)
            {
                int cur = st.Pop(); ref var cn = ref t[cur];
                if (cn.IsDir) for (int k = 0; k < cn.ChildCount; k++) { int c = cn.FirstChild + k; if (t[c].Parent != -2) st.Push(c); }
                cn.Parent = -2; cn.Logical = cn.Allocated = cn.Resident = cn.OwnLogical = cn.OwnAllocated = cn.OwnResident = 0; cn.FileCount = cn.DirCount = 0;
            }
            removed++;
        }
        if (removed == 0) return;
        r.HardLinkCands.RemoveAll(c => t[c.Node].Parent == -2);
        Finalizer.RunRescan(r, new List<Scan.HardLinkCand>());
        r.Notes.Add($"{reason} ({removed} mục) lúc {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
    }
}
