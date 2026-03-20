using System.Text.Json;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed class WorkspaceProfileService
{
    private readonly IHubRootLocator _hubRootLocator;
    private readonly Func<string?, IWorkspaceProfileCatalogStore> _profileCatalogStoreFactory;
    private readonly Func<string?, IProjectRegistry> _projectRegistryFactory;
    private readonly Func<string?, IHubSettingsStore> _hubSettingsStoreFactory;
    private readonly Func<string?, IMcpProfileStore> _mcpProfileStoreFactory;

    public WorkspaceProfileService(
        IHubRootLocator hubRootLocator,
        Func<string?, IWorkspaceProfileCatalogStore> profileCatalogStoreFactory,
        Func<string?, IProjectRegistry> projectRegistryFactory,
        Func<string?, IHubSettingsStore> hubSettingsStoreFactory,
        Func<string?, IMcpProfileStore> mcpProfileStoreFactory)
    {
        _hubRootLocator = hubRootLocator;
        _profileCatalogStoreFactory = profileCatalogStoreFactory;
        _projectRegistryFactory = projectRegistryFactory;
        _hubSettingsStoreFactory = hubSettingsStoreFactory;
        _mcpProfileStoreFactory = mcpProfileStoreFactory;
    }

    public async Task<WorkspaceProfileCatalogSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return new WorkspaceProfileCatalogSnapshot(resolution, Array.Empty<WorkspaceProfileDescriptor>());
        }

        var profiles = await _profileCatalogStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
        var descriptors = await BuildDescriptorsAsync(resolution.RootPath, profiles, cancellationToken);
        return new WorkspaceProfileCatalogSnapshot(resolution, descriptors);
    }

    public async Task<OperationResult> SaveAsync(
        string? originalId,
        WorkspaceProfileRecord draft,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法保存分类。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var store = _profileCatalogStoreFactory(resolution.RootPath);
        var profiles = (await store.LoadAsync(cancellationToken)).ToList();
        var normalizedOriginalId = string.IsNullOrWhiteSpace(originalId) ? null : WorkspaceProfiles.NormalizeId(originalId);
        var normalized = NormalizeProfileRecord(draft, profiles.Count);

        if (profiles.Any(profile =>
                !string.Equals(profile.Id, normalizedOriginalId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.Id, normalized.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Fail("已存在同名分类。", normalized.Id);
        }

        if (!string.IsNullOrWhiteSpace(normalizedOriginalId))
        {
            var existing = profiles.FirstOrDefault(profile => string.Equals(profile.Id, normalizedOriginalId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return OperationResult.Fail("未找到要编辑的分类。", normalizedOriginalId);
            }

            if (!string.Equals(existing.Id, normalized.Id, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail("当前版本仅支持修改分类显示名，不支持直接修改分类标识。", existing.Id);
            }

            if (!existing.IsDeletable && !string.Equals(existing.Id, normalized.Id, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail("内置全局分类不允许修改标识。", existing.Id);
            }

            profiles.Remove(existing);
            profiles.Add(normalized with
            {
                IsBuiltin = existing.IsBuiltin,
                IsDeletable = existing.IsDeletable,
                SortOrder = existing.SortOrder
            });
        }
        else
        {
            profiles.Add(normalized with
            {
                SortOrder = profiles.Count == 0 ? 0 : profiles.Max(profile => profile.SortOrder) + 1
            });
        }

        await store.SaveAsync(profiles, cancellationToken);
        return OperationResult.Ok("分类已保存。", normalized.Id);
    }

    public async Task<OperationResult> DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法删除分类。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedProfileId = WorkspaceProfiles.NormalizeId(profileId);
        if (WorkspaceProfiles.IsGlobal(normalizedProfileId))
        {
            return OperationResult.Fail("global 分类是默认基础层，不能删除。", normalizedProfileId);
        }

        var store = _profileCatalogStoreFactory(resolution.RootPath);
        var profiles = (await store.LoadAsync(cancellationToken)).ToList();
        var target = profiles.FirstOrDefault(profile => string.Equals(profile.Id, normalizedProfileId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return OperationResult.Fail("未找到要删除的分类。", normalizedProfileId);
        }

        if (!target.IsDeletable)
        {
            return OperationResult.Fail("该分类不允许删除。", normalizedProfileId);
        }

        var descriptor = (await BuildDescriptorsAsync(resolution.RootPath, [target], cancellationToken)).Single();
        if (descriptor.HasReferences)
        {
            return OperationResult.Fail("分类仍被现有配置引用，不能删除。", descriptor.UsageSummary);
        }

        profiles.Remove(target);
        await store.SaveAsync(profiles, cancellationToken);
        return OperationResult.Ok("分类已删除。", normalizedProfileId);
    }

    private async Task<IReadOnlyList<WorkspaceProfileDescriptor>> BuildDescriptorsAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceProfileRecord> profiles,
        CancellationToken cancellationToken)
    {
        var normalizedProfiles = profiles
            .Select((profile, index) => NormalizeProfileRecord(profile, index))
            .OrderBy(profile => profile.SortOrder)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projects = await _projectRegistryFactory(hubRoot).GetAllAsync(cancellationToken);
        var settings = await _hubSettingsStoreFactory(hubRoot).LoadAsync(cancellationToken);
        var mcpProfiles = await _mcpProfileStoreFactory(hubRoot).GetAllAsync(cancellationToken);
        var skillSources = await LoadProfileCountsAsync(GetSourcesPath(hubRoot), "sources", "profile", cancellationToken);
        var skillInstalls = await LoadProfileCountsAsync(GetSkillInstallsPath(hubRoot), "installs", "profile", cancellationToken);
        var skillStates = await LoadProfileCountsAsync(GetSkillStatesPath(hubRoot), "states", "profile", cancellationToken);

        return normalizedProfiles
            .Select(profile => new WorkspaceProfileDescriptor(
                profile,
                projects.Count(project => string.Equals(WorkspaceProfiles.NormalizeId(project.Profile), profile.Id, StringComparison.OrdinalIgnoreCase)),
                GetCount(skillSources, profile.Id),
                GetCount(skillInstalls, profile.Id),
                GetCount(skillStates, profile.Id),
                CountSkillDirectories(hubRoot, profile.Id),
                mcpProfiles.FirstOrDefault(item => string.Equals(item.Profile, profile.Id, StringComparison.OrdinalIgnoreCase))?.ServerNames.Count ?? 0,
                string.Equals(WorkspaceProfiles.NormalizeId(settings.DefaultProfile), profile.Id, StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                CountProfileAssets(hubRoot, "claude", "commands", profile.Id),
                CountProfileAssets(hubRoot, "claude", "agents", profile.Id)))
            .ToArray();
    }

    private static WorkspaceProfileRecord NormalizeProfileRecord(WorkspaceProfileRecord record, int fallbackSortOrder)
    {
        var normalizedId = WorkspaceProfiles.NormalizeId(record.Id);
        var isGlobal = WorkspaceProfiles.IsGlobal(normalizedId);
        return record with
        {
            Id = normalizedId,
            DisplayName = WorkspaceProfiles.NormalizeDisplayName(record.DisplayName, normalizedId),
            IsBuiltin = isGlobal || record.IsBuiltin,
            IsDeletable = !isGlobal && (record.IsDeletable || !record.IsBuiltin),
            SortOrder = record.SortOrder < 0 ? fallbackSortOrder : record.SortOrder
        };
    }

    private static int GetCount(IReadOnlyDictionary<string, int> counts, string profileId)
    {
        return counts.TryGetValue(profileId, out var count) ? count : 0;
    }

    private static int CountSkillDirectories(string hubRoot, string profileId)
    {
        var profileRoot = Path.Combine(hubRoot, "skills", profileId);
        if (!Directory.Exists(profileRoot))
        {
            return 0;
        }

        return Directory
            .EnumerateFiles(profileRoot, "SKILL.md", SearchOption.AllDirectories)
            .Count();
    }

    private static int CountProfileAssets(string hubRoot, params string[] segments)
    {
        var profileRoot = Path.Combine(new[] { hubRoot }.Concat(segments).ToArray());
        if (!Directory.Exists(profileRoot))
        {
            return 0;
        }

        return Directory
            .EnumerateFiles(profileRoot, "*", SearchOption.AllDirectories)
            .Count();
    }

    private static async Task<IReadOnlyDictionary<string, int>> LoadProfileCountsAsync(
        string filePath,
        string collectionName,
        string propertyName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath, cancellationToken));
        if (!TryGetPropertyCaseInsensitive(document.RootElement, collectionName, out var collection) || collection.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in collection.EnumerateArray())
        {
            if (!TryGetPropertyCaseInsensitive(item, propertyName, out var profileProperty) || profileProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var profileId = WorkspaceProfiles.NormalizeId(profileProperty.GetString());
            counts[profileId] = GetCount(counts, profileId) + 1;
        }

        return counts;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string GetSourcesPath(string hubRoot) => Path.Combine(hubRoot, "skills", "sources.json");

    private static string GetSkillInstallsPath(string hubRoot) => Path.Combine(hubRoot, "config", "skills-installs.json");

    private static string GetSkillStatesPath(string hubRoot) => Path.Combine(hubRoot, "config", "skills-state.json");
}
