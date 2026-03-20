using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace AIHub.Infrastructure;

internal static class LayeredWorkspaceMaterializer
{
    public static string GetPersonalRoot(string userHome)
    {
        return Path.Combine(Path.GetFullPath(userHome), "AI-Personal");
    }

    public static string GetEffectiveProfileRoot(string hubRoot, string profileId)
    {
        return Path.Combine(Path.GetFullPath(hubRoot), ".runtime", "effective", WorkspaceProfiles.Normalize(profileId));
    }

    public static void EnsurePrivateLayerStructure(string personalRoot)
    {
        var normalizedPersonalRoot = Path.GetFullPath(personalRoot);
        foreach (var profile in WorkspaceProfiles.All)
        {
            Directory.CreateDirectory(Path.Combine(normalizedPersonalRoot, "skills", profile));
            Directory.CreateDirectory(Path.Combine(normalizedPersonalRoot, "claude", "commands", profile));
            Directory.CreateDirectory(Path.Combine(normalizedPersonalRoot, "claude", "agents", profile));
            Directory.CreateDirectory(Path.Combine(normalizedPersonalRoot, "claude", "settings"));
            Directory.CreateDirectory(Path.Combine(normalizedPersonalRoot, "mcp", "manifest"));
        }
    }

    public static EffectiveWorkspaceProfile MaterializeProfile(string hubRoot, string personalRoot, string profileId)
    {
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var normalizedPersonalRoot = Path.GetFullPath(personalRoot);
        var normalizedProfileId = WorkspaceProfiles.Normalize(profileId);
        EnsurePrivateLayerStructure(normalizedPersonalRoot);

        var effectiveRoot = GetEffectiveProfileRoot(normalizedHubRoot, normalizedProfileId);
        RecreateDirectory(effectiveRoot);

        var skillsRoot = Path.Combine(effectiveRoot, "skills");
        var commandsRoot = Path.Combine(effectiveRoot, "claude", "commands");
        var agentsRoot = Path.Combine(effectiveRoot, "claude", "agents");
        var mcpRoot = Path.Combine(effectiveRoot, "mcp");
        Directory.CreateDirectory(skillsRoot);
        Directory.CreateDirectory(commandsRoot);
        Directory.CreateDirectory(agentsRoot);
        Directory.CreateDirectory(mcpRoot);

        foreach (var layer in BuildLayers(normalizedHubRoot, normalizedPersonalRoot, normalizedProfileId))
        {
            CopyDirectoryContents(layer.SkillsRoot, skillsRoot);
            CopyDirectoryContents(layer.CommandsRoot, commandsRoot);
            CopyDirectoryContents(layer.AgentsRoot, agentsRoot);
        }

        var settingsContent = BuildEffectiveSettingsContent(normalizedHubRoot, normalizedPersonalRoot, normalizedProfileId);
        var settingsPath = Path.Combine(effectiveRoot, "claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, settingsContent, new UTF8Encoding(false));

        var servers = BuildEffectiveServerMap(normalizedHubRoot, normalizedPersonalRoot, normalizedProfileId);
        var claudeMcpPath = Path.Combine(mcpRoot, "claude.mcp.json");
        var codexMcpPath = Path.Combine(mcpRoot, "codex.config.toml");
        var antigravityMcpPath = Path.Combine(mcpRoot, "antigravity.mcp.json");
        WriteJsonManifest(claudeMcpPath, servers);
        WriteTomlManifest(codexMcpPath, servers);
        WriteJsonManifest(antigravityMcpPath, servers);

        return new EffectiveWorkspaceProfile(
            normalizedProfileId,
            effectiveRoot,
            skillsRoot,
            commandsRoot,
            agentsRoot,
            settingsPath,
            claudeMcpPath,
            codexMcpPath,
            antigravityMcpPath,
            servers);
    }

    public static OperationResult GenerateLegacyMcpOutputs(string hubRoot, string personalRoot)
        => GenerateLegacyMcpOutputs(hubRoot, personalRoot, WorkspaceProfiles.All);

    public static OperationResult GenerateLegacyMcpOutputs(string hubRoot, string personalRoot, IEnumerable<string> profiles)
    {
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var normalizedPersonalRoot = Path.GetFullPath(personalRoot);
        var selectedProfiles = profiles
            .Select(WorkspaceProfiles.Normalize)
            .Distinct()
            .ToArray();
        var claudeOutput = Path.Combine(normalizedHubRoot, "mcp", "generated", "claude");
        var codexOutput = Path.Combine(normalizedHubRoot, "mcp", "generated", "codex");
        var antigravityOutput = Path.Combine(normalizedHubRoot, "mcp", "generated", "antigravity");
        Directory.CreateDirectory(claudeOutput);
        Directory.CreateDirectory(codexOutput);
        Directory.CreateDirectory(antigravityOutput);

        foreach (var profile in selectedProfiles)
        {
            var effective = MaterializeProfile(normalizedHubRoot, normalizedPersonalRoot, profile);
            File.Copy(effective.ClaudeMcpPath, Path.Combine(claudeOutput, profile + ".mcp.json"), overwrite: true);
            File.Copy(effective.CodexMcpPath, Path.Combine(codexOutput, profile + ".config.toml"), overwrite: true);
            File.Copy(effective.AntigravityMcpPath, Path.Combine(antigravityOutput, profile + ".mcp.json"), overwrite: true);
        }

        return OperationResult.Ok(
            "MCP generated 配置已完成刷新。",
            string.Join(Environment.NewLine, new[]
            {
                "Claude 输出：" + claudeOutput,
                "Codex 输出：" + codexOutput,
                "Antigravity 输出：" + antigravityOutput,
                "Profile 数量：" + selectedProfiles.Length
            }));
    }

    public static IReadOnlyDictionary<string, McpServerDefinitionRecord> BuildEffectiveServerMap(
        string hubRoot,
        string personalRoot,
        string profileId)
    {
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifestPath in EnumerateManifestLayerPaths(Path.GetFullPath(hubRoot), Path.GetFullPath(personalRoot), WorkspaceProfiles.Normalize(profileId)))
        {
            foreach (var entry in ParseManifest(manifestPath))
            {
                servers[entry.Key] = entry.Value;
            }
        }

        return servers;
    }

