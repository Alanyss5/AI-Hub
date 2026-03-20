using System.Collections.ObjectModel;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private InstalledSkillRecord? _selectedInstalledSkill;
    private SkillSourceRecord? _selectedSkillInstallSource;
    private SkillModeOption? _selectedSkillModeOption;
    private SkillSourceKindOption? _selectedSkillSourceKindOption;
    private string _skillInstallSourcePath = string.Empty;
    private string _selectedSkillDetailsDisplay = DefaultText.State.SelectInstalledSkill;
    private string _skillInstallStatusDisplay = DefaultText.State.SkillInstallStatusPlaceholder;
    private string _skillInstallBaselineDisplay = DefaultText.State.SkillInstallBaselinePlaceholder;
    private string _skillInstallSourceDisplay = DefaultText.State.SkillInstallSourcePlaceholder;

    public ObservableCollection<SkillModeOption> SkillModeOptions { get; }

    public ObservableCollection<SkillSourceKindOption> SkillSourceKindOptions { get; }

    public AsyncDelegateCommand SaveSkillInstallCommand { get; }

    public AsyncDelegateCommand DeleteSkillInstallCommand { get; }

    public AsyncDelegateCommand CaptureSkillBaselineCommand { get; }

    public AsyncDelegateCommand PreviewSkillDiffCommand { get; }

    public AsyncDelegateCommand ScanSkillSourceCommand { get; }

    public AsyncDelegateCommand CheckSkillUpdateCommand { get; }

    public AsyncDelegateCommand SyncSkillCommand { get; }

    public AsyncDelegateCommand ForceSyncSkillCommand { get; }

    public AsyncDelegateCommand RollbackSkillCommand { get; }

    public InstalledSkillRecord? SelectedInstalledSkill
    {
        get => _selectedInstalledSkill;
        set
        {
            if (SetProperty(ref _selectedInstalledSkill, value))
            {
                ApplySelectedInstalledSkill();
                RaiseCommandStates();
            }
        }
    }

    public SkillSourceRecord? SelectedSkillInstallSource
    {
        get => _selectedSkillInstallSource;
        set
        {
            if (SetProperty(ref _selectedSkillInstallSource, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public SkillModeOption? SelectedSkillModeOption
    {
        get => _selectedSkillModeOption;
        set
        {
            if (SetProperty(ref _selectedSkillModeOption, value))
            {
                ApplySelectedSkillMode();
                RaiseCommandStates();
            }
        }
    }

    public SkillSourceKindOption? SelectedSkillSourceKindOption
    {
        get => _selectedSkillSourceKindOption;
        set
        {
            if (SetProperty(ref _selectedSkillSourceKindOption, value))
            {
                ApplySkillSourceVersionState(SelectedSkillSource);
                RaiseCommandStates();
            }
        }
    }

    public string SkillInstallSourcePath
    {
        get => _skillInstallSourcePath;
        set => SetProperty(ref _skillInstallSourcePath, value);
    }

    public string SelectedSkillDetailsDisplay
    {
        get => _selectedSkillDetailsDisplay;
        private set => SetProperty(ref _selectedSkillDetailsDisplay, value);
    }

    public string SkillInstallStatusDisplay
    {
        get => _skillInstallStatusDisplay;
        private set => SetProperty(ref _skillInstallStatusDisplay, value);
    }

    public string SkillInstallBaselineDisplay
    {
        get => _skillInstallBaselineDisplay;
        private set => SetProperty(ref _skillInstallBaselineDisplay, value);
    }

    public string SkillInstallSourceDisplay
    {
        get => _skillInstallSourceDisplay;
        private set => SetProperty(ref _skillInstallSourceDisplay, value);
    }

    private async Task SaveSkillInstallAsync()
    {
        if (SelectedInstalledSkill is null)
        {
            SetOperation(false, Text.State.SelectInstalledSkill, string.Empty);
            return;
        }

        var draft = new SkillInstallRecord
        {
            Name = SelectedInstalledSkill.Name,
            Profile = SelectedInstalledSkill.Profile,
            InstalledRelativePath = SelectedInstalledSkill.RelativePath,
            SourceLocalName = SelectedSkillModeOption?.Value == SkillCustomizationMode.Local ? null : SelectedSkillInstallSource?.LocalName,
            SourceProfile = SelectedSkillModeOption?.Value == SkillCustomizationMode.Local ? null : SelectedSkillInstallSource?.Profile,
            SourceSkillPath = SelectedSkillModeOption?.Value == SkillCustomizationMode.Local || string.IsNullOrWhiteSpace(SkillInstallSourcePath)
                ? null
                : SkillInstallSourcePath.Trim(),
            CustomizationMode = SelectedSkillModeOption?.Value ?? SkillCustomizationMode.Local
        };

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.SaveInstallAsync(draft);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task DeleteSkillInstallAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        var confirmed = await ConfirmAsync(CreateDeleteSkillInstallConfirmation(installedSkill));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.DeleteInstallAsync(installedSkill.Profile, installedSkill.RelativePath);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task CaptureSkillBaselineAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.CaptureBaselineAsync(installedSkill.Profile, installedSkill.RelativePath);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(installedSkill.SourceLocalName ?? SelectedSkillSource?.LocalName, installedSkill.SourceProfile ?? SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task PreviewSkillDiffAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.PreviewInstalledSkillDiffAsync(installedSkill.Profile, installedSkill.RelativePath);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(installedSkill.SourceLocalName ?? SelectedSkillSource?.LocalName, installedSkill.SourceProfile ?? SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task ScanSelectedSkillSourceAsync()
    {
        if (SelectedSkillSource is null)
        {
            SetOperation(false, Text.State.SelectSkillsSourceToScan, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.ScanSourceAsync(SelectedSkillSource.LocalName, SelectedSkillSource.Profile);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(SelectedSkillSource.LocalName, SelectedSkillSource.Profile);
            }
        });
    }

    private async Task CheckSelectedSkillUpdateAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.CheckForUpdatesAsync(installedSkill.Profile, installedSkill.RelativePath);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(installedSkill.SourceLocalName ?? SelectedSkillSource?.LocalName, installedSkill.SourceProfile ?? SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task SyncSelectedSkillAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.SyncInstalledSkillAsync(installedSkill.Profile, installedSkill.RelativePath, force: false);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(installedSkill.SourceLocalName ?? SelectedSkillSource?.LocalName, installedSkill.SourceProfile ?? SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task ForceSyncSelectedSkillAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        var confirmed = await ConfirmAsync(CreateForceSyncSkillConfirmation(installedSkill));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.SyncInstalledSkillAsync(installedSkill.Profile, installedSkill.RelativePath, force: true);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(installedSkill.SourceLocalName ?? SelectedSkillSource?.LocalName, installedSkill.SourceProfile ?? SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task RollbackSelectedSkillAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        if (installedSkill.BackupRecords.Count == 0)
        {
            SetOperation(false, Text.State.NoSkillBackupAvailable, installedSkill.RelativePath);
            return;
        }

        var backup = SelectedSkillBackup ?? installedSkill.BackupRecords.First();
        var confirmed = await ConfirmAsync(CreateRollbackSkillConfirmation(installedSkill, backup));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.RollbackInstalledSkillAsync(installedSkill.Profile, installedSkill.RelativePath, backup.Path);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(installedSkill.SourceLocalName ?? SelectedSkillSource?.LocalName, installedSkill.SourceProfile ?? SelectedSkillSource?.Profile);
            }
        });
    }

    private void ApplySelectedInstalledSkill()
    {
        if (SelectedInstalledSkill is null)
        {
            SelectedSkillDetailsDisplay = Text.State.SelectInstalledSkill;
            SkillInstallStatusDisplay = Text.State.SkillInstallStatusPlaceholder;
            SkillInstallBaselineDisplay = Text.State.SkillInstallBaselinePlaceholder;
            SkillInstallSourceDisplay = Text.State.SkillInstallSourcePlaceholder;
            SelectedSkillModeOption = SkillModeOptions.FirstOrDefault(option => option.Value == SkillCustomizationMode.Local);
            SelectedSkillInstallSource = null;
            SkillInstallSourcePath = string.Empty;
            ApplySelectedSkillBackups(null);
            ResetSkillMergePreview();
            ApplySelectedSkillBindings();
            return;
        }

        SelectedSkillDetailsDisplay = $"{SelectedInstalledSkill.ProfileDisplay} / {SelectedInstalledSkill.RelativePath}";
        SkillInstallStatusDisplay = SelectedInstalledSkill.StatusDisplay;
        SkillInstallBaselineDisplay = SelectedInstalledSkill.BaselineDisplay;
        SkillInstallSourceDisplay = SelectedInstalledSkill.SourceDisplay;
        SelectedSkillModeOption = SkillModeOptions.FirstOrDefault(option => option.Value == SelectedInstalledSkill.CustomizationMode)
            ?? SkillModeOptions.FirstOrDefault(option => option.Value == SkillCustomizationMode.Local);
        SelectedSkillInstallSource = FindSkillSource(SkillSources, SelectedInstalledSkill.SourceLocalName, SelectedInstalledSkill.SourceProfile);
        SkillInstallSourcePath = SelectedInstalledSkill.SourceSkillPath ?? string.Empty;
        ApplySelectedSkillBackups(SelectedInstalledSkill);
        ResetSkillMergePreview();
        ApplySelectedSkillBindings();
    }

    private void ApplySelectedSkillMode()
    {
        if (SelectedSkillModeOption?.Value == SkillCustomizationMode.Local)
        {
            SelectedSkillInstallSource = null;
            SkillInstallSourcePath = string.Empty;
        }
    }

    private bool TryGetSelectedRegisteredSkill(out InstalledSkillRecord installedSkill, out string validationError)
    {
        validationError = string.Empty;
        installedSkill = null!;

        if (SelectedInstalledSkill is null)
        {
            validationError = Text.State.SelectInstalledSkill;
            return false;
        }

        if (!SelectedInstalledSkill.IsRegistered)
        {
            validationError = Text.State.SkillInstallRecordRequired;
            return false;
        }

        installedSkill = SelectedInstalledSkill;
        return true;
    }

    private bool CanUseSelectedInstalledSkill()
    {
        return !IsBusy && _skillsCatalogService is not null && SelectedInstalledSkill is not null;
    }

    private bool CanDeleteSelectedSkillInstall()
    {
        return !IsBusy && _skillsCatalogService is not null && SelectedInstalledSkill?.IsRegistered == true;
    }

    private bool CanCaptureSelectedSkillBaseline()
    {
        return !IsBusy && _skillsCatalogService is not null && SelectedInstalledSkill?.IsRegistered == true;
    }

    private bool CanScanSelectedSkillSource()
    {
        return !IsBusy && _skillsCatalogService is not null && SelectedSkillSource is not null;
    }

    private bool CanCheckSelectedSkillUpdate()
    {
        return !IsBusy &&
               _skillsCatalogService is not null &&
               SelectedInstalledSkill?.IsRegistered == true &&
               SelectedInstalledSkill.CustomizationMode != SkillCustomizationMode.Local;
    }

    private bool CanSyncSelectedSkill()
    {
        return !IsBusy &&
               _skillsCatalogService is not null &&
               SelectedInstalledSkill?.IsRegistered == true &&
               SelectedInstalledSkill.CustomizationMode != SkillCustomizationMode.Local;
    }

    private bool CanRollbackSelectedSkill()
    {
        return !IsBusy &&
               _skillsCatalogService is not null &&
               SelectedInstalledSkill?.IsRegistered == true &&
               SelectedInstalledSkill.BackupRecords.Count > 0;
    }

    private static IReadOnlyList<SkillModeOption> CreateSkillModeOptions()
    {
        return
        [
            new SkillModeOption(SkillCustomizationMode.Managed, SkillCustomizationMode.Managed.ToDisplayName(), SkillCustomizationMode.Managed.ToDescription()),
            new SkillModeOption(SkillCustomizationMode.Overlay, SkillCustomizationMode.Overlay.ToDisplayName(), SkillCustomizationMode.Overlay.ToDescription()),
            new SkillModeOption(SkillCustomizationMode.Fork, SkillCustomizationMode.Fork.ToDisplayName(), SkillCustomizationMode.Fork.ToDescription()),
            new SkillModeOption(SkillCustomizationMode.Local, SkillCustomizationMode.Local.ToDisplayName(), SkillCustomizationMode.Local.ToDescription())
        ];
    }

    private static IReadOnlyList<SkillSourceKindOption> CreateSkillSourceKindOptions()
    {
        return
        [
            new SkillSourceKindOption(SkillSourceKind.GitRepository, SkillSourceKind.GitRepository.ToDisplayName(), SkillSourceKind.GitRepository.ToDescription()),
            new SkillSourceKindOption(SkillSourceKind.LocalDirectory, SkillSourceKind.LocalDirectory.ToDisplayName(), SkillSourceKind.LocalDirectory.ToDescription())
        ];
    }

    private static InstalledSkillRecord? FindInstalledSkill(IEnumerable<InstalledSkillRecord> items, string? relativePath, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        return items.FirstOrDefault(item =>
            string.Equals(item.Profile, WorkspaceProfiles.NormalizeId(profileId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }
}
