using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private SkillFolderGroupItem? _selectedSkillGroup;
    private McpServerBindingItem? _selectedMcpServer;
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

    public string SelectedSkillBindingSummaryDisplay => SelectedInstalledSkill?.BindingSummaryDisplay ?? "未选择 Skill";

    public string PendingSkillBindingSummaryDisplay => BuildBindingSummaryDisplay(SkillBindingProfiles);

    public string PendingSkillBindingSaveTargetDisplay => BuildBindingSaveTargetDisplay(SkillBindingProfiles);

    public string SelectedSkillGroupBindingSummaryDisplay => SelectedSkillGroup?.ProfileSummary ?? "未选择分组";

    public string PendingSkillGroupBindingSummaryDisplay => BuildBindingSummaryDisplay(SkillGroupBindingProfiles);

    public string PendingSkillGroupBindingSaveTargetDisplay => BuildBindingSaveTargetDisplay(SkillGroupBindingProfiles);

    private void InitializeBindingState()
    {
        SaveSkillBindingsCommand = new AsyncDelegateCommand(SaveSelectedSkillBindingsAsync, CanSaveSelectedSkillBindings);
        SaveSkillGroupBindingsCommand = new AsyncDelegateCommand(SaveSelectedSkillGroupBindingsAsync, CanSaveSelectedSkillGroupBindings);
        SaveMcpServerBindingsCommand = new AsyncDelegateCommand(SaveSelectedMcpServerBindingsAsync, CanSaveSelectedMcpServerBindings);
        ClearMcpServerSelectionCommand = new AsyncDelegateCommand(ClearSelectedMcpServerAsync, () => !IsBusy);
        RefreshBindingOptions();
    }

    private void RefreshBindingOptions()
    {
        RefreshProfileBindingCollection(SkillBindingProfiles, SelectedInstalledSkill?.BindingProfileIds);
        RefreshProfileBindingCollection(SkillGroupBindingProfiles, SelectedSkillGroup?.BindingProfileIds);
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

        ReplaceCollection(
            target,
            _workspaceProfileCatalog
                .OrderBy(profile => profile.SortOrder)
                .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(profile => new ProfileBindingOption(
                    profile.Id,
                    profile.DisplayName,
                    selected.Contains(profile.Id))));

        foreach (var option in target)
        {
            option.PropertyChanged -= OnProfileBindingOptionChanged;
            option.PropertyChanged += OnProfileBindingOptionChanged;
        }
    }

    private void ApplySelectedSkillBindings()
    {
        RefreshProfileBindingCollection(SkillBindingProfiles, SelectedInstalledSkill?.BindingProfileIds);
        RaiseBindingSelectionState();
    }

    private void ApplySelectedSkillGroup()
    {
        RefreshProfileBindingCollection(SkillGroupBindingProfiles, SelectedSkillGroup?.BindingProfileIds);
        RaiseBindingSelectionState();
    }

    private async Task SaveSelectedSkillBindingsAsync()
    {
        if (SelectedInstalledSkill is null)
        {
            SetOperation(false, Text.State.SelectInstalledSkill, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var targets = SkillBindingProfiles
                .Where(option => option.IsSelected)
                .Select(option => option.ProfileId)
                .ToArray();
            var sourceProfile = ResolveSkillBindingSourceProfile(SelectedInstalledSkill);

            var result = await _skillsCatalogService!.SaveSkillBindingsAsync(
                sourceProfile,
                SelectedInstalledSkill.RelativePath,
                targets);
            ApplyOperationResult(result);
            if (result.Success)
            {
                await LoadSkillsAsync(SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile);
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

        var sourceProfile = SelectedSkillGroup.SourceProfileIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sourceProfile))
        {
            SetOperation(false, Text.State.NoSkillGroupSourceProfile, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var targets = SkillGroupBindingProfiles
                .Where(option => option.IsSelected)
                .Select(option => option.ProfileId)
                .ToArray();
            var effectiveSourceProfile = ResolveSkillGroupBindingSourceProfile(SelectedSkillGroup, sourceProfile);

            var result = await _skillsCatalogService!.SaveSkillGroupBindingsAsync(
                effectiveSourceProfile,
                SelectedSkillGroup.RelativeRootPath,
                targets);
            ApplyOperationResult(result);
            if (result.Success)
            {
                await LoadSkillsAsync(SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile);
            }
        });
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
            var targets = McpServerBindingProfiles
                .Where(option => option.IsSelected)
                .Select(option => option.ProfileId)
                .ToArray();

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
        => !IsBusy && _skillsCatalogService is not null && SelectedInstalledSkill is not null;

    private bool CanSaveSelectedSkillGroupBindings()
        => !IsBusy && _skillsCatalogService is not null && SelectedSkillGroup is not null;

    private bool CanSaveSelectedMcpServerBindings()
        => !IsBusy && _mcpControlService is not null;

    private void OnProfileBindingOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileBindingOption.IsSelected))
        {
            RaiseBindingSelectionState();
        }
    }

    private void RaiseBindingSelectionState()
    {
        RaisePropertyChanged(nameof(SelectedSkillBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillBindingSaveTargetDisplay));
        RaisePropertyChanged(nameof(SelectedSkillGroupBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillGroupBindingSummaryDisplay));
        RaisePropertyChanged(nameof(PendingSkillGroupBindingSaveTargetDisplay));
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
        var availableProfiles = group.BindingProfileIds.Count == 0
            ? group.SourceProfileIds
            : group.BindingProfileIds;

        return ResolvePreferredBindingSourceProfile(availableProfiles, fallbackProfile);
    }

    private string ResolvePreferredBindingSourceProfile(IEnumerable<string> availableProfiles, string? fallbackProfile)
    {
        var normalizedProfiles = availableProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var contextProfile = WorkspaceProfiles.NormalizeId(SkillsPageContext.SelectedTarget?.Project?.Profile);
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
