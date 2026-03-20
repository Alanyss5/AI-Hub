using System.Collections.ObjectModel;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly ObservableCollection<WorkspaceProfileDescriptor> _workspaceProfileItems = new();
    private WorkspaceProfileDescriptor? _selectedWorkspaceProfile;
    private string _workspaceProfileIdInput = string.Empty;
    private string _workspaceProfileDisplayNameInput = string.Empty;
    private string _workspaceProfileUsageSummary = "新分类可用于项目、Skills 和 MCP。";
    private AsyncDelegateCommand? _saveWorkspaceProfileCommand;
    private AsyncDelegateCommand? _deleteWorkspaceProfileCommand;
    private AsyncDelegateCommand? _clearWorkspaceProfileFormCommand;

    public ObservableCollection<WorkspaceProfileDescriptor> WorkspaceProfileItems => _workspaceProfileItems;

    public WorkspaceProfileDescriptor? SelectedWorkspaceProfile
    {
        get => _selectedWorkspaceProfile;
        set
        {
            if (SetProperty(ref _selectedWorkspaceProfile, value))
            {
                ApplySelectedWorkspaceProfile();
                RaisePropertyChanged(nameof(IsWorkspaceProfileIdReadOnly));
                RaisePropertyChanged(nameof(CanDeleteSelectedWorkspaceProfileRecord));
                RaiseCommandStates();
            }
        }
    }

    public string WorkspaceProfileIdInput
    {
        get => _workspaceProfileIdInput;
        set => SetProperty(ref _workspaceProfileIdInput, value);
    }

    public string WorkspaceProfileDisplayNameInput
    {
        get => _workspaceProfileDisplayNameInput;
        set => SetProperty(ref _workspaceProfileDisplayNameInput, value);
    }

    public string WorkspaceProfileUsageSummary
    {
        get => _workspaceProfileUsageSummary;
        private set => SetProperty(ref _workspaceProfileUsageSummary, value);
    }

    public bool IsWorkspaceProfileIdReadOnly => SelectedWorkspaceProfile is not null;

    public bool CanDeleteSelectedWorkspaceProfileRecord => SelectedWorkspaceProfile?.IsDeletable == true;

    public AsyncDelegateCommand SaveWorkspaceProfileCommand => _saveWorkspaceProfileCommand!;

    public AsyncDelegateCommand DeleteWorkspaceProfileCommand => _deleteWorkspaceProfileCommand!;

    public AsyncDelegateCommand ClearWorkspaceProfileFormCommand => _clearWorkspaceProfileFormCommand!;

    private void InitializeProfileManagementState()
    {
        _saveWorkspaceProfileCommand = new AsyncDelegateCommand(SaveWorkspaceProfileAsync, CanUseWorkspaceProfiles);
        _deleteWorkspaceProfileCommand = new AsyncDelegateCommand(DeleteWorkspaceProfileAsync, CanDeleteSelectedWorkspaceProfile);
        _clearWorkspaceProfileFormCommand = new AsyncDelegateCommand(ClearWorkspaceProfileFormAsync, () => !IsBusy);
    }

    private void ApplyWorkspaceProfileSnapshot(IReadOnlyList<WorkspaceProfileDescriptor> descriptors)
    {
        ReplaceCollection(WorkspaceProfileItems, descriptors);
        ApplyWorkspaceProfileCatalog(descriptors.Select(profile => profile.Record).ToArray());

        var selectedProfileId = SelectedWorkspaceProfile?.Id;
        SelectedWorkspaceProfile = descriptors.FirstOrDefault(profile =>
                string.Equals(profile.Id, selectedProfileId, StringComparison.OrdinalIgnoreCase))
            ?? descriptors.FirstOrDefault();

        if (SelectedWorkspaceProfile is null)
        {
            WorkspaceProfileIdInput = string.Empty;
            WorkspaceProfileDisplayNameInput = string.Empty;
            WorkspaceProfileUsageSummary = "新分类可用于项目、Skills 和 MCP。";
        }
    }

    private void ApplySelectedWorkspaceProfile()
    {
        if (SelectedWorkspaceProfile is null)
        {
            WorkspaceProfileIdInput = string.Empty;
            WorkspaceProfileDisplayNameInput = string.Empty;
            WorkspaceProfileUsageSummary = "新分类可用于项目、Skills 和 MCP。";
            return;
        }

        WorkspaceProfileIdInput = SelectedWorkspaceProfile.Id;
        WorkspaceProfileDisplayNameInput = SelectedWorkspaceProfile.DisplayName;
        WorkspaceProfileUsageSummary = SelectedWorkspaceProfile.UsageSummary;
    }

    private async Task SaveWorkspaceProfileAsync()
    {
        if (_workspaceProfileService is null)
        {
            SetOperation(false, "当前桌面端未启用分类目录服务。", string.Empty);
            return;
        }

        if (string.IsNullOrWhiteSpace(WorkspaceProfileIdInput))
        {
            SetOperation(false, "分类标识不能为空。", string.Empty);
            return;
        }

        var draft = new WorkspaceProfileRecord
        {
            Id = WorkspaceProfileIdInput.Trim(),
            DisplayName = WorkspaceProfileDisplayNameInput.Trim(),
            IsBuiltin = SelectedWorkspaceProfile?.IsBuiltin ?? false,
            IsDeletable = SelectedWorkspaceProfile?.IsDeletable ?? true,
            SortOrder = SelectedWorkspaceProfile?.SortOrder ?? WorkspaceProfileItems.Count
        };

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceProfileService.SaveAsync(SelectedWorkspaceProfile?.Id, draft);
            ApplyOperationResult(result);

            if (!result.Success)
            {
                return;
            }

            var preferredProfileId = WorkspaceProfiles.NormalizeId(draft.Id);
            await ReloadWorkspaceProfileDependentDataAsync(preferredProfileId);
        });
    }

    private async Task DeleteWorkspaceProfileAsync()
    {
        if (_workspaceProfileService is null)
        {
            SetOperation(false, "当前桌面端未启用分类目录服务。", string.Empty);
            return;
        }

        if (SelectedWorkspaceProfile is null)
        {
            SetOperation(false, "请先选择要删除的分类。", string.Empty);
            return;
        }

        var confirmed = await ConfirmAsync(CreateDeleteWorkspaceProfileConfirmation(SelectedWorkspaceProfile));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceProfileService.DeleteAsync(SelectedWorkspaceProfile.Id);
            ApplyOperationResult(result);

            if (!result.Success)
            {
                return;
            }

            SelectedWorkspaceProfile = null;
            await ReloadWorkspaceProfileDependentDataAsync();
        });
    }

    private Task ClearWorkspaceProfileFormAsync()
    {
        SelectedWorkspaceProfile = null;
        WorkspaceProfileIdInput = string.Empty;
        WorkspaceProfileDisplayNameInput = string.Empty;
        WorkspaceProfileUsageSummary = "新分类可用于项目、Skills 和 MCP。";
        SetOperation(true, "分类表单已清空。", string.Empty);
        return Task.CompletedTask;
    }

    private async Task ReloadWorkspaceProfileDependentDataAsync(string? preferredProfileId = null)
    {
        await LoadWorkspaceProfilesAsync();
        await LoadWorkspaceAsync(SelectedProject?.Path);
        await LoadMcpAsync(preferredProfileId ?? SelectedMcpProfile?.Profile);
        await LoadSkillsAsync(SelectedSkillSource?.LocalName, preferredProfileId ?? SelectedSkillSource?.Profile);
        await LoadScriptsAsync(SelectedScript?.RelativePath);
    }

    private bool CanUseWorkspaceProfiles()
    {
        return !IsBusy && _workspaceProfileService is not null;
    }

    private bool CanDeleteSelectedWorkspaceProfile()
    {
        return !IsBusy &&
               _workspaceProfileService is not null &&
               SelectedWorkspaceProfile?.IsDeletable == true;
    }

    private void RaiseProfileManagementCommandStates()
    {
        _saveWorkspaceProfileCommand?.RaiseCanExecuteChanged();
        _deleteWorkspaceProfileCommand?.RaiseCanExecuteChanged();
        _clearWorkspaceProfileFormCommand?.RaiseCanExecuteChanged();
    }

    private ConfirmationRequest CreateDeleteWorkspaceProfileConfirmation(WorkspaceProfileDescriptor profile)
    {
        return new ConfirmationRequest(
            "删除分类",
            $"确定要删除分类“{profile.DisplayName}”吗？",
            string.Join(Environment.NewLine, new[]
            {
                "分类标识：" + profile.Id,
                "显示名称：" + profile.DisplayName,
                "引用概览：" + profile.UsageSummary
            }),
            ConfirmText: "删除");
    }
}