    private static IEnumerable<LayerDescriptor> BuildLayers(string hubRoot, string personalRoot, string profileId)
    {
        yield return CreateLayer(hubRoot, WorkspaceProfiles.Global);
        yield return CreateLayer(personalRoot, WorkspaceProfiles.Global);

        if (!WorkspaceProfiles.IsGlobal(profileId))
        {
            yield return CreateLayer(hubRoot, profileId);
            yield return CreateLayer(personalRoot, profileId);
        }
    }

    private static IEnumerable<string> EnumerateSettingsLayerPaths(string hubRoot, string personalRoot, string profileId)
    {
        yield return Path.Combine(hubRoot, "claude", "settings", "global.settings.json");
        yield return Path.Combine(personalRoot, "claude", "settings", "global.settings.json");

        if (!WorkspaceProfiles.IsGlobal(profileId))
        {
            yield return Path.Combine(hubRoot, "claude", "settings", profileId + ".settings.json");
            yield return Path.Combine(personalRoot, "claude", "settings", profileId + ".settings.json");
        }
    }

    private static IEnumerable<string> EnumerateManifestLayerPaths(string hubRoot, string personalRoot, string profileId)
    {
        yield return Path.Combine(hubRoot, "mcp", "manifest", "global.json");
        yield return Path.Combine(personalRoot, "mcp", "manifest", "global.json");

        if (!WorkspaceProfiles.IsGlobal(profileId))
        {
            yield return Path.Combine(hubRoot, "mcp", "manifest", profileId + ".json");
            yield return Path.Combine(personalRoot, "mcp", "manifest", profileId + ".json");
        }
    }

    private static LayerDescriptor CreateLayer(string root, string profileId)
    {
        return new LayerDescriptor(
            Path.Combine(root, "skills", profileId),
            Path.Combine(root, "claude", "commands", profileId),
            Path.Combine(root, "claude", "agents", profileId));
    }

