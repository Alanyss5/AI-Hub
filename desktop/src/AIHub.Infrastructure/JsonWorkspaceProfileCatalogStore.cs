using System.Text.Json;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class JsonWorkspaceProfileCatalogStore : IWorkspaceProfileCatalogStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string? _hubRoot;
    private readonly ISourcePathLayout _sourcePathLayout;

    public JsonWorkspaceProfileCatalogStore(string? hubRoot, ISourcePathLayout? sourcePathLayout = null)
    {
        _hubRoot = hubRoot;
        _sourcePathLayout = sourcePathLayout ?? SourceLayoutMigrationService.DefaultLayout;
    }

    public Task<IReadOnlyList<WorkspaceProfileRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            return Task.FromResult<IReadOnlyList<WorkspaceProfileRecord>>(WorkspaceProfiles.CreateDefaultCatalog());
        }

        var catalogPath = GetCatalogPath();
        if (!File.Exists(catalogPath))
        {
            var legacyPath = GetLegacyCatalogPath();
            if (!File.Exists(legacyPath))
            {
                return Task.FromResult<IReadOnlyList<WorkspaceProfileRecord>>(WorkspaceProfiles.CreateDefaultCatalog());
            }

            catalogPath = legacyPath;
        }

        try
        {
            var json = File.ReadAllText(catalogPath);
            var document = JsonSerializer.Deserialize<WorkspaceProfileCatalogDocument>(json, SerializerOptions) ?? new WorkspaceProfileCatalogDocument();
            return Task.FromResult<IReadOnlyList<WorkspaceProfileRecord>>(MergeWithDefaults(document.Profiles ?? []));
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<WorkspaceProfileRecord>>(WorkspaceProfiles.CreateDefaultCatalog());
        }
    }

    public Task SaveAsync(IReadOnlyList<WorkspaceProfileRecord> profiles, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            throw new InvalidOperationException("Hub root is not available.");
        }

        var normalizedProfiles = MergeWithDefaults(profiles);
        var document = new WorkspaceProfileCatalogDocument
        {
            Profiles = normalizedProfiles.ToList()
        };

        var path = GetCatalogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        HubStatePersistence.WriteTextWithBackup(_hubRoot, path, JsonSerializer.Serialize(document, SerializerOptions));
        return Task.CompletedTask;
    }

    private string GetCatalogPath()
    {
        var sourceRoot = _sourcePathLayout.GetCompanySourceRoot(_hubRoot!);
        Directory.CreateDirectory(_sourcePathLayout.GetRegistryRoot(sourceRoot));
        return _sourcePathLayout.GetProfileCatalogPath(sourceRoot);
    }

    private string GetLegacyCatalogPath()
    {
        return Path.Combine(_hubRoot!, "config", "profile-catalog.json");
    }

    private static IReadOnlyList<WorkspaceProfileRecord> MergeWithDefaults(IReadOnlyList<WorkspaceProfileRecord> profiles)
    {
        var merged = WorkspaceProfiles.CreateDefaultCatalog().ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            var normalizedId = WorkspaceProfiles.NormalizeId(profile.Id);
            var defaultProfile = merged.TryGetValue(normalizedId, out var existing) ? existing : null;
            merged[normalizedId] = profile with
            {
                Id = normalizedId,
                DisplayName = WorkspaceProfiles.NormalizeDisplayName(profile.DisplayName, normalizedId),
                IsBuiltin = defaultProfile?.IsBuiltin ?? profile.IsBuiltin,
                IsDeletable = WorkspaceProfiles.IsGlobal(normalizedId)
                    ? false
                    : (defaultProfile?.IsDeletable ?? profile.IsDeletable || !profile.IsBuiltin),
                SortOrder = profile.SortOrder < 0 ? defaultProfile?.SortOrder ?? merged.Count : profile.SortOrder
            };
        }

        return merged.Values
            .OrderBy(profile => profile.SortOrder)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class WorkspaceProfileCatalogDocument
    {
        public List<WorkspaceProfileRecord>? Profiles { get; set; }
    }
}
