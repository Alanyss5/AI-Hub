using System.Text.Json;
using System.Text.Json.Serialization;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class JsonHubSettingsStore : IHubSettingsStore
{
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly string[] DefaultClients = ["claude", "codex", "antigravity"];
    private readonly string? _hubRoot;
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public JsonHubSettingsStore(string? hubRoot, IDiagnosticLogService? diagnosticLogService = null)
    {
        _hubRoot = hubRoot;
        _diagnosticLogService = diagnosticLogService;
    }

    public Task<HubSettingsRecord> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            return Task.FromResult(new HubSettingsRecord());
        }

        var settingsPath = GetSettingsPath();
        if (!File.Exists(settingsPath))
        {
            return Task.FromResult(CreateDefaultSettings());
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            using var document = JsonDocument.Parse(json);
            HubSettingsRecord? settings;

            if (document.RootElement.TryGetProperty("settings", out var settingsElement))
            {
                settings = settingsElement.Deserialize<HubSettingsRecord>(SerializerOptions);
            }
            else
            {
                settings = JsonSerializer.Deserialize<HubSettingsRecord>(json, SerializerOptions);
                _diagnosticLogService?.RecordInfo("store-settings", "已迁移旧版 hub-settings.json 读取格式。", settingsPath);
            }

            return Task.FromResult(Normalize(settings));
        }
        catch (Exception exception)
        {
            _diagnosticLogService?.RecordWarning("store-settings", "读取 hub-settings.json 失败，已回退到默认设置。", exception.Message);
            return Task.FromResult(CreateDefaultSettings());
        }
    }

    public Task SaveAsync(HubSettingsRecord settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            throw new InvalidOperationException("Hub root is not available.");
        }

        var normalized = Normalize(settings);
        var document = new HubSettingsDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Settings = normalized
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        HubStatePersistence.WriteTextWithBackup(_hubRoot, GetSettingsPath(), json);
        return Task.CompletedTask;
    }

    private HubSettingsRecord CreateDefaultSettings()
    {
        return new HubSettingsRecord
        {
            HubRoot = _hubRoot,
            PreferredClients = DefaultClients.ToArray(),
            AutoStartManagedMcpOnLoad = true,
            AutoCheckSkillUpdatesOnLoad = true,
            AutoSyncSafeSkillsOnLoad = false,
            OnboardedProjectPaths = Array.Empty<string>()
        };
    }

    private HubSettingsRecord Normalize(HubSettingsRecord? settings)
    {
        var current = settings ?? CreateDefaultSettings();
        var preferredClients = current.PreferredClients is { Length: > 0 }
            ? current.PreferredClients
            : DefaultClients.ToArray();

        return current with
        {
            HubRoot = string.IsNullOrWhiteSpace(current.HubRoot) ? _hubRoot : current.HubRoot,
            PreferredClients = preferredClients,
            AutoStartManagedMcpOnLoad = current.AutoStartManagedMcpOnLoad,
            AutoCheckSkillUpdatesOnLoad = current.AutoCheckSkillUpdatesOnLoad,
            AutoSyncSafeSkillsOnLoad = current.AutoSyncSafeSkillsOnLoad,
            OnboardedProjectPaths = current.OnboardedProjectPaths ?? Array.Empty<string>()
        };
    }

    private string GetSettingsPath()
    {
        return Path.Combine(_hubRoot!, "config", "hub-settings.json");
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

    private sealed class HubSettingsDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public HubSettingsRecord Settings { get; set; } = new();
    }
}
