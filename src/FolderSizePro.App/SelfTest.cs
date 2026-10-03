using System.Text;
using FolderSizePro.Analysis;
using FolderSizePro.App.ViewModels;
using FolderSizePro.Model;
using FolderSizePro.Safety;
using FolderSizePro.Storage;

namespace FolderSizePro.App;

/// <summary>
/// Chạy thật ViewModel + lõi trong chính app (cờ --selftest &lt;file&gt;): thao tác như người dùng (quét, mở/đóng nhánh, sắp xếp, tìm, chi tiết,
/// quét lại nhánh, xoá vào Thùng rác, snapshot, xuất, so sánh, theo dõi trực tiếp…) rồi IN số thật — không assert. Dùng cho Phase 3/4.
/// </summary>
internal sealed class SelfTestShell : IShell
{
    public List<string> Infos { get; } = new();
    public int ChooseAnswer { get; set; } = 0;
    public bool ConfirmDeleteAnswer { get; set; } = true;
    public DeleteRequest? LastDeleteRequest { get; private set; }
    public Queue<string> SavePaths { get; } = new(); public Queue<string> OpenPaths { get; } = new();
    public List<TreeRow> Selected { get; } = new();
    public string? PickFolder() => null;
    public string? PickOpenFsp() => OpenPaths.Count > 0 ? OpenPaths.Dequeue() : null;
    public string? PickSave(string filter, string defaultName) => SavePaths.Count > 0 ? SavePaths.Dequeue() : null;
    public void Info(string title, string text) => Infos.Add($"[{title}] {text}");
    public bool Confirm(string title, string text, string ok, string cancel) => true;
    public int Choose(string title, string text, params string[] buttons) { Infos.Add($"[Choose:{title}] → nút {ChooseAnswer}"); return ChooseAnswer; }
    public bool ConfirmDelete(DeleteRequest req) { LastDeleteRequest = req; return ConfirmDeleteAnswer; }
    public void CopyText(string text) { }
    public IReadOnlyList<TreeRow> SelectedRows() => Selected;
    public IntPtr Hwnd => IntPtr.Zero;
    public void SelectTab(string key) { }
    public void ScrollRowIntoView(TreeRow row) { }
}

internal static class SelfTest
{
    static StringBuilder _o = new();
    static void P(string s = "") { _o.AppendLine(s); }
    static string N(long v) => v.ToString("N0");

    public static async Task Run(MainViewModel vm, string outFile, string fixtureRoot)
    {
        var shell = new SelfTestShell(); vm.Shell = shell;
        string fx = Path.Combine(fixtureRoot, "verify");
        try { await Body(vm, shell, fx, fixtureRoot); }
        catch (Exception ex) { P("LỖI SELFTEST: " + ex); }
        File.WriteAllText(outFile, _o.ToString(), new UTF8Encoding(true));
        Application.Current.Shutdown();
    }

