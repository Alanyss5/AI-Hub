using System.Text.Json;
using System.Text.Json.Serialization;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class JsonMcpRuntimeStore : IMcpRuntimeStore
{
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly string? _hubRoot;
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public JsonMcpRuntimeStore(string? hubRoot, IDiagnosticLogService? diagnosticLogService = null)
    {
        _hubRoot = hubRoot;
        _diagnosticLogService = diagnosticLogService;
    }

    public Task<IReadOnlyList<McpRuntimeRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            return Task.FromResult<IReadOnlyList<McpRuntimeRecord>>(Array.Empty<McpRuntimeRecord>());
        }

        var runtimePath = GetRuntimePath();
        if (!File.Exists(runtimePath))
        {
            return Task.FromResult<IReadOnlyList<McpRuntimeRecord>>(Array.Empty<McpRuntimeRecord>());
        }

        try
        {
            var json = File.ReadAllText(runtimePath);
            var document = JsonSerializer.Deserialize<McpRuntimeDocument>(json, SerializerOptions);
            if (document is not null)
            {
                return Task.FromResult<IReadOnlyList<McpRuntimeRecord>>(document.ManagedProcesses.Select(NormalizeRecord).ToArray());
            }

            var legacy = JsonSerializer.Deserialize<LegacyMcpRuntimeDocument>(json, SerializerOptions) ?? new LegacyMcpRuntimeDocument();
            _diagnosticLogService?.RecordInfo("store-mcp-runtime", "已迁移旧版 runtime.json 读取格式。", runtimePath);
            return Task.FromResult<IReadOnlyList<McpRuntimeRecord>>(legacy.ManagedProcesses.Select(NormalizeRecord).ToArray());
        }
        catch (Exception exception)
        {
            _diagnosticLogService?.RecordWarning("store-mcp-runtime", "读取 runtime.json 失败，已回退为空列表。", exception.Message);
            return Task.FromResult<IReadOnlyList<McpRuntimeRecord>>(Array.Empty<McpRuntimeRecord>());
        }
    }

    public Task SaveAllAsync(IReadOnlyList<McpRuntimeRecord> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            throw new InvalidOperationException("Hub root is not available.");
        }

        var document = new McpRuntimeDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            ManagedProcesses = records.Select(NormalizeRecord).ToList()
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        HubStatePersistence.WriteTextWithBackup(_hubRoot, GetRuntimePath(), json);
        return Task.CompletedTask;
    }

    private string GetRuntimePath()
    {
        return Path.Combine(_hubRoot!, "mcp", "runtime.json");
    }

    private static McpRuntimeRecord NormalizeRecord(McpRuntimeRecord? record)
    {
        var current = record ?? new McpRuntimeRecord();
        return current with
        {
            Name = current.Name?.Trim() ?? string.Empty,
            Command = current.Command?.Trim() ?? string.Empty,
            Arguments = current.Arguments?.Where(argument => !string.IsNullOrWhiteSpace(argument)).Select(argument => argument.Trim()).ToArray() ?? Array.Empty<string>(),
            WorkingDirectory = string.IsNullOrWhiteSpace(current.WorkingDirectory) ? null : current.WorkingDirectory,
            EnvironmentVariables = current.EnvironmentVariables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            HealthCheckUrl = string.IsNullOrWhiteSpace(current.HealthCheckUrl) ? null : current.HealthCheckUrl.Trim(),
            HealthCheckTimeoutSeconds = current.HealthCheckTimeoutSeconds <= 0 ? 5 : current.HealthCheckTimeoutSeconds
        };
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class McpRuntimeDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public List<McpRuntimeRecord> ManagedProcesses { get; set; } = new();
    }

    private sealed class LegacyMcpRuntimeDocument
    {
        public List<McpRuntimeRecord> ManagedProcesses { get; set; } = new();
    }
}