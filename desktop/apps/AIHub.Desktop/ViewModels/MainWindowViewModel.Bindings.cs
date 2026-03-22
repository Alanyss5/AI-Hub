using System.Collections.ObjectModel;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private SkillFolderGroupItem? _selectedSkillGroup;
    private McpServerBindingItem? _selectedMcpServer;
    private BindingResolutionPreview? _pendingSkillBindingResolution;
    private BindingResolutionPreview? _pendingSkillGroupBindingResolution;
    private BindingPreviewState _pendingSkillBindingPreviewState = BindingPreviewState.Idle;
    private BindingPreviewState _pendingSkillGroupBindingPreviewState = BindingPreviewState.Idle;
    private int _skillBindingResolutionPreviewVersion;
    private int _skillGroupBindingResolutionPreviewVersion;
    private readonly Dictionary<string, BindingDraftState> _skillBindingDraftStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BindingDraftState> _skillGroupBindingDraftStates = new(StringComparer.OrdinalIgnoreCase);
    private string _mcpServerName = string.Empty;
    private string _mcpServerEditor = "{\r\n  \"command\": \"\"\r\n}";

    public ObservableCollection<SkillFolderGroupItem> SkillGroups { get; } = new();

    public ObservableCollection<ProfileBindingOption> SkillBindingProfiles { get; } = new();

    public ObservableCollection<ProfileBindingOption> SkillGroupBindingProfiles { get; } = new();

    public ObservableCollection<McpServerBindingItem> McpServers { get; } = new();

    public ObservableCollection<ProfileBindingOption> McpServerBindingProfiles { get; } = new();

    public AsyncDelegateCommand SaveSkillBindingsCommand { get; private set; } = null!;

    public AsyncDelegateCommand SaveSkillGroupBindingsCommand { get; private set; } = null!;

    public AsyncDelegateCommand SaveMcpServerBindingsCommand { get; private set; } = null!;

    public AsyncDelegateCommand ClearMcpServerSelectionCommand { get; private set; } = null!;

    public AsyncDelegateCommand OpenSelectedSkillBindingsCommand { get; private set; } = null!;

    public AsyncDelegateCommand OpenSelectedSkillGroupBindingsCommand { get; private set; } = null!;

    public AsyncDelegateCommand OpenSelectedSkillSourceManagementCommand { get; private set; } = null!;

    public SkillFolderGroupItem? SelectedSkillGroup
    {
        get => _selectedSkillGroup;
        set
        {
            if (SetProperty(ref _selectedSkillGroup, value))
            {
                ApplySelectedSkillGroup();
                RaiseCommandStates();
            }
        }
    }

    public McpServerBindingItem? SelectedMcpServer
    {
        get => _selectedMcpServer;
        set
        {
            if (SetProperty(ref _selectedMcpServer, value))
            {
                ApplySelectedMcpServer();
                RaiseCommandStates();
            }
        }
    }

    public string McpServerName
    {
        get => _mcpServerName;
        set => SetProperty(ref _mcpServerName, value);
    }

    public string McpServerEditor
    {
        get => _mcpServerEditor;
        set => SetProperty(ref _mcpServerEditor, value);
    }

    public string SelectedSkillBindingSummaryDisplay => SelectedInstalledSkill?.BindingSummaryDisplay ?? "鏈€夋嫨 Skill";

    public string PendingSkillBindingSummaryDisplay => BuildBindingSummaryDisplay(SkillBindingProfiles);

    public string PendingSkillBindingSaveTargetDisplay => BuildBindingSaveTargetDisplay(SkillBindingProfiles);

    public string PendingSkillBindingSourceDisplay => BuildBindingPreviewSourceDisplay(
        SelectedInstalledSkill is not null,
        _pendingSkillBindingPreviewState,
        _pendingSkillBindingResolution,
        "Skill");

    public string SelectedSkillGroupBindingSummaryDisplay => SelectedSkillGroup?.ProfileSummary ?? "鏈€夋嫨鍒嗙粍";

    public string PendingSkillGroupBindingSummaryDisplay => BuildBindingSummaryDisplay(SkillGroupBindingProfiles);

    public string PendingSkillGroupBindingSaveTargetDisplay => BuildBindingSaveTargetDisplay(SkillGroupBindingProfiles);

    public string PendingSkillGroupBindingSourceDisplay => BuildBindingPreviewSourceDisplay(
        SelectedSkillGroup is not null,
        _pendingSkillGroupBindingPreviewState,
        _pendingSkillGroupBindingResolution,
        "鍒嗙粍");

    public string CurrentSkillBindingImpactDisplay => BuildSkillBindingImpactDisplay();

    public string CurrentSkillGroupBindingImpactDisplay => BuildSkillGroupBindingImpactDisplay();

    public string CurrentSkillsContextImpactDisplay => BuildSkillsContextImpactDisplay();

    public string SelectedBindingTargetsImpactDisplay => BuildSelectedBindingImpactDisplay();

    public bool HasPendingSkillBindingChanges => HasPendingBindingChanges(GetSelectedSkillPersistedProfileIds(), SkillBindingProfiles);

    public bool HasPendingSkillGroupBindingChanges => HasPendingBindingChanges(GetSelectedSkillGroupPersistedProfileIds(), SkillGroupBindingProfiles);

    private void InitializeBindingState()
    {
        SaveSkillBindingsCommand = new AsyncDelegateCommand(SaveSelectedSkillBindingsAsync, CanSaveSelectedSkillBindings);
        SaveSkillGroupBindingsCommand = new AsyncDelegateCommand(SaveSelectedSkillGroupBindingsAsync, CanSaveSelectedSkillGroupBindings);
        SaveMcpServerBindingsCommand = new AsyncDelegateCommand(SaveSelectedMcpServerBindingsAsync, CanSaveSelectedMcpServerBindings);
        ClearMcpServerSelectionCommand = new AsyncDelegateCommand(ClearSelectedMcpServerAsync, () => !IsBusy);
        OpenSelectedSkillBindingsCommand = new AsyncDelegateCommand(OpenSelectedSkillBindingsAsync, CanOpenSelectedSkillBindings);
        OpenSelectedSkillGroupBindingsCommand = new AsyncDelegateCommand(OpenSelectedSkillGroupBindingsAsync, CanOpenSelectedSkillGroupBindings);
        OpenSelectedSkillSourceManagementCommand = new AsyncDelegateCommand(OpenSelectedSkillSourceManagementAsync, CanOpenSelectedSkillSourceManagement);
        RefreshBindingOptions();
    }

    private void RefreshBindingOptions()
    {
        RefreshProfileBindingCollection(SkillBindingProfiles, GetSelectedSkillBindingDraftProfileIds());
        RefreshProfileBindingCollection(SkillGroupBindingProfiles, GetSelectedSkillGroupBindingDraftProfileIds());
        RefreshProfileBindingCollection(McpServerBindingProfiles, SelectedMcpServer?.ProfileIds);
        RaiseBindingSelectionState();
    }

    private void RefreshProfileBindingCollection(
        ObservableCollection<ProfileBindingOption> target,
        params string?[] selectedProfiles)
    {
        var selected = selectedProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RefreshProfileBindingCollection(target, selected);
    }

    private void RefreshProfileBindingCollection(
        ObservableCollection<ProfileBindingOption> target,
        IEnumerable<string>? selectedProfiles)
    {
        var selected = (selectedProfiles ?? Array.Empty<string>())
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var options = _workspaceProfileCatalog
            .OrderBy(profile => profile.SortOrder)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new ProfileBindingOption(
                profile.Id,
                profile.DisplayName,
                selected.Contains(profile.Id)))
            .ToList();

        var knownProfileIds = options
            .Select(option => WorkspaceProfiles.NormalizeId(option.ProfileId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var orphanedProfileId in selected
                     .Where(profileId => !knownProfileIds.Contains(profileId))
                     .OrderBy(profileId => profileId, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(new ProfileBindingOption(
                orphanedProfileId,
                BuildOrphanedProfileDisplayName(orphanedProfileId),
                true));
        }

        ReplaceCollection(
            target,
            options);

        foreach (var option in target)
        {
            option.PropertyChanged -= OnProfileBindingOptionChanged;
            option.PropertyChanged += OnProfileBindingOptionChanged;
        }
    }

    private void ApplySelectedSkillBindings()
    {
        RefreshProfileBindingCollection(SkillBindingProfiles, GetSelectedSkillBindingDraftProfileIds());
        RaiseBindingSelectionState();
    }

    private void ApplySelectedSkillGroup()
    {
        RefreshProfileBindingCollection(SkillGroupBindingProfiles, GetSelectedSkillGroupBindingDraftProfileIds());
        RaiseBindingSelectionState();
    }

    private async Task SaveSelectedSkillBindingsAsync()
    {
        if (SelectedInstalledSkill is null)
        {
            SetOperation(false, Text.State.SelectInstalledSkill, string.Empty);
            return;
        }

        if (!HasPendingSkillBindingChanges)
        {
            return;
        }

        if (TryGetBindingPreviewValidationError(_pendingSkillBindingPreviewState, _pendingSkillBindingResolution, out var previewError))
        {
            SetOperation(false, previewError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var targets = GetSelectedBindingTargetProfiles(SkillBindingProfiles);
            var sourceProfile = ResolveSkillBindingSourceProfile(SelectedInstalledSkill);

            var result = await _skillsCatalogService!.SaveSkillBindingsAsync(
                sourceProfile,
                SelectedInstalledSkill.RelativePath,
                targets);
            ApplyOperationResult(result);
            if (result.Success)
            {
                await LoadSkillsAsync();
            }
        });
    }

    private async Task SaveSelectedSkillGroupBindingsAsync()
    {
        if (SelectedSkillGroup is null)
        {
            SetOperation(false, Text.State.SelectSkillGroupFirst, string.Empty);
            return;
        }

        if (!HasPendingSkillGroupBindingChanges)
        {
            return;
        }

        if (TryGetBindingPreviewValidationError(_pendingSkillGroupBindingPreviewState, _pendingSkillGroupBindingResolution, out var previewError))
        {
            SetOperation(false, previewError, string.Empty);
            return;
        }

        var sourceProfile = SelectedSkillGroup.SourceProfileIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sourceProfile))
        {
            SetOperation(false, Text.State.NoSkillGroupSourceProfile, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var targets = GetSelectedBindingTargetProfiles(SkillGroupBindingProfiles);
            var effectiveSourceProfile = ResolveSkillGroupBindingSourceProfile(SelectedSkillGroup, sourceProfile);

            var result = await _skillsCatalogService!.SaveSkillGroupBindingsAsync(
                effectiveSourceProfile,
                SelectedSkillGroup.RelativeRootPath,
                targets);
            ApplyOperationResult(result);
            if (result.Success)
            {
                await LoadSkillsAsync();
            }
        });
    }

    private Task OpenSelectedSkillBindingsAsync()
    {
        SelectedSkillsBindingListIndex = 0;
        SelectedSkillsBindingEditorIndex = 0;
        SelectedSkillsSection = SkillsSection.Bindings;
        return Task.CompletedTask;
    }

    private Task OpenSelectedSkillGroupBindingsAsync()
    {
        SelectedSkillsBindingListIndex = 1;
        SelectedSkillsBindingEditorIndex = 1;
        SelectedSkillsSection = SkillsSection.Bindings;
        return Task.CompletedTask;
    }

    private Task OpenSelectedSkillSourceManagementAsync()
    {
        SetSelectedSkillSourceEditor(GetSelectedSkillSourceBrowser(), raiseCommandStates: true);
        SelectedSkillsSection = SkillsSection.Sources;
        return Task.CompletedTask;
    }

    private void RefreshMcpServerItems(string? preferredProfile = null)
    {
        var selectedProfile = !string.IsNullOrWhiteSpace(preferredProfile)
            ? WorkspaceProfiles.NormalizeId(preferredProfile)
            : SelectedMcpProfile?.Profile;

        var serverItems = _mcpProfileCache
            .Select(profile => new
            {
                Profile = profile.Profile,
                DisplayName = profile.ProfileDisplayName,
                Servers = ParseMcpServers(profile.RawJson)
            })
            .SelectMany(profile => profile.Servers.Select(server => new
            {
                server.Key,
                RawJson = NormalizeJsonFragment(server.Value),
                profile.Profile,
                profile.DisplayName
            }))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group
                    .OrderBy(item => string.Equals(item.Profile, selectedProfile, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(item => item.Profile, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new McpServerBindingItem(
                    group.Key,
                    entries.First().RawJson,
                    entries.Select(item => item.Profile).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    entries.Select(item => item.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceCollection(McpServers, serverItems);
        var preferredServerName = SelectedMcpServer?.Name;
        if (!string.IsNullOrWhiteSpace(selectedProfile))
        {
            preferredServerName ??= serverItems.FirstOrDefault(item => item.ProfileIds.Contains(selectedProfile, StringComparer.OrdinalIgnoreCase))?.Name;
        }

        SelectedMcpServer = FindMcpServerItem(serverItems, preferredServerName) ?? serverItems.FirstOrDefault();
        if (SelectedMcpServer is null)
        {
            ResetSelectedMcpServer();
        }
    }

    private void ApplySelectedMcpServer()
    {
        if (SelectedMcpServer is null)
        {
            ResetSelectedMcpServer();
            return;
        }

        McpServerName = SelectedMcpServer.Name;
        McpServerEditor = SelectedMcpServer.RawJson;
        RefreshProfileBindingCollection(McpServerBindingProfiles, SelectedMcpServer.ProfileIds);
    }

    private async Task SaveSelectedMcpServerBindingsAsync()
    {
        var serverName = string.IsNullOrWhiteSpace(McpServerName) ? SelectedMcpServer?.Name : McpServerName.Trim();
        if (string.IsNullOrWhiteSpace(serverName))
        {
            SetOperation(false, Text.State.SelectOrEnterMcpServerName, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var targets = GetSelectedBindingTargetProfiles(McpServerBindingProfiles);

            var result = await _mcpControlService!.SaveServerBindingsAsync(serverName, McpServerEditor, targets);
            ApplyOperationResult(result);
            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
                SelectedMcpServer = McpServers.FirstOrDefault(item => string.Equals(item.Name, serverName, StringComparison.OrdinalIgnoreCase));
            }
        });
    }

    private Task ClearSelectedMcpServerAsync()
    {
        SelectedMcpServer = null;
        return Task.CompletedTask;
    }

    private void ResetSelectedMcpServer()
    {
        McpServerName = string.Empty;
        McpServerEditor = "{\r\n  \"command\": \"\"\r\n}";
        RefreshProfileBindingCollection(McpServerBindingProfiles, Array.Empty<string>());
    }

    private bool CanSaveSelectedSkillBindings()
        => !IsBusy
           && _skillsCatalogService is not null
           && SelectedInstalledSkill is not null
           && HasPendingSkillBindingChanges
           && IsBindingPreviewSaveable(_pendingSkillBindingPreviewState, _pendingSkillBindingResolution);

    private bool CanSaveSelectedSkillGroupBindings()
        => !IsBusy
           && _skillsCatalogService is not null
           && SelectedSkillGroup is not null
           && HasPendingSkillGroupBindingChanges
           && IsBindingPreviewSaveable(_pendingSkillGroupBindingPreviewState, _pendingSkillGroupBindingResolution);

    private bool CanOpenSelectedSkillBindings()
        => !IsBusy && SelectedInstalledSkill is not null;

    private bool CanOpenSelectedSkillGroupBindings()
        => !IsBusy && SelectedSkillGroup is not null;

    private bool CanOpenSelectedSkillSourceManagement()
        => !IsBusy && GetSelectedSkillSourceBrowser() is not null;

    private bool CanSaveSelectedMcpServerBindings()
        => !IsBusy && _mcpControlService is not null;

    private void OnProfileBindingOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileBindingOption.IsSelected))
        {
            if (sender is ProfileBindingOption option)
            {
                if (SkillBindingProfiles.Contains(option))
                {
                    UpdateCurrentSkillBindingDraftState();
                }
                else if (SkillGroupBindingProfiles.Contains(option))
                {
                    UpdateCurrentSkillGroupBindingDraftState();
                }
            }

            RaiseBindingSelectionState();
            RaiseCommandStates();
        }
    }

    private void QueueBindingResolutionPreviewRefresh()
    {
        QueueSkillBindingResolutionPreviewRefresh();
        QueueSkillGroupBindingResolutionPreviewRefresh();
    }

    private void QueueSkillBindingResolutionPreviewRefresh()
    {
        if (_skillsCatalogService is null)
        {
            ResetSkillBindingResolutionPreview(BindingPreviewState.Idle);
            return;
        }

        if (SelectedInstalledSkill is null)
        {
            ResetSkillBindingResolutionPreview(BindingPreviewState.Idle);
            return;
        }

        var previewVersion = Interlocked.Increment(ref _skillBindingResolutionPreviewVersion);
        var requestedSourceProfile = ResolveSkillBindingSourceProfile(SelectedInstalledSkill);
        var relativePath = SelectedInstalledSkill.RelativePath;
        var targets = GetSelectedBindingTargetProfiles(SkillBindingProfiles);
        _pendingSkillBindingResolution = null;
        _pendingSkillBindingPreviewState = BindingPreviewState.Loading;
        RaiseSkillBindingPreviewStateChanged();
        _ = RefreshSkillBindingResolutionPreviewAsync(previewVersion, requestedSourceProfile, relativePath, targets);
    }

    private void QueueSkillGroupBindingResolutionPreviewRefresh()
    {
        if (_skillsCatalogService is null)
        {
            ResetSkillGroupBindingResolutionPreview(BindingPreviewState.Idle);
            return;
        }

        if (SelectedSkillGroup is null)
        {
            ResetSkillGroupBindingResolutionPreview(BindingPreviewState.Idle);
            return;
        }

        var previewVersion = Interlocked.Increment(ref _skillGroupBindingResolutionPreviewVersion);
        var fallbackProfile = SelectedSkillGroup.SourceProfileIds.FirstOrDefault() ?? WorkspaceProfiles.GlobalId;
        var requestedSourceProfile = ResolveSkillGroupBindingSourceProfile(SelectedSkillGroup, fallbackProfile);
        var relativeRootPath = SelectedSkillGroup.RelativeRootPath;
        var targets = GetSelectedBindingTargetProfiles(SkillGroupBindingProfiles);
        _pendingSkillGroupBindingResolution = null;
        _pendingSkillGroupBindingPreviewState = BindingPreviewState.Loading;
        RaiseSkillGroupBindingPreviewStateChanged();
        _ = RefreshSkillGroupBindingResolutionPreviewAsync(previewVersion, requestedSourceProfile, relativeRootPath, targets);
    }

    private async Task RefreshSkillBindingResolutionPreviewAsync(
        int previewVersion,
        string requestedSourceProfile,
        string relativePath,
        IReadOnlyList<string> targets)
    {
        try
        {
            var preview = await _skillsCatalogService!.PreviewSkillBindingResolutionAsync(
                requestedSourceProfile,
                relativePath,
                targets);
            if (previewVersion != _skillBindingResolutionPreviewVersion)
            {
                return;
            }

            _pendingSkillBindingResolution = preview;
            _pendingSkillBindingPreviewState = BindingPreviewState.Resolved;
        }
        catch
        {
            if (previewVersion != _skillBindingResolutionPreviewVersion)
            {
                return;
            }

            _pendingSkillBindingResolution = null;
            _pendingSkillBindingPreviewState = BindingPreviewState.Failed;
        }

        RaiseSkillBindingPreviewStateChanged();
    }

    private async Task RefreshSkillGroupBindingResolutionPreviewAsync(
        int previewVersion,
        string requestedSourceProfile,
        string relativeRootPath,
        IReadOnlyList<string> targets)
    {
        try
        {
            var preview = await _skillsCatalogService!.PreviewSkillGroupBindingResolutionAsync(
                requestedSourceProfile,
                relativeRootPath,
                targets);
            if (previewVersion != _skillGroupBindingResolutionPreviewVersion)
            {
                return;
            }

            _pendingSkillGroupBindingResolution = preview;
            _pendingSkillGroupBindingPreviewState = BindingPreviewState.Resolved;
        }
        catch
        {
            if (previewVersion != _skillGroupBindingResolutionPreviewVersion)
            {
                return;
            }

            _pendingSkillGroupBindingResolution = null;
            _pendingSkillGroupBindingPreviewState = BindingPreviewState.Failed;
        }

        RaiseSkillGroupBindingPreviewStateChanged();
    }

    private void RaiseBindingResolutionPreviewProperties()
    {
        RaiseSkillBindingPreviewStateChanged();
        RaiseSkillGroupBindingPreviewStateChanged();
    }

    private void RaiseSkillBindingPreviewStateChanged()
        => RaiseBindingPreviewStateChanged(
            nameof(PendingSkillBindingSourceDisplay),
            nameof(CurrentSkillBindingImpactDisplay));

    private void RaiseSkillGroupBindingPreviewStateChanged()
        => RaiseBindingPreviewStateChanged(
            nameof(PendingSkillGroupBindingSourceDisplay),
            nameof(CurrentSkillGroupBindingImpactDisplay));

    private void RaiseBindingPreviewStateChanged(string sourcePropertyName, string impactPropertyName)
    {
        RaisePropertyChanged(sourcePropertyName);
        RaisePropertyChanged(impactPropertyName);
        RaisePropertyChanged(nameof(SelectedBindingTargetsImpactDisplay));
        RaiseCommandStates();
    }

    private void RaiseBindingSelectionState()
    {
        RaisePropertyChanged(nameof(SelectedSkillBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillBindingSaveTargetDisplay));
        RaisePropertyChanged(nameof(PendingSkillBindingSourceDisplay));
        RaisePropertyChanged(nameof(CurrentSkillBindingImpactDisplay));
        RaisePropertyChanged(nameof(HasPendingSkillBindingChanges));
        RaisePropertyChanged(nameof(SelectedSkillGroupBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillGroupBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillGroupBindingSaveTargetDisplay));
        RaisePropertyChanged(nameof(PendingSkillGroupBindingSourceDisplay));
        RaisePropertyChanged(nameof(CurrentSkillGroupBindingImpactDisplay));
        RaisePropertyChanged(nameof(HasPendingSkillGroupBindingChanges));
        RaisePropertyChanged(nameof(CurrentSkillsContextImpactDisplay));
        RaisePropertyChanged(nameof(SelectedBindingTargetsImpactDisplay));
        RaiseCommandStates();
        QueueBindingResolutionPreviewRefresh();
    }

    private static string BuildBindingSummaryDisplay(IEnumerable<ProfileBindingOption> options)
    {
        var selected = options
            .Where(option => option.IsSelected)
            .Select(option => option.DisplayName)
            .ToArray();

        return selected.Length == 0 ? "未绑定" : string.Join(" / ", selected);
    }

    private static string BuildBindingSaveTargetDisplay(IEnumerable<ProfileBindingOption> options)
    {
        var selected = options
            .Where(option => option.IsSelected)
            .Select(option => option.DisplayName)
            .ToArray();

        return selected.Length == 0
            ? "保存后将显示为未绑定"
            : $"保存到：{string.Join(" / ", selected)}";
    }

    private string ResolveSkillBindingSourceProfile(InstalledSkillRecord skill)
    {
        var availableProfiles = skill.BindingProfileIds.Count == 0
            ? new[] { skill.Profile }
            : skill.BindingProfileIds;

        return ResolvePreferredBindingSourceProfile(availableProfiles, skill.Profile);
    }

    private string ResolveSkillGroupBindingSourceProfile(SkillFolderGroupItem group, string fallbackProfile)
    {
        var availableProfiles = GetCompleteSkillGroupSourceProfiles(group);

        return ResolvePreferredBindingSourceProfile(availableProfiles, fallbackProfile);
    }

    private IReadOnlyList<string> GetCompleteSkillGroupSourceProfiles(SkillFolderGroupItem group)
    {
        var sourceSkills = _installedSkillCache
            .Where(skill => string.Equals(GetSkillGroupRootPath(skill.RelativePath), group.RelativeRootPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sourceSkills.Length == 0)
        {
            sourceSkills = group.Skills.ToArray();
        }

        if (sourceSkills.Length == 0)
        {
            return group.SourceProfileIds;
        }

        HashSet<string>? sharedProfiles = null;
        foreach (var skill in sourceSkills)
        {
            var skillProfiles = (skill.BindingProfileIds.Count == 0
                    ? new[] { skill.Profile }
                    : skill.BindingProfileIds)
                .Where(profile => !string.IsNullOrWhiteSpace(profile))
                .Select(WorkspaceProfiles.NormalizeId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            sharedProfiles = sharedProfiles is null
                ? skillProfiles
                : sharedProfiles.Intersect(skillProfiles, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (sharedProfiles is { Count: > 0 })
        {
            return sharedProfiles.ToArray();
        }

        return group.SourceProfileIds
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ResolvePreferredBindingSourceProfile(IEnumerable<string> availableProfiles, string? fallbackProfile)
    {
        var normalizedProfiles = availableProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var contextProfile = WorkspaceProfiles.NormalizeId(_skillsPageContext?.SelectedTarget?.ProfileId);
        if (normalizedProfiles.Contains(contextProfile, StringComparer.OrdinalIgnoreCase))
        {
            return contextProfile;
        }

        var normalizedFallback = string.IsNullOrWhiteSpace(fallbackProfile)
            ? null
            : WorkspaceProfiles.NormalizeId(fallbackProfile);
        if (!string.IsNullOrWhiteSpace(normalizedFallback)
            && normalizedProfiles.Contains(normalizedFallback, StringComparer.OrdinalIgnoreCase))
        {
            return normalizedFallback;
        }

        return normalizedProfiles.FirstOrDefault() ?? normalizedFallback ?? WorkspaceProfiles.GlobalId;
    }

    private string BuildResolvedSkillBindingSourceDisplay()
    {
        if (SelectedInstalledSkill is null)
        {
            return "实际写回来源：未选择 Skill";
        }

        return _pendingSkillBindingResolution is null
            ? "实际写回来源：正在解析..."
            : $"实际写回来源：{FormatBindingResolutionSource(_pendingSkillBindingResolution)}";
    }

    private string BuildResolvedSkillGroupBindingSourceDisplay()
    {
        if (SelectedSkillGroup is null)
        {
            return "实际写回来源：未选择分组";
        }

        return _pendingSkillGroupBindingResolution is null
            ? "实际写回来源：正在解析..."
            : $"实际写回来源：{FormatBindingResolutionSource(_pendingSkillGroupBindingResolution)}";
    }

    private string BuildSkillBindingImpactDisplay()
    {
        return BuildBindingImpactDisplay(
            GetSelectedBindingTargetProfiles(SkillBindingProfiles),
            _pendingSkillBindingPreviewState == BindingPreviewState.Resolved ? _pendingSkillBindingResolution : null);
    }

    private string BuildSkillGroupBindingImpactDisplay()
    {
        return BuildBindingImpactDisplay(
            GetSelectedBindingTargetProfiles(SkillGroupBindingProfiles),
            _pendingSkillGroupBindingPreviewState == BindingPreviewState.Resolved ? _pendingSkillGroupBindingResolution : null);
    }

    private string BuildSelectedBindingImpactDisplay()
    {
        if (_selectedSkillsBindingEditor == SkillsBindingEditor.Skill && SelectedInstalledSkill is null)
        {
            return "影响说明：未选择 Skill。";
        }

        if (_selectedSkillsBindingEditor == SkillsBindingEditor.SkillGroup && SelectedSkillGroup is null)
        {
            return "影响说明：未选择分组。";
        }

        var selectedTargets = _selectedSkillsBindingEditor switch
        {
            SkillsBindingEditor.Skill => GetSelectedBindingTargetProfiles(SkillBindingProfiles),
            SkillsBindingEditor.SkillGroup => GetSelectedBindingTargetProfiles(SkillGroupBindingProfiles),
            _ => Array.Empty<string>()
        };
        var preview = _selectedSkillsBindingEditor switch
        {
            SkillsBindingEditor.Skill when _pendingSkillBindingPreviewState == BindingPreviewState.Resolved
                => _pendingSkillBindingResolution,
            SkillsBindingEditor.SkillGroup when _pendingSkillGroupBindingPreviewState == BindingPreviewState.Resolved
                => _pendingSkillGroupBindingResolution,
            _ => null
        };
        return BuildBindingImpactDisplay(selectedTargets, preview);
    }

    private string BuildBindingPreviewSourceDisplay(
        bool hasSelection,
        BindingPreviewState previewState,
        BindingResolutionPreview? preview,
        string subjectName)
    {
        if (!hasSelection)
        {
            return $"保存后上游来源：未选择{subjectName}";
        }

        return previewState switch
        {
            BindingPreviewState.Loading => "保存后上游来源：正在解析...",
            BindingPreviewState.Failed => "保存后上游来源：解析失败",
            BindingPreviewState.Resolved when preview is not null
                => $"保存后上游来源：{FormatBindingPreviewSource(preview)}",
            _ => "保存后上游来源：未解析"
        };
    }
    private string BuildBindingImpactDisplay(
        IReadOnlyList<string> targetProfiles,
        BindingResolutionPreview? preview = null)
    {
        var normalizedProfiles = ResolveImpactProfileIds(targetProfiles, preview);
        if (normalizedProfiles.Length == 0)
        {
            return "影响说明：取消全部勾选后会转入库中，不会直接影响任何已接管项目。";
        }

        var profileDisplay = normalizedProfiles
            .Select(FormatBindingProfileDisplayName)
            .ToArray();
        var impactedProjects = BuildImpactedProjectLabels(normalizedProfiles);

        if (impactedProjects.Length == 0)
        {
            return $"影响说明：保存后会影响使用 {string.Join(" / ", profileDisplay)} 的已接管项目，当前未发现已登记项目。";
        }

        var impactedPreview = string.Join(" / ", impactedProjects.Take(3));
        var suffix = impactedProjects.Length > 3 ? " / ..." : string.Empty;
        return $"影响说明：保存后会影响使用 {string.Join(" / ", profileDisplay)} 的已接管项目。当前项目：{impactedPreview}{suffix}";
    }

    private void ResetSkillBindingResolutionPreview(BindingPreviewState state)
    {
        Interlocked.Increment(ref _skillBindingResolutionPreviewVersion);
        _pendingSkillBindingResolution = null;
        _pendingSkillBindingPreviewState = state;
        RaiseSkillBindingPreviewStateChanged();
    }

    private void ResetSkillGroupBindingResolutionPreview(BindingPreviewState state)
    {
        Interlocked.Increment(ref _skillGroupBindingResolutionPreviewVersion);
        _pendingSkillGroupBindingResolution = null;
        _pendingSkillGroupBindingPreviewState = state;
        RaiseSkillGroupBindingPreviewStateChanged();
    }

    private static bool HasPendingBindingChanges(
        IEnumerable<string>? persistedProfiles,
        IEnumerable<ProfileBindingOption> draftOptions)
    {
        var persisted = NormalizeBindingProfileIds(persistedProfiles);
        var draft = GetSelectedBindingTargetProfiles(draftOptions);
        return !persisted.SequenceEqual(draft, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetSelectedBindingTargetProfiles(IEnumerable<ProfileBindingOption> options)
    {
        return NormalizeBindingProfileIds(options
            .Where(option => option.IsSelected)
            .Select(option => option.ProfileId));
    }

    private IReadOnlyList<string> GetSelectedSkillPersistedProfileIds()
    {
        return ReconcileBindingDraftState(
            _skillBindingDraftStates,
            SelectedInstalledSkill?.RelativePath,
            SelectedInstalledSkill?.BindingProfileIds).PersistedProfileIds;
    }

    private IReadOnlyList<string> GetSelectedSkillGroupPersistedProfileIds()
    {
        return ReconcileBindingDraftState(
            _skillGroupBindingDraftStates,
            SelectedSkillGroup?.RelativeRootPath,
            SelectedSkillGroup?.BindingProfileIds).PersistedProfileIds;
    }

    private IReadOnlyList<string> GetSelectedSkillBindingDraftProfileIds()
    {
        return ReconcileBindingDraftState(
            _skillBindingDraftStates,
            SelectedInstalledSkill?.RelativePath,
            SelectedInstalledSkill?.BindingProfileIds).DraftProfileIds;
    }

    private IReadOnlyList<string> GetSelectedSkillGroupBindingDraftProfileIds()
    {
        return ReconcileBindingDraftState(
            _skillGroupBindingDraftStates,
            SelectedSkillGroup?.RelativeRootPath,
            SelectedSkillGroup?.BindingProfileIds).DraftProfileIds;
    }

    private void UpdateCurrentSkillBindingDraftState()
    {
        UpdateCurrentBindingDraftState(
            _skillBindingDraftStates,
            SelectedInstalledSkill?.RelativePath,
            SelectedInstalledSkill?.BindingProfileIds,
            SkillBindingProfiles);
    }

    private void UpdateCurrentSkillGroupBindingDraftState()
    {
        UpdateCurrentBindingDraftState(
            _skillGroupBindingDraftStates,
            SelectedSkillGroup?.RelativeRootPath,
            SelectedSkillGroup?.BindingProfileIds,
            SkillGroupBindingProfiles);
    }

    private void UpdateCurrentBindingDraftState(
        IDictionary<string, BindingDraftState> draftStates,
        string? key,
        IEnumerable<string>? persistedProfiles,
        IEnumerable<ProfileBindingOption> draftOptions)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var draftState = ReconcileBindingDraftState(draftStates, key, persistedProfiles);
        draftState.DraftProfileIds = GetSelectedBindingTargetProfiles(draftOptions).ToArray();
    }

    private static string[] NormalizeBindingProfileIds(IEnumerable<string>? profileIds)
    {
        return (profileIds ?? Array.Empty<string>())
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static BindingDraftState ReconcileBindingDraftState(
        IDictionary<string, BindingDraftState> draftStates,
        string? key,
        IEnumerable<string>? persistedProfiles)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BindingDraftState.Empty;
        }

        var normalizedKey = key.Trim();
        var latestPersisted = NormalizeBindingProfileIds(persistedProfiles);
        if (!draftStates.TryGetValue(normalizedKey, out var draftState))
        {
            draftState = new BindingDraftState(latestPersisted);
            draftStates[normalizedKey] = draftState;
            return draftState;
        }

        var draftMatchesPersisted = draftState.PersistedProfileIds.SequenceEqual(
            draftState.DraftProfileIds,
            StringComparer.OrdinalIgnoreCase);

        draftState.PersistedProfileIds = latestPersisted;
        if (draftMatchesPersisted)
        {
            draftState.DraftProfileIds = latestPersisted;
        }

        return draftState;
    }

    private static string BuildOrphanedProfileDisplayName(string profileId)
        => $"已失效分类（{WorkspaceProfiles.NormalizeId(profileId)}）";

    private static bool IsBindingPreviewSaveable(
        BindingPreviewState previewState,
        BindingResolutionPreview? preview)
    {
        return previewState == BindingPreviewState.Resolved
               && preview?.ResolutionStatus == BindingResolutionStatus.Resolved;
    }

    private static bool TryGetBindingPreviewValidationError(
        BindingPreviewState previewState,
        BindingResolutionPreview? preview,
        out string error)
    {
        if (previewState == BindingPreviewState.Failed)
        {
            error = "保存后上游来源解析失败，请稍后重试。";
            return true;
        }

        if (previewState == BindingPreviewState.Resolved
            && preview is not null
            && preview.ResolutionStatus != BindingResolutionStatus.Resolved)
        {
            error = string.IsNullOrWhiteSpace(preview.ResolutionReason)
                ? "当前绑定目标没有可用的上游来源，请调整后再保存。"
                : preview.ResolutionReason;
            return true;
        }

        error = string.Empty;
        return false;
    }

    private string FormatBindingResolutionSource(BindingResolutionPreview preview)
    {
        if (preview.SourceKind == BindingSourceKind.Library)
        {
            return "库中";
        }

        if (string.IsNullOrWhiteSpace(preview.SourceProfileId))
        {
            return "未知来源";
        }

        return FormatBindingProfileDisplayName(preview.SourceProfileId);
    }
    private string FormatBindingPreviewSource(BindingResolutionPreview preview)
    {
        if (preview.ResolutionStatus != BindingResolutionStatus.Resolved)
        {
            return string.IsNullOrWhiteSpace(preview.ResolutionReason) ? "无可用上游来源" : preview.ResolutionReason;
        }

        if (preview.SourceKind == BindingSourceKind.Library)
        {
            return "库中";
        }

        if (string.IsNullOrWhiteSpace(preview.SourceProfileId))
        {
            return string.IsNullOrWhiteSpace(preview.ResolutionReason) ? "未知来源" : preview.ResolutionReason;
        }

        return FormatBindingProfileDisplayName(preview.SourceProfileId);
    }
    private string FormatBindingProfileDisplayName(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return "閺堫亣袙閺?";
        }

        var normalizedProfileId = WorkspaceProfiles.NormalizeId(profileId);
        if (string.Equals(normalizedProfileId, "library", StringComparison.OrdinalIgnoreCase))
        {
            return "搴撲腑";
        }

        var profile = _workspaceProfileCatalog.FirstOrDefault(item =>
            string.Equals(item.Id, normalizedProfileId, StringComparison.OrdinalIgnoreCase));
        return profile is not null
            ? profile.DisplayName
            : $"已失效分类（{normalizedProfileId}）";
    }

    private string BuildSkillBindingSourceDisplay()
    {
        if (SelectedInstalledSkill is null)
        {
            return "保存后上游来源：未选择 Skill";
        }

        var sourceProfile = ResolveSkillBindingSourceProfile(SelectedInstalledSkill);
        return $"保存后上游来源：{WorkspaceProfiles.ToDisplayName(sourceProfile)}";
    }

    private string BuildSkillGroupBindingSourceDisplay()
    {
        if (SelectedSkillGroup is null)
        {
            return "保存后上游来源：未选择分组";
        }

        var fallbackProfile = SelectedSkillGroup.SourceProfileIds.FirstOrDefault() ?? WorkspaceProfiles.GlobalId;
        var sourceProfile = ResolveSkillGroupBindingSourceProfile(SelectedSkillGroup, fallbackProfile);
        return $"保存后上游来源：{WorkspaceProfiles.ToDisplayName(sourceProfile)}";
    }

    private string BuildSkillsContextImpactDisplay()
    {
        var profileId = SkillsPageContext.SelectedTarget?.ProfileId ?? WorkspaceProfiles.GlobalId;
        var profileDisplayName = SkillsPageContext.SelectedTarget?.DisplayName ?? WorkspaceProfiles.GlobalDisplayName;
        var impactedProjects = BuildImpactedProjectLabels(new[] { WorkspaceProfiles.NormalizeId(profileId) });

        if (impactedProjects.Length == 0)
        {
            return $"影响说明：保存后会影响所有已接管且使用“{profileDisplayName}”分类的项目。当前未发现已登记项目。";
        }

        var preview = string.Join(" / ", impactedProjects.Take(3));
        var suffix = impactedProjects.Length > 3 ? " / ..." : string.Empty;
        return $"影响说明：保存后会影响所有已接管且使用“{profileDisplayName}”分类的项目。当前项目：{preview}{suffix}";
    }

    private static string[] ResolveImpactProfileIds(
        IEnumerable<string> targetProfiles,
        BindingResolutionPreview? preview)
    {
        var previewProfiles = NormalizeBindingProfileIds(
            (preview?.MaterializedTargetProfiles ?? Array.Empty<string>())
            .Concat(preview?.RefreshedProfileIds ?? Array.Empty<string>())
            .Concat(preview?.RemovedProfileIds ?? Array.Empty<string>())
            .Where(profile => !string.Equals(
                WorkspaceProfiles.NormalizeId(profile),
                "library",
                StringComparison.OrdinalIgnoreCase)));
        if (previewProfiles.Length > 0)
        {
            return previewProfiles;
        }

        return NormalizeBindingProfileIds(targetProfiles)
            .Where(profile => !string.Equals(profile, "library", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private string[] BuildImpactedProjectLabels(IEnumerable<string> normalizedProfiles)
    {
        var impactedProjects = Projects
            .Where(project => normalizedProfiles.Contains(WorkspaceProfiles.NormalizeId(project.Profile), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (impactedProjects.Length == 0)
        {
            return Array.Empty<string>();
        }

        var duplicateNames = impactedProjects
            .GroupBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return impactedProjects
            .Select(project => duplicateNames.Contains(project.Name)
                ? $"{project.Name} ({project.Path})"
                : project.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private enum BindingPreviewState
    {
        Idle,
        Loading,
        Resolved,
        Failed
    }

    private sealed class BindingDraftState
    {
        public static BindingDraftState Empty { get; } = new(Array.Empty<string>());

        public BindingDraftState(string[] persistedProfileIds)
        {
            PersistedProfileIds = persistedProfileIds;
            DraftProfileIds = persistedProfileIds;
        }

        public string[] PersistedProfileIds { get; set; }

        public string[] DraftProfileIds { get; set; }
    }

    private static string GetSkillGroupRootPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var separatorIndex = normalized.IndexOf('/');
        return separatorIndex < 0 ? normalized : normalized[..separatorIndex];
    }

    private static SkillFolderGroupItem? FindSkillGroup(IEnumerable<SkillFolderGroupItem> groups, string? relativeRootPath)
    {
        if (string.IsNullOrWhiteSpace(relativeRootPath))
        {
            return null;
        }

        return groups.FirstOrDefault(group =>
            string.Equals(group.RelativeRootPath, relativeRootPath, StringComparison.OrdinalIgnoreCase));
    }

    private static McpServerBindingItem? FindMcpServerItem(IEnumerable<McpServerBindingItem> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return items.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject ParseMcpServers(string rawJson)
    {
        try
        {
            var root = JsonNode.Parse(rawJson) as JsonObject;
            return root?["mcpServers"] as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static string NormalizeJsonFragment(JsonNode? node)
    {
        return (node ?? new JsonObject()).ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
