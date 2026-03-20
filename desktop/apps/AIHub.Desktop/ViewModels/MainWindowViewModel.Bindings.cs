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
        RefreshProfileBindingCollection(SkillBindingProfiles, SelectedInstalledSkill?.Profile);
        RefreshProfileBindingCollection(SkillGroupBindingProfiles, SelectedSkillGroup?.ProfileIds);
        RefreshProfileBindingCollection(McpServerBindingProfiles, SelectedMcpServer?.ProfileIds);
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
    }

    private void RefreshSkillGroups(IEnumerable<InstalledSkillRecord> skills)
    {
        var groupItems = skills
            .GroupBy(skill => GetSkillGroupRootPath(skill.RelativePath), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var skillItems = group
                    .OrderBy(skill => skill.Profile, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(skill => skill.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var profileItems = skillItems
                    .Select(skill => new { skill.Profile, skill.ProfileDisplay })
                    .DistinctBy(item => item.Profile, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new SkillFolderGroupItem(
                    group.Key,
                    skillItems,
                    profileItems.Select(item => item.Profile).ToArray(),
                    profileItems.Select(item => item.ProfileDisplay).ToArray(),
                    skillItems.Select(skill => skill.RelativePath).ToArray());
            })
            .OrderBy(group => group.RelativeRootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceCollection(SkillGroups, groupItems);

        var preferredGroupPath = GetSkillGroupRootPath(SelectedInstalledSkill?.RelativePath)
            ?? SelectedSkillGroup?.RelativeRootPath;
        SelectedSkillGroup = FindSkillGroup(groupItems, preferredGroupPath) ?? groupItems.FirstOrDefault();
    }

    private void ApplySelectedSkillBindings()
    {
        RefreshProfileBindingCollection(
            SkillBindingProfiles,
            SelectedInstalledSkill is null
                ? Array.Empty<string>()
                : _installedSkillCache
                    .Where(skill => string.Equals(skill.RelativePath, SelectedInstalledSkill.RelativePath, StringComparison.OrdinalIgnoreCase))
                    .Select(skill => skill.Profile));
    }

    private void ApplySelectedSkillGroup()
    {
        RefreshProfileBindingCollection(SkillGroupBindingProfiles, SelectedSkillGroup?.ProfileIds);
    }

    private async Task SaveSelectedSkillBindingsAsync()
    {
        if (SelectedInstalledSkill is null)
        {
            SetOperation(false, "请先选择一个 Skill。", string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var targets = SkillBindingProfiles
                .Where(option => option.IsSelected)
                .Select(option => option.ProfileId)
                .ToArray();

            var result = await _skillsCatalogService!.SaveSkillBindingsAsync(
                SelectedInstalledSkill.Profile,
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
            SetOperation(false, "请先选择一个 Skill 仓库或目录。", string.Empty);
            return;
        }

        var sourceProfile = SelectedSkillGroup.ProfileIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sourceProfile))
        {
            SetOperation(false, "当前 Skill 目录没有可复制的源分类。", string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var targets = SkillGroupBindingProfiles
                .Where(option => option.IsSelected)
                .Select(option => option.ProfileId)
                .ToArray();

            var result = await _skillsCatalogService!.SaveSkillGroupBindingsAsync(
                sourceProfile,
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
            SetOperation(false, "请先选择或输入 MCP server 名称。", string.Empty);
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
