namespace AIHub.Desktop.Services;

public interface IFileDialogService
{
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);

    Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string fileTypeDisplayName,
        IReadOnlyList<string> patterns,
        CancellationToken cancellationToken = default);

    Task<string?> PickOpenFileAsync(
        string title,
        string fileTypeDisplayName,
        IReadOnlyList<string> patterns,
        CancellationToken cancellationToken = default);
}
