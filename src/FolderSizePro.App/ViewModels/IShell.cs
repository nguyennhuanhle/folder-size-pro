using FolderSizePro.Safety;

namespace FolderSizePro.App.ViewModels;

public sealed class DeleteRequest
{
    public List<string> Paths { get; init; } = new();
    public FreedEstimate Estimate { get; init; } = null!;
}

/// <summary>Những gì ViewModel cần từ cửa sổ (hộp thoại, chọn file…) — giữ ViewModel không phụ thuộc UI.</summary>
public interface IShell
{
    string? PickFolder();
    string? PickOpenFsp();
    string? PickSave(string filter, string defaultName);
    void Info(string title, string text);
    bool Confirm(string title, string text, string ok, string cancel);
    /// <summary>Trả chỉ số nút được bấm, -1 nếu đóng.</summary>
    int Choose(string title, string text, params string[] buttons);
    bool ConfirmDelete(DeleteRequest req);
    void CopyText(string text);
    IReadOnlyList<TreeRow> SelectedRows();
    IntPtr Hwnd { get; }
    void SelectTab(string key);
    void ScrollRowIntoView(TreeRow row);
}