    private static string BuildEffectiveSettingsContent(string hubRoot, string personalRoot, string profileId)
    {
        JsonObject? merged = null;
        foreach (var settingsPath in EnumerateSettingsLayerPaths(hubRoot, personalRoot, profileId))
        {
            if (!File.Exists(settingsPath))
            {
                continue;
            }

            var current = ParseTemplateJson(settingsPath, hubRoot);
            merged = merged is null ? current : MergeJsonObjects(merged, current);
        }

        merged ??= new JsonObject();
        return merged.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static JsonObject ParseTemplateJson(string path, string hubRoot)
    {
        var rawContent = File.ReadAllText(path, Encoding.UTF8)
            .Replace("__AI_HUB_ROOT_JSON__", hubRoot.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal);
        return JsonNode.Parse(rawContent) as JsonObject ?? new JsonObject();
    }

    private static JsonObject MergeJsonObjects(JsonObject target, JsonObject source)
    {
        foreach (var entry in source)
        {
            if (entry.Value is JsonObject sourceObject
                && target[entry.Key] is JsonObject targetObject)
            {
                target[entry.Key] = MergeJsonObjects(targetObject, sourceObject);
                continue;
            }

            target[entry.Key] = entry.Value?.DeepClone();
        }

        return target;
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseManifest(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        return ParseJsonServers(root["mcpServers"] as JsonObject);
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseJsonServers(JsonObject? serversObject)
    {
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        if (serversObject is null)
        {
            return servers;
        }

        foreach (var entry in serversObject)
        {
            if (entry.Value is not JsonObject serverObject)
            {
                continue;
            }

            var args = serverObject["args"] is JsonArray argsArray
                ? argsArray
                    .Select(item => item?.GetValue<string>())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .ToArray()
                : Array.Empty<string>();
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (serverObject["env"] is JsonObject envObject)
            {
                foreach (var envEntry in envObject)
                {
                    env[envEntry.Key] = envEntry.Value?.GetValue<string>() ?? string.Empty;
                }
            }

            servers[entry.Key] = new McpServerDefinitionRecord(
                serverObject["command"]?.GetValue<string>() ?? string.Empty,
                args,
                env);
        }

        return servers;
    }

    private static void WriteJsonManifest(string path, IReadOnlyDictionary<string, McpServerDefinitionRecord> servers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = new JsonObject
        {
            ["mcpServers"] = CreateJsonServerObject(servers)
        };

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static JsonObject CreateJsonServerObject(IReadOnlyDictionary<string, McpServerDefinitionRecord> servers)
    {
        var serverObject = new JsonObject();
        foreach (var entry in servers.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var env = new JsonObject();
            foreach (var envEntry in entry.Value.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                env[envEntry.Key] = envEntry.Value;
            }

            serverObject[entry.Key] = new JsonObject
            {
                ["command"] = entry.Value.Command,
                ["args"] = new JsonArray(entry.Value.Arguments.Select(argument => (JsonNode?)argument).ToArray()),
                ["env"] = env
            };
        }

        return serverObject;
    }

    private static void WriteTomlManifest(string path, IReadOnlyDictionary<string, McpServerDefinitionRecord> servers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = new TomlTable();
        var serverTable = new TomlTable();

        foreach (var entry in servers.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var table = new TomlTable
            {
                ["command"] = entry.Value.Command
            };

            if (entry.Value.Arguments.Count > 0)
            {
                var args = new TomlArray();
                foreach (var argument in entry.Value.Arguments)
                {
                    args.Add(argument);
                }

                table["args"] = args;
            }

            if (entry.Value.EnvironmentVariables.Count > 0)
            {
                var envTable = new TomlTable();
                foreach (var envEntry in entry.Value.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    envTable[envEntry.Key] = envEntry.Value;
                }

                table["env"] = envTable;
            }

            serverTable[GetCodexServerKey(entry.Key)] = table;
        }

        root["mcp_servers"] = serverTable;
        File.WriteAllText(path, Toml.FromModel(root), new UTF8Encoding(false));
    }

    private static string GetCodexServerKey(string serverName)
    {
        return string.Equals(serverName, "coplay-mcp", StringComparison.OrdinalIgnoreCase)
            ? "coplay_mcp"
            : serverName;
    }

    private static void CopyDirectoryContents(string sourceRoot, string destinationRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, filePath);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    private sealed record LayerDescriptor(
        string SkillsRoot,
        string CommandsRoot,
        string AgentsRoot);
}

internal sealed record EffectiveWorkspaceProfile(
    string ProfileId,
    string RootPath,
    string SkillsRoot,
    string CommandsRoot,
    string AgentsRoot,
    string SettingsPath,
    string ClaudeMcpPath,
    string CodexMcpPath,
    string AntigravityMcpPath,
    IReadOnlyDictionary<string, McpServerDefinitionRecord> ManagedServers)
{
    public string Profile => ProfileId;
}

internal static class WorkspaceProfiles
{
    public const string Global = "global";
    public const string Frontend = "frontend";
    public const string Backend = "backend";

    public static readonly string[] All =
    [
        Global,
        Frontend,
        Backend
    ];

    public static string Normalize(string? profileId)
    {
        return string.IsNullOrWhiteSpace(profileId)
            ? Global
            : profileId.Trim().ToLowerInvariant();
    }

    public static bool IsGlobal(string? profileId)
        => string.Equals(Normalize(profileId), Global, StringComparison.OrdinalIgnoreCase);

    public static string ToDisplayName(string? profileId)
    {
        return Normalize(profileId) switch
        {
            Global => "全局",
            Frontend => "前端",
            Backend => "后端",
            _ => "未知"
        };
    }

}