    static async Task<bool> WaitUntil(Func<bool> f, int ms = 60000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!f()) { if (sw.ElapsedMilliseconds > ms) return false; await Task.Delay(50); }
        return true;
    }

    static async Task ScanAndWait(MainViewModel vm, params string[] paths)
    {
        vm.StartScan(paths);
        await WaitUntil(() => vm.IsScanning);
        await WaitUntil(() => !vm.IsScanning && vm.HasResult, 120000);
        await Task.Delay(300);
    }

    static TreeRow? Row(MainViewModel vm, string name) => vm.Rows.FirstOrDefault(r => !r.IsMore && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    static async Task Body(MainViewModel vm, SelfTestShell sh, string fx, string fixtureRoot)
    {
        // ---------------------------------------------------------------- 1. ER-01: nhập sai đường dẫn
        P("== 1. ER-01 nhập đường dẫn sai ==");
        foreach (var bad in new[] { "", "   ", @"Z:\khong-co-thu-muc", Path.Combine(fx, "sizes", "f4096.bin"), "D:\\a|b" })
        {
            vm.PathText = bad; vm.StartCommand.Execute(null);
            P($"  '{bad}' → PathError = {vm.PathError ?? "(null)"} ; IsScanning={vm.IsScanning}");
        }
        vm.PathText = "";

        // ---------------------------------------------------------------- 2. quét + cây
        P("\n== 2. Quét fixture, cây, mở/đóng, sắp xếp ==");
        await ScanAndWait(vm, fx);
        var t = vm.Tree!;
        P($"  Gốc: Trên đĩa {N(t[vm.Result!.Roots[0].Node].Allocated)}, Kích thước {N(t[vm.Result.Roots[0].Node].Logical)}; hàng hiển thị: {vm.Rows.Count} ({string.Join(", ", vm.Rows.Select(r => r.Name))})");
        P($"  Tổng hiển thị: {vm.TotalsText}");
        P($"  Mốc quét: {vm.ScanTimeText} | Chế độ: {vm.ModeText} | Trạng thái: {vm.StatusText}");
        var sizes = Row(vm, "sizes")!; int before = vm.Rows.Count;
        vm.ExpandRow(sizes); P($"  Mở 'sizes': hàng {before} → {vm.Rows.Count}; con: {string.Join(", ", vm.Rows.Skip(vm.Rows.IndexOf(sizes) + 1).Take(10).Select(r => r.Name))}");
        vm.CollapseRow(sizes); P($"  Đóng 'sizes': hàng → {vm.Rows.Count}");
        vm.SetSort(SortKey.Name); P($"  Sắp theo Tên (tăng): {string.Join(", ", vm.Rows.Select(r => r.Name))}");
        vm.SetSort(SortKey.Files); P($"  Sắp theo File (giảm): {string.Join(", ", vm.Rows.Select(r => r.Name))}");
        vm.SetSort(SortKey.Allocated); P($"  Sắp theo Trên đĩa (giảm): {string.Join(", ", vm.Rows.Select(r => r.Name))}");
        var hl = Row(vm, "hl")!; vm.ExpandRow(hl); vm.ExpandRow(Row(vm, "dirB")!); var link = Row(vm, "link.bin");
        P($"  Liên kết cứng không chủ hiển thị: '{link?.Name}' Kích thước={link?.LogicalText} Trên đĩa={link?.AllocatedText} badges='{link?.Badges}'");
        var ads = Row(vm, "ads")!; vm.ExpandRow(ads); var host = Row(vm, "host.txt");
        P($"  host.txt: {host?.LogicalText} / {host?.AllocatedText} / {host?.Badges}");

        // ---------------------------------------------------------------- 3. chi tiết (UC-10)
        P("\n== 3. Chi tiết (UC-10) ==");
        vm.SelectedRow = host; await WaitUntil(() => vm.Details is { Loading: false }, 10000);
        foreach (var f in vm.Details!.Fields) P($"  {f.Label,-36}: {f.Value}");
        P("  ADS: " + string.Join(" | ", vm.Details.Streams));
        vm.SelectedRow = link; await WaitUntil(() => vm.Details is { Loading: false }, 10000);
        P("  link.bin — đường dẫn khác (liên kết cứng): " + string.Join(" | ", vm.Details!.Links));
        P($"  link.bin ghi chú: {vm.Details.Fields.FirstOrDefault(x => x.Label is "Ghi chú" or "Note").Value}");

        // ---------------------------------------------------------------- 4. tìm / lọc (UC-09)
        P("\n== 4. Tìm / lọc (UC-09) ==");
        vm.FilterText = "*.bin"; await Task.Delay(700); await WaitUntil(() => !vm.SearchSummary.Contains("Đang") && !vm.SearchSummary.Contains("Searching"), 10000);
        P($"  Tên '*.bin': {vm.SearchSummary}; ví dụ: {string.Join(", ", vm.SearchResults.Take(4).Select(r => r.Name))}");
        vm.FilterText = ""; vm.FilterMin = "1 MiB"; await Task.Delay(700); await WaitUntil(() => vm.IsFilterActive, 5000); await Task.Delay(400);
        P($"  Kích thước (trên đĩa) ≥ 1 MiB: {vm.SearchSummary}; {string.Join(", ", vm.SearchResults.Select(r => r.Name + " " + r.Allocated))}");
        vm.ClearFilter(); vm.FilterText = "["; vm.FilterRegex = true; await Task.Delay(700);
        P($"  Regex sai '[': FilterError = {vm.FilterError ?? "(null)"}");
        vm.ClearFilter(); vm.FilterExt = "txt"; await Task.Delay(700); await Task.Delay(400);
        P($"  Đuôi txt: {vm.SearchSummary}");
        vm.ClearFilter(); await Task.Delay(500);
        P($"  Xoá lọc → IsFilterActive={vm.IsFilterActive}");

        // ---------------------------------------------------------------- 5. Top / loại
        P("\n== 5. Top lớn nhất / loại ==");
        P("  Top file: " + string.Join(" | ", vm.TopFiles.Take(3).Select(f => f.Name + " " + f.Allocated)));
        P("  Top thư mục: " + string.Join(" | ", vm.TopDirs.Take(3).Select(f => Path.GetFileName(f.Path) + " " + f.DirectAllocated)));
        P("  Nhóm loại: " + string.Join(" | ", vm.Groups.Take(4).Select(g => $"{g.Label} {g.Allocated} ({g.Fraction:P0})")));

        // ---------------------------------------------------------------- 6. quét lại nhánh (UC-17)
        P("\n== 6. Quét lại nhánh (UC-17) ==");
        long rootBefore = t[vm.Result.Roots[0].Node].Allocated;
        File.WriteAllBytes(Path.Combine(fx, "sizes", "selftest-added.bin"), new byte[100000]);
        vm.SelectedRow = Row(vm, "sizes"); vm.RescanBranchCommand.Execute(null);
        await WaitUntil(() => vm.IsScanning, 3000); await WaitUntil(() => !vm.IsScanning, 30000); await Task.Delay(300);
        long rootAfter = t[vm.Result.Roots[0].Node].Allocated;
        P($"  Gốc trên đĩa: {N(rootBefore)} → {N(rootAfter)} (Δ {N(rootAfter - rootBefore)}; kỳ vọng +102.400) | {vm.ProgressText} | {vm.StatusText}");

        // ---------------------------------------------------------------- 7. xoá vào Thùng rác (UC-13)
        P("\n== 7. Xoá vào Thùng rác (UC-13) ==");
        vm.ExpandRow(Row(vm, "sizes")!);
        var victim = Row(vm, "selftest-added.bin")!; vm.SelectedRow = victim; sh.Selected.Clear(); sh.Selected.Add(victim);
        long rb = t[vm.Result.Roots[0].Node].Allocated;
        vm.DeleteCommand.Execute(null);
        var req = sh.LastDeleteRequest;
        P($"  Hộp xác nhận: giải phóng NGAY {N(req!.Estimate.FreedNow)} B; SAU khi dọn Thùng rác {N(req.Estimate.FreedAfterEmpty)} B; chọn {N(req.Estimate.SelectedLogical)} B");
        P($"  Còn trên đĩa? {File.Exists(Path.Combine(fx, "sizes", "selftest-added.bin"))}; gốc: {N(rb)} → {N(t[vm.Result.Roots[0].Node].Allocated)} (kỳ vọng −102.400)");
        // ER-09: xoá mục đã bị xoá ngoài app
        var f1 = Row(vm, "f4095.bin")!; vm.SelectedRow = f1; sh.Selected.Clear(); sh.Selected.Add(f1);
        File.Delete(Path.Combine(fx, "sizes", "f4095.bin"));     // xoá ngoài app (vĩnh viễn, file fixture)
        sh.Infos.Clear(); vm.DeleteCommand.Execute(null);
        P($"  ER-09 (mục đã bị xoá ngoài app): thông báo = {string.Join(" || ", sh.Infos.Select(s => s.Replace("\n", " ")))}; còn trong cây? {Row(vm, "f4095.bin") != null}");
        // hard link: ước tính trước khi xoá
        vm.ExpandRow(Row(vm, "hl")!); var dirA = Row(vm, "dirA")!; vm.ExpandRow(dirA); var fileBin = Row(vm, "file.bin")!;
        vm.SelectedRow = fileBin; sh.Selected.Clear(); sh.Selected.Add(fileBin); sh.ConfirmDeleteAnswer = false; vm.DeleteCommand.Execute(null);
        P($"  Chọn file.bin (3 liên kết, 1 ngoài vùng quét) → giải phóng sau dọn = {N(sh.LastDeleteRequest!.Estimate.FreedAfterEmpty)} (kỳ vọng 0), hard link nơi khác = {sh.LastDeleteRequest.Estimate.SharedHardLinkFiles}; người dùng bấm Huỷ → file còn: {File.Exists(Path.Combine(fx, "hl", "dirA", "file.bin"))}");
        sh.ConfirmDeleteAnswer = true;
        // KT-03: chặn vị trí bảo vệ — quét một thư mục tạm có tên giống và thử xoá thư mục gốc quét
        var rootRow = vm.Rows.First(); vm.SelectedRow = rootRow; sh.Selected.Clear(); sh.Selected.Add(rootRow); sh.Infos.Clear();
        vm.DeleteCommand.Execute(null);
        P($"  Xoá cả thư mục gốc quét (không phải vị trí bảo vệ, cần xác nhận): ConfirmDelete được hỏi = {sh.LastDeleteRequest!.Paths[0].EndsWith("verify")} — (bấm Huỷ để giữ fixture)");

        // ---------------------------------------------------------------- 8. snapshot + so sánh + xuất
        P("\n== 8. Snapshot / so sánh / xuất ==");
        string snapA = Path.Combine(fixtureRoot, "selftest-a.fsp"), snapB = Path.Combine(fixtureRoot, "selftest-b.fsp");
        sh.SavePaths.Enqueue(snapA); vm.SaveSnapshotCommand.Execute(null); await WaitUntil(() => File.Exists(snapA), 10000); await Task.Delay(500);
        File.WriteAllBytes(Path.Combine(fx, "sizes", "selftest-grew.bin"), new byte[500000]);
        await ScanAndWait(vm, fx);
        sh.SavePaths.Enqueue(snapB); vm.SaveSnapshotCommand.Execute(null); await WaitUntil(() => File.Exists(snapB), 10000); await Task.Delay(500);
        P($"  Đã lưu 2 snapshot: {new FileInfo(snapA).Length:N0} B và {new FileInfo(snapB).Length:N0} B");
        sh.OpenPaths.Enqueue(snapA); vm.PickCompare(true); sh.OpenPaths.Enqueue(snapB); vm.PickCompare(false);
        await vm.RunCompareAsync();
        P($"  So sánh: {vm.DiffSummary}"); foreach (var d in vm.DiffRows.Take(4)) P($"     {d.Status,-8} {d.Delta,12}  {d.Path}");
        vm.OpenSnapshot(snapA);
        P($"  Mở snapshot A: banner đầu = {vm.Banners.FirstOrDefault()?.Text}");
        P($"  Mốc quét hiển thị: {vm.ScanTimeText}");
        File.WriteAllBytes(Path.Combine(fixtureRoot, "bad-selftest.fsp"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 });
        sh.Infos.Clear(); vm.OpenSnapshot(Path.Combine(fixtureRoot, "bad-selftest.fsp"));
        P($"  ER-12 mở file .fsp hỏng: {string.Join(" || ", sh.Infos)}");
        foreach (var kind in new[] { "html", "csv", "json" })
        {
            string o = Path.Combine(fixtureRoot, "selftest-export." + kind); File.Delete(o);
            sh.SavePaths.Enqueue(o); vm.ExportCommand.Execute(kind);
            await WaitUntil(() => File.Exists(o), 10000); await Task.Delay(300);
            P($"  Xuất {kind}: {new FileInfo(o).Length:N0} B; {vm.StatusText}");
        }
        string html = File.ReadAllText(Path.Combine(fixtureRoot, "selftest-export.html"));
        P($"  HTML chứa: mốc thời gian={html.Contains("UTC ")}, 'host.txt'={html.Contains("host.txt")}, số byte chính xác (title=)={html.Contains("title=\"")}");
        // ER-13: xuất ra đường dẫn không ghi được
        sh.Infos.Clear(); string badOut = @"Z:\khong-co\x.csv"; sh.SavePaths.Enqueue(badOut); vm.ExportCommand.Execute("csv");
        await WaitUntil(() => sh.Infos.Count > 0, 10000); P($"  ER-13 xuất ra '{badOut}': {string.Join(" || ", sh.Infos)}; sót file .tmp: {File.Exists(badOut + ".tmp")}");

        // ---------------------------------------------------------------- 9. KT-07 + dừng/huỷ
        P("\n== 9. Quét lớn: tạm dừng, tiếp tục, huỷ, KT-07 ==");
        string big = @"D:\GitHub";
        vm.StartScan(new[] { big }); await WaitUntil(() => vm.IsScanning, 3000); await Task.Delay(1500);
        vm.PauseResumeCommand.Execute(null); var p1 = vm.Job!.Snapshot(); await Task.Delay(1500); var p2 = vm.Job.Snapshot();
        P($"  Tạm dừng: IsPaused={vm.IsPaused}; số file trước/sau 1,5s: {N(p1.Files)} → {N(p2.Files)} (kỳ vọng gần như đứng yên); trạng thái: {vm.StatusText}");
        sh.ChooseAnswer = 1; vm.StartScan(new[] { fx }); P($"  KT-07 quét khi đang có phiên (chọn 'chờ'): vẫn quét phiên cũ = {vm.IsScanning}; Choose log = {sh.Infos.LastOrDefault()}");
        vm.PauseResumeCommand.Execute(null); await Task.Delay(1200);
        vm.StopCommand.Execute(null); await WaitUntil(() => !vm.IsScanning, 60000); await Task.Delay(300);
        P($"  Huỷ: kết quả {vm.Result!.Completeness}; trạng thái '{vm.StatusText}'; banner: {vm.Banners.FirstOrDefault()?.Text}");
        P($"  Số thư mục 'chưa quét' (Pending): {Enumerable.Range(1, vm.Tree!.Count - 1).Count(i => vm.Tree[i].Parent >= 0 && vm.Tree[i].Has(NodeFlags.Pending)):N0}");
        sh.ChooseAnswer = 0;

        // ---------------------------------------------------------------- 10. thư mục bị chặn (ER-02)
        P("\n== 10. Thư mục bị chặn quyền (ER-02, KT-01) ==");
        string denied = Path.Combine(fixtureRoot, "denied2");
        try { Directory.Delete(denied, true); } catch { }
        Directory.CreateDirectory(Path.Combine(denied, "secret")); File.WriteAllBytes(Path.Combine(denied, "secret", "h.bin"), new byte[40000]); File.WriteAllBytes(Path.Combine(denied, "ok.bin"), new byte[5000]);
        var psi = new System.Diagnostics.ProcessStartInfo("icacls", $"\"{Path.Combine(denied, "secret")}\" /deny \"{Environment.UserName}:(OI)(CI)(RX)\" /q") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        System.Diagnostics.Process.Start(psi)!.WaitForExit();
        await ScanAndWait(vm, denied);
        P($"  Banner: {string.Join(" || ", vm.Banners.Select(b => b.Text + (b.ActionText != null ? " [nút: " + b.ActionText + "]" : "")))}");
        P($"  Tab Vấn đề: {string.Join(" | ", vm.Issues.Select(i => i.Kind + " " + Path.GetFileName(i.Path)))}");
        vm.ExpandRow(vm.Rows.First()); var sec = Row(vm, "secret");
        P($"  Dòng 'secret': Kích thước='{sec?.LogicalText}' Trên đĩa='{sec?.AllocatedText}' badges='{sec?.Badges}' severity={sec?.Severity}");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("icacls", $"\"{denied}\" /reset /t /c /q") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        try { Directory.Delete(denied, true); } catch { }

        // ---------------------------------------------------------------- 11. theo dõi trực tiếp (UC-50)
        P("\n== 11. Theo dõi trực tiếp (UC-50) ==");
        vm.Settings.LiveWatch = true;
        await ScanAndWait(vm, fx); await Task.Delay(800);
        long liveBefore = vm.Tree![vm.Result!.Roots[0].Node].Allocated;
        File.WriteAllBytes(Path.Combine(fx, "sizes", "selftest-live.bin"), new byte[300000]);
        bool changed = await WaitUntil(() => vm.Tree![vm.Result.Roots[0].Node].Allocated != liveBefore, 15000);
        await WaitUntil(() => !vm.IsScanning, 15000); await Task.Delay(500);
        P($"  Sau khi thêm file 300.000 B: tự cập nhật = {changed}; gốc {N(liveBefore)} → {N(vm.Tree![vm.Result.Roots[0].Node].Allocated)} (kỳ vọng +303.104); trạng thái '{vm.ProgressText}'");
        vm.Settings.LiveWatch = false; vm.ApplySettings();

        // dọn
        foreach (var f in new[] { "selftest-added.bin", "selftest-grew.bin", "selftest-live.bin" }) try { File.Delete(Path.Combine(fx, "sizes", f)); } catch { }
        P("\nSELFTEST XONG");
    }
}
