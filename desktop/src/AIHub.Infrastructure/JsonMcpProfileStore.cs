using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class JsonMcpProfileStore : IMcpProfileStore
{
    private static readonly ProfileKind[] Profiles =
    [
        ProfileKind.Global,
        ProfileKind.Frontend,
        ProfileKind.Backend
    ];

    private readonly string? _hubRoot;
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public JsonMcpProfileStore(string? hubRoot, IDiagnosticLogService? diagnosticLogService = null)
    {
        _hubRoot = hubRoot;
        _diagnosticLogService = diagnosticLogService;
    }

    public Task<IReadOnlyList<McpProfileRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            return Task.FromResult<IReadOnlyList<McpProfileRecord>>(Array.Empty<McpProfileRecord>());
        }

        var profiles = Profiles.Select(CreateProfileRecord).ToArray();
        return Task.FromResult<IReadOnlyList<McpProfileRecord>>(profiles);
    }

    public Task SaveManifestAsync(ProfileKind profile, string rawJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_hubRoot))
        {
            throw new InvalidOperationException("Hub root is not available.");
        }

        HubStatePersistence.WriteTextWithBackup(_hubRoot, GetManifestPath(profile), rawJson);
        return Task.CompletedTask;
    }

    private McpProfileRecord CreateProfileRecord(ProfileKind profile)
    {
        var manifestPath = GetManifestPath(profile);
        var rawJson = File.Exists(manifestPath)
            ? File.ReadAllText(manifestPath)
            : DefaultManifestJson();

        var serverNames = ParseServerNames(rawJson);
        var generatedClients = new[]
        {
            CreateGeneratedClient("Claude", Path.Combine(_hubRoot!, "mcp", "generated", "claude", profile.ToStorageValue() + ".mcp.json")),
            CreateGeneratedClient("Codex", Path.Combine(_hubRoot!, "mcp", "generated", "codex", profile.ToStorageValue() + ".config.toml")),
            CreateGeneratedClient("Antigravity", Path.Combine(_hubRoot!, "mcp", "generated", "antigravity", profile.ToStorageValue() + ".mcp.json"))
        };

        return new McpProfileRecord(
            profile,
            manifestPath,
            rawJson,
            serverNames,
            generatedClients);
    }

    private static McpGeneratedClientConfig CreateGeneratedClient(string clientName, string filePath)
    {
        var content = File.Exists(filePath)
            ? File.ReadAllText(filePath)
            : "尚未生成配置。";

        return new McpGeneratedClientConfig(clientName, filePath, content);
    }

    private string GetManifestPath(ProfileKind profile)
    {
        return Path.Combine(_hubRoot!, "mcp", "manifest", profile.ToStorageValue() + ".json");
    }

    private static string[] ParseServerNames(string rawJson)
    {
        try
        {
            var node = JsonNode.Parse(rawJson) as JsonObject;
            var serverObject = node?["mcpServers"] as JsonObject;
            return serverObject?.Select(entry => entry.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string DefaultManifestJson()
    {
        return "{\r\n  \"mcpServers\": {}\r\n}";
    }
}