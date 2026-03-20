using System.Text.Json;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class JsonProjectRegistry : IProjectRegistry
{
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly string? _hubRoot;
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public JsonProjectRegistry(string? hubRoot, IDiagnosticLogService? diagnosticLogService = null)
    {
        _hubRoot = hubRoot;
        _diagnosticLogService = diagnosticLogService;
    }

    public Task<IReadOnlyList<ProjectRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            return Task.FromResult<IReadOnlyList<ProjectRecord>>(Array.Empty<ProjectRecord>());
        }

        var registryPath = GetRegistryPath();
        if (!File.Exists(registryPath))
        {
            return Task.FromResult<IReadOnlyList<ProjectRecord>>(Array.Empty<ProjectRecord>());
        }

        try
        {
            var json = File.ReadAllText(registryPath);
            var document = JsonSerializer.Deserialize<ProjectRegistryDocument>(json, SerializerOptions);
            if (document is not null)
            {
                return Task.FromResult<IReadOnlyList<ProjectRecord>>(NormalizeProjects(document.Projects));
            }

            var legacy = JsonSerializer.Deserialize<LegacyProjectRegistryDocument>(json, SerializerOptions) ?? new LegacyProjectRegistryDocument();
            _diagnosticLogService?.RecordInfo("store-projects", "已迁移旧版 projects.json 读取格式。", registryPath);
            return Task.FromResult<IReadOnlyList<ProjectRecord>>(NormalizeProjects(legacy.Projects));
        }
        catch (Exception exception)
        {
            _diagnosticLogService?.RecordWarning("store-projects", "读取 projects.json 失败，已回退为空列表。", exception.Message);
            return Task.FromResult<IReadOnlyList<ProjectRecord>>(Array.Empty<ProjectRecord>());
        }
    }

    public Task SaveAllAsync(IReadOnlyList<ProjectRecord> projects, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            throw new InvalidOperationException("Hub root is not available.");
        }

        var document = new ProjectRegistryDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Projects = NormalizeProjects(projects).ToList()
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        HubStatePersistence.WriteTextWithBackup(_hubRoot, GetRegistryPath(), json);
        return Task.CompletedTask;
    }

    private string GetRegistryPath()
    {
        return Path.Combine(_hubRoot!, "projects", "projects.json");
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        return options;
    }

    private static IReadOnlyList<ProjectRecord> NormalizeProjects(IEnumerable<ProjectRecord>? projects)
    {
        return (projects ?? Array.Empty<ProjectRecord>())
            .Select(project => project with { Profile = WorkspaceProfiles.NormalizeId(project.Profile) })
            .ToList();
    }

    private sealed class ProjectRegistryDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public List<ProjectRecord> Projects { get; set; } = new();
    }

    private sealed class LegacyProjectRegistryDocument
    {
        public List<ProjectRecord> Projects { get; set; } = new();
    }
}
