using System.IO;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _autoCheckSkillUpdatesOnLoad;
    private bool _autoSyncSafeSkillsOnLoad;
    private bool _managedProcessKeepAlive;
    private string _settingsPackagePath = string.Empty;

    public AsyncDelegateCommand StartAllManagedProcessesCommand { get; private set; } = null!;

    public AsyncDelegateCommand StopAllManagedProcessesCommand { get; private set; } = null!;

    public AsyncDelegateCommand MaintainManagedProcessesCommand { get; private set; } = null!;

    public AsyncDelegateCommand ResumeSuspendedManagedProcessesCommand { get; private set; } = null!;

    public AsyncDelegateCommand ExportConfigurationPackageCommand { get; private set; } = null!;

    public AsyncDelegateCommand ImportConfigurationPackageCommand { get; private set; } = null!;

    public bool AutoCheckSkillUpdatesOnLoad
    {
        get => _autoCheckSkillUpdatesOnLoad;
        set => SetProperty(ref _autoCheckSkillUpdatesOnLoad, value);
    }

    public bool AutoSyncSafeSkillsOnLoad
    {
        get => _autoSyncSafeSkillsOnLoad;
        set => SetProperty(ref _autoSyncSafeSkillsOnLoad, value);
    }

    public bool ManagedProcessKeepAlive
    {
        get => _managedProcessKeepAlive;
        set => SetProperty(ref _managedProcessKeepAlive, value);
    }

    public string SettingsPackagePath
    {
        get => _settingsPackagePath;
        set => SetProperty(ref _settingsPackagePath, value);
    }

    private void InitializeAdvancedCommands()
    {
        StartAllManagedProcessesCommand = new AsyncDelegateCommand(StartAllManagedProcessesAsync, CanUseMcp);
        StopAllManagedProcessesCommand = new AsyncDelegateCommand(StopAllManagedProcessesAsync, CanUseMcp);
        MaintainManagedProcessesCommand = new AsyncDelegateCommand(MaintainManagedProcessesAsync, CanUseMcp);
        ResumeSuspendedManagedProcessesCommand = new AsyncDelegateCommand(ResumeSuspendedManagedProcessesAsync, CanUseMcp);
        ExportConfigurationPackageCommand = new AsyncDelegateCommand(ExportConfigurationPackageAsync, CanUseConfigurationPackage);
        ImportConfigurationPackageCommand = new AsyncDelegateCommand(ImportConfigurationPackageAsync, CanUseConfigurationPackage);
    }

    private void RaiseAdvancedCommandStates()
    {
        StartAllManagedProcessesCommand.RaiseCanExecuteChanged();
        StopAllManagedProcessesCommand.RaiseCanExecuteChanged();
        MaintainManagedProcessesCommand.RaiseCanExecuteChanged();
        ResumeSuspendedManagedProcessesCommand.RaiseCanExecuteChanged();
        ExportConfigurationPackageCommand.RaiseCanExecuteChanged();
        ImportConfigurationPackageCommand.RaiseCanExecuteChanged();
    }

    private bool CanUseConfigurationPackage()
    {
        return !IsBusy && _workspaceControlService is not null && !string.IsNullOrWhiteSpace(SettingsPackagePath);
    }

    private async Task StartAllManagedProcessesAsync()
    {
        if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ManagedMcpExecution))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.StartManagedProcessesAsync(autoStartOnly: false);
            ApplyOperationResult(result);
            await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
        });
    }

    private async Task StopAllManagedProcessesAsync()
    {
        var confirmed = await ConfirmAsync(CreateStopAllManagedProcessesConfirmation());
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.StopManagedProcessesAsync();
            ApplyOperationResult(result);
            await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
        });
    }

    private async Task MaintainManagedProcessesAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.MaintainManagedProcessesAsync();
            ApplyOperationResult(result);
            await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
        });
    }

    private async Task ResumeSuspendedManagedProcessesAsync()
    {
        if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ManagedMcpExecution))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.ResumeSuspendedManagedProcessesAsync();
            ApplyOperationResult(result);
            await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
        });
    }

    private async Task ExportConfigurationPackageAsync()
    {
        if (string.IsNullOrWhiteSpace(SettingsPackagePath))
        {
            SetOperation(false, Text.State.ConfigurationPackageExportPathRequired, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.ExportConfigurationPackageAsync(SettingsPackagePath);
            ApplyOperationResult(result);
        });
    }

    private async Task ImportConfigurationPackageAsync()
    {
        if (string.IsNullOrWhiteSpace(SettingsPackagePath))
        {
            SetOperation(false, Text.State.ConfigurationPackageImportPathRequired, string.Empty);
            return;
        }

        ConfigurationPackageImportPreview? preview;
        try
        {
            var previewResult = await _workspaceControlService!.PreviewConfigurationPackageImportAsync(SettingsPackagePath);
            if (!previewResult.Success || previewResult.Preview is null)
            {
                SetOperation(false, previewResult.Message, previewResult.Details);
                return;
            }

            preview = previewResult.Preview;
        }
        catch (Exception exception)
        {
            SetOperation(false, Text.State.ConfigurationPackagePreviewFailed, exception.Message);
            return;
        }

        var confirmed = await ConfirmAsync(CreateImportConfigurationPackageConfirmation(preview));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.ImportConfigurationPackageAsync(SettingsPackagePath, preview.PlannedBackupPath);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(SelectedProject?.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    public void SetSettingsPackagePathFromPicker(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            SettingsPackagePath = Path.GetFullPath(path);
        }
    }

    public Task RefreshFromTrayAsync()
    {
        return RefreshAsync();
    }

    public Task StartAllManagedProcessesFromTrayAsync()
    {
        return StartAllManagedProcessesAsync();
    }

    public Task StopAllManagedProcessesFromTrayAsync()
    {
        return StopAllManagedProcessesAsync();
    }

    public Task MaintainManagedProcessesFromTrayAsync()
    {
        return MaintainManagedProcessesAsync();
    }

    public Task ResumeSuspendedManagedProcessesFromTrayAsync()
    {
        return ResumeSuspendedManagedProcessesAsync();
    }

    public async Task MaintainManagedProcessesInBackgroundAsync()
    {
        if (IsBusy || _mcpControlService is null)
        {
            return;
        }

        try
        {
            await _mcpControlService.MaintainManagedProcessesAsync();
            await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
        }
        catch (Exception exception)
        {
            SetOperation(false, Text.State.BackgroundMaintainMcpFailed, exception.Message);
        }
    }

    public async Task RefreshInBackgroundAsync()
    {
        if (IsBusy || _workspaceControlService is null || _mcpControlService is null || _skillsCatalogService is null)
        {
            return;
        }

        try
        {
            await ReloadAllAsync(SelectedProject?.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
        }
        catch (Exception exception)
        {
            SetOperation(false, Text.State.BackgroundRefreshFailed, exception.Message);
        }
    }
}
