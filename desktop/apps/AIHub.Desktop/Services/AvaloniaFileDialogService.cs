using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AIHub.Desktop.Services;

public sealed class AvaloniaFileDialogService(Window owner) : IFileDialogService
{
    private readonly Window _owner = owner;

    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        if (_owner.StorageProvider is null)
        {
            return null;
        }

        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path?.LocalPath;
    }

    public async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        string fileTypeDisplayName,
        IReadOnlyList<string> patterns,
        CancellationToken cancellationToken = default)
    {
        if (_owner.StorageProvider is null)
        {
            return null;
        }

        var file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType(fileTypeDisplayName)
                {
                    Patterns = patterns.ToArray()
                }
            ]
        });

        return file?.Path?.LocalPath;
    }

    public async Task<string?> PickOpenFileAsync(
        string title,
        string fileTypeDisplayName,
        IReadOnlyList<string> patterns,
        CancellationToken cancellationToken = default)
    {
        if (_owner.StorageProvider is null)
        {
            return null;
        }

        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(fileTypeDisplayName)
                {
                    Patterns = patterns.ToArray()
                }
            ]
        });

        return files.FirstOrDefault()?.Path?.LocalPath;
    }
}
