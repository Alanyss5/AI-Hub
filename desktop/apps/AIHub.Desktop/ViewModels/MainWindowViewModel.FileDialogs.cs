using System.IO;
using AIHub.Desktop.Services;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IFileDialogService? _fileDialogService;

    public AsyncDelegateCommand ChooseProjectFolderCommand { get; private set; } = null!;

    public AsyncDelegateCommand ChooseHubRootFolderCommand { get; private set; } = null!;

    public AsyncDelegateCommand ChooseScriptProjectFolderCommand { get; private set; } = null!;

    public AsyncDelegateCommand ChooseScriptUserHomeFolderCommand { get; private set; } = null!;

    public AsyncDelegateCommand ChooseManagedProcessWorkingDirectoryCommand { get; private set; } = null!;

    public AsyncDelegateCommand ChooseSettingsPackageSaveCommand { get; private set; } = null!;

    public AsyncDelegateCommand ChooseSettingsPackageOpenCommand { get; private set; } = null!;

    public AsyncDelegateCommand ChooseDiagnosticsPackageSaveCommand { get; private set; } = null!;

    private void InitializeFileDialogCommands()
    {
        ChooseProjectFolderCommand = new AsyncDelegateCommand(ChooseProjectFolderAsync, CanUseFileDialogs);
        ChooseHubRootFolderCommand = new AsyncDelegateCommand(ChooseHubRootFolderAsync, CanUseFileDialogs);
        ChooseScriptProjectFolderCommand = new AsyncDelegateCommand(ChooseScriptProjectFolderAsync, CanUseFileDialogs);
        ChooseScriptUserHomeFolderCommand = new AsyncDelegateCommand(ChooseScriptUserHomeFolderAsync, CanUseFileDialogs);
        ChooseManagedProcessWorkingDirectoryCommand = new AsyncDelegateCommand(ChooseManagedProcessWorkingDirectoryAsync, CanUseFileDialogs);
        ChooseSettingsPackageSaveCommand = new AsyncDelegateCommand(ChooseSettingsPackageSaveAsync, CanUseFileDialogs);
        ChooseSettingsPackageOpenCommand = new AsyncDelegateCommand(ChooseSettingsPackageOpenAsync, CanUseFileDialogs);
        ChooseDiagnosticsPackageSaveCommand = new AsyncDelegateCommand(ChooseDiagnosticsPackageSaveAsync, CanUseFileDialogs);
    }

    private void RaiseFileDialogCommandStates()
    {
        ChooseProjectFolderCommand.RaiseCanExecuteChanged();
        ChooseHubRootFolderCommand.RaiseCanExecuteChanged();
        ChooseScriptProjectFolderCommand.RaiseCanExecuteChanged();
        ChooseScriptUserHomeFolderCommand.RaiseCanExecuteChanged();
        ChooseManagedProcessWorkingDirectoryCommand.RaiseCanExecuteChanged();
        ChooseSettingsPackageSaveCommand.RaiseCanExecuteChanged();
        ChooseSettingsPackageOpenCommand.RaiseCanExecuteChanged();
        ChooseDiagnosticsPackageSaveCommand.RaiseCanExecuteChanged();
    }

    private bool CanUseFileDialogs()
    {
        return !IsBusy && _fileDialogService is not null;
    }

    private async Task ChooseProjectFolderAsync()
    {
        SetProjectPathFromPicker(await _fileDialogService!.PickFolderAsync(Text.Dialogs.SelectProjectDirectory));
    }

    private async Task ChooseHubRootFolderAsync()
    {
        SetHubRootFromPicker(await _fileDialogService!.PickFolderAsync(Text.Dialogs.SelectHubRootDirectory));
    }

    private async Task ChooseScriptProjectFolderAsync()
    {
        SetScriptProjectPathFromPicker(await _fileDialogService!.PickFolderAsync(Text.Dialogs.SelectScriptProjectDirectory));
    }

    private async Task ChooseScriptUserHomeFolderAsync()
    {
        SetScriptUserHomeFromPicker(await _fileDialogService!.PickFolderAsync(Text.Dialogs.SelectUserDirectory));
    }

    private async Task ChooseManagedProcessWorkingDirectoryAsync()
    {
        SetManagedProcessWorkingDirectoryFromPicker(await _fileDialogService!.PickFolderAsync(Text.Dialogs.SelectManagedProcessWorkingDirectory));
    }

    private async Task ChooseSettingsPackageSaveAsync()
    {
        SetSettingsPackagePathFromPicker(await _fileDialogService!.PickSaveFileAsync(
            Text.Dialogs.SelectPackageSaveLocation,
            "aihub-config-package.json",
            Text.Dialogs.PackageFileTypeName,
            ["*.json"]));
    }

    private async Task ChooseSettingsPackageOpenAsync()
    {
        SetSettingsPackagePathFromPicker(await _fileDialogService!.PickOpenFileAsync(
            Text.Dialogs.SelectPackageToImport,
            Text.Dialogs.PackageFileTypeName,
            ["*.json"]));
    }

    private async Task ChooseDiagnosticsPackageSaveAsync()
    {
        SetDiagnosticsPackagePathFromPicker(await _fileDialogService!.PickSaveFileAsync(
            Text.Dialogs.SelectDiagnosticsExportLocation,
            "aihub-diagnostics.zip",
            Text.Dialogs.DiagnosticsFileTypeName,
            ["*.zip"]));
    }

    public void SetProjectPathFromPicker(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            ProjectPath = Path.GetFullPath(path);
        }
    }

    public void SetHubRootFromPicker(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            HubRootInput = Path.GetFullPath(path);
        }
    }

    public void SetScriptProjectPathFromPicker(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            ScriptProjectPath = Path.GetFullPath(path);
        }
    }

    public void SetScriptUserHomeFromPicker(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            ScriptUserHome = Path.GetFullPath(path);
        }
    }

    public void SetManagedProcessWorkingDirectoryFromPicker(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            ManagedProcessWorkingDirectory = Path.GetFullPath(path);
        }
    }
}
