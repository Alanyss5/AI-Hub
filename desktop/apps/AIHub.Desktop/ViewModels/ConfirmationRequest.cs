namespace AIHub.Desktop.ViewModels;

public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string Details,
    string ConfirmText = "确认",
    string CancelText = "取消",
    bool IsDangerous = true);