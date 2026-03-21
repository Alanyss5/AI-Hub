using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Abstractions;
using AIHub.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace AIHub.Infrastructure;

internal static class LayeredWorkspaceMaterializer
{
    private static readonly ISourcePathLayout SourcePathLayout = SourceLayoutMigrationService.DefaultLayout;
    private static readonly string[] DefaultProfiles =
    [
        WorkspaceProfiles.GlobalId,
        WorkspaceProfiles.FrontendId,
        WorkspaceProfiles.BackendId
    ];

    public static IReadOnlyList<string> GetKnownProfiles(string? hubRoot)
    {
        if (string.IsNullOrWhiteSpace(hubRoot))
        {
            return DefaultProfiles;
        }

        try
        {
            return new JsonWorkspaceProfileCatalogStore(hubRoot)
                .LoadAsync()
                .GetAwaiter()
                .GetResult()
                .Select(profile => WorkspaceProfiles.NormalizeId(profile.Id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return DefaultProfiles;
        }
    }

    public static string GetPersonalRoot(string userHome)
    {
        return Path.Combine(Path.GetFullPath(userHome), "AI-Personal");
    }

    public static string GetEffectiveProfileRoot(string hubRoot, string profile)
    {
        return Path.Combine(Path.GetFullPath(hubRoot), ".runtime", "effective", WorkspaceProfiles.NormalizeId(profile));
    }

    public static void EnsurePrivateLayerStructure(string personalRoot)
    {
        EnsurePrivateLayerStructure(null, personalRoot);
    }

    public static void EnsurePrivateLayerStructure(string? hubRoot, string personalRoot)
    {
        var normalizedPersonalRoot = Path.GetFullPath(personalRoot);
        var personalSourceRoot = SourcePathLayout.GetPersonalSourceRoot(normalizedPersonalRoot);
        foreach (var profile in GetKnownProfiles(hubRoot).Select(WorkspaceProfiles.NormalizeId))
        {
            Directory.CreateDirectory(SourcePathLayout.GetProfileSkillsRoot(personalSourceRoot, profile));
            Directory.CreateDirectory(SourcePathLayout.GetProfileCommandsRoot(personalSourceRoot, profile));
            Directory.CreateDirectory(SourcePathLayout.GetProfileAgentsRoot(personalSourceRoot, profile));
            Directory.CreateDirectory(Path.GetDirectoryName(SourcePathLayout.GetProfileSettingsPath(personalSourceRoot, profile))!);
            Directory.CreateDirectory(Path.GetDirectoryName(SourcePathLayout.GetProfileManifestPath(personalSourceRoot, profile))!);
        }

        Directory.CreateDirectory(SourcePathLayout.GetSkillsLibraryRoot(personalSourceRoot));
        Directory.CreateDirectory(SourcePathLayout.GetMcpDraftsRoot(personalSourceRoot));
        Directory.CreateDirectory(SourcePathLayout.GetRegistryRoot(personalSourceRoot));
    }

    public static EffectiveWorkspaceProfile MaterializeProfile(string hubRoot, string personalRoot, string profile)
    {
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var normalizedPersonalRoot = Path.GetFullPath(personalRoot);
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        SourceLayoutMigrationService.EnsureMigrated(normalizedHubRoot, normalizedPersonalRoot, GetKnownProfiles(normalizedHubRoot), SourcePathLayout);
        EnsurePrivateLayerStructure(normalizedHubRoot, normalizedPersonalRoot);

        var effectiveRoot = GetEffectiveProfileRoot(normalizedHubRoot, normalizedProfile);
        RecreateDirectory(effectiveRoot);

        var skillsRoot = Path.Combine(effectiveRoot, "skills");
        var commandsRoot = Path.Combine(effectiveRoot, "claude", "commands");
        var agentsRoot = Path.Combine(effectiveRoot, "claude", "agents");
        var mcpRoot = Path.Combine(effectiveRoot, "mcp");
        var agentsBootstrapRoot = Path.Combine(effectiveRoot, ".agents");
        Directory.CreateDirectory(skillsRoot);
        Directory.CreateDirectory(commandsRoot);
        Directory.CreateDirectory(agentsRoot);
        Directory.CreateDirectory(mcpRoot);
        Directory.CreateDirectory(agentsBootstrapRoot);

        foreach (var layer in BuildLayers(normalizedHubRoot, normalizedPersonalRoot, normalizedProfile))
        {
            CopyDirectoryContents(layer.SkillsRoot, skillsRoot);
            CopyDirectoryContents(layer.CommandsRoot, commandsRoot);
            CopyDirectoryContents(layer.AgentsRoot, agentsRoot);
        }

        var agentsBootstrapPath = Path.Combine(agentsBootstrapRoot, "AGENTS.md");
        File.WriteAllText(agentsBootstrapPath, BuildAgentsBootstrapContent(normalizedProfile), new UTF8Encoding(false));

        var settingsContent = BuildEffectiveSettingsContent(normalizedHubRoot, normalizedPersonalRoot, normalizedProfile);
        var settingsPath = Path.Combine(effectiveRoot, "claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, settingsContent, new UTF8Encoding(false));

        var servers = BuildEffectiveServerMap(normalizedHubRoot, normalizedPersonalRoot, normalizedProfile);
        var claudeMcpPath = Path.Combine(mcpRoot, "claude.mcp.json");
        var codexMcpPath = Path.Combine(mcpRoot, "codex.config.toml");
        var antigravityMcpPath = Path.Combine(mcpRoot, "antigravity.mcp.json");
        WriteJsonManifest(claudeMcpPath, servers);
        WriteTomlManifest(codexMcpPath, servers);
        WriteJsonManifest(antigravityMcpPath, servers);

        return new EffectiveWorkspaceProfile(
            normalizedProfile,
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
        => GenerateLegacyMcpOutputs(hubRoot, personalRoot, GetKnownProfiles(hubRoot));

    public static OperationResult GenerateLegacyMcpOutputs(string hubRoot, string personalRoot, IEnumerable<string> profiles)
    {
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var normalizedPersonalRoot = Path.GetFullPath(personalRoot);
        var selectedProfiles = profiles
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
        string profile)
    {
        SourceLayoutMigrationService.EnsureMigrated(hubRoot, personalRoot, GetKnownProfiles(hubRoot), SourcePathLayout);
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifestPath in EnumerateManifestLayerPaths(Path.GetFullPath(hubRoot), Path.GetFullPath(personalRoot), profile))
        {
            foreach (var entry in ParseManifest(manifestPath))
            {
                servers[entry.Key] = entry.Value;
            }
        }

        return servers;
    }

    private static IEnumerable<LayerDescriptor> BuildLayers(string hubRoot, string personalRoot, string profile)
    {
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        yield return CreateLayer(SourcePathLayout.GetCompanySourceRoot(hubRoot), WorkspaceProfiles.GlobalId);
        yield return CreateLayer(SourcePathLayout.GetPersonalSourceRoot(personalRoot), WorkspaceProfiles.GlobalId);

        if (!WorkspaceProfiles.IsGlobal(normalizedProfile))
        {
            yield return CreateLayer(SourcePathLayout.GetCompanySourceRoot(hubRoot), normalizedProfile);
            yield return CreateLayer(SourcePathLayout.GetPersonalSourceRoot(personalRoot), normalizedProfile);
        }
    }

    private static IEnumerable<string> EnumerateSettingsLayerPaths(string hubRoot, string personalRoot, string profile)
    {
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        var companySourceRoot = SourcePathLayout.GetCompanySourceRoot(hubRoot);
        var personalSourceRoot = SourcePathLayout.GetPersonalSourceRoot(personalRoot);
        yield return SourcePathLayout.GetProfileSettingsPath(companySourceRoot, WorkspaceProfiles.GlobalId);
        yield return SourcePathLayout.GetProfileSettingsPath(personalSourceRoot, WorkspaceProfiles.GlobalId);

        if (!WorkspaceProfiles.IsGlobal(normalizedProfile))
        {
            yield return SourcePathLayout.GetProfileSettingsPath(companySourceRoot, normalizedProfile);
            yield return SourcePathLayout.GetProfileSettingsPath(personalSourceRoot, normalizedProfile);
        }
    }

    private static IEnumerable<string> EnumerateManifestLayerPaths(string hubRoot, string personalRoot, string profile)
    {
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        var companySourceRoot = SourcePathLayout.GetCompanySourceRoot(hubRoot);
        var personalSourceRoot = SourcePathLayout.GetPersonalSourceRoot(personalRoot);
        yield return SourcePathLayout.GetProfileManifestPath(companySourceRoot, WorkspaceProfiles.GlobalId);
        yield return SourcePathLayout.GetProfileManifestPath(personalSourceRoot, WorkspaceProfiles.GlobalId);

        if (!WorkspaceProfiles.IsGlobal(normalizedProfile))
        {
            yield return SourcePathLayout.GetProfileManifestPath(companySourceRoot, normalizedProfile);
            yield return SourcePathLayout.GetProfileManifestPath(personalSourceRoot, normalizedProfile);
        }
    }

    private static LayerDescriptor CreateLayer(string root, string profile)
    {
        var profileValue = WorkspaceProfiles.NormalizeId(profile);
        return new LayerDescriptor(
            SourcePathLayout.GetProfileSkillsRoot(root, profileValue),
            SourcePathLayout.GetProfileCommandsRoot(root, profileValue),
            SourcePathLayout.GetProfileAgentsRoot(root, profileValue));
    }

    private static string BuildEffectiveSettingsContent(string hubRoot, string personalRoot, string profile)
    {
        JsonObject? merged = null;
        foreach (var settingsPath in EnumerateSettingsLayerPaths(hubRoot, personalRoot, profile))
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

    private static string BuildAgentsBootstrapContent(string profile)
    {
        var displayName = WorkspaceProfiles.ToDisplayName(profile);
        return string.Join(Environment.NewLine, new[]
        {
            "# AI-Hub AGENTS Bootstrap",
            string.Empty,
            "Profile: " + displayName,
            "ProfileId: " + WorkspaceProfiles.NormalizeId(profile),
            string.Empty,
            "Use the nearby effective workspace paths directly:",
            "- .agents\\skills -> profile-scoped skills for this workspace",
            "- .agents\\agents -> profile-scoped agent definitions near this bootstrap",
            string.Empty,
            "Keep this file lightweight.",
            "No workflow metadata parsing, DAG, or planner lives here."
        });
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

            servers[McpServerNameAliases.ToCanonical(entry.Key)] = new McpServerDefinitionRecord(
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

            serverTable[McpServerNameAliases.ToCodexKey(entry.Key)] = table;
        }

        root["mcp_servers"] = serverTable;
        File.WriteAllText(path, Toml.FromModel(root), new UTF8Encoding(false));
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
    string Profile,
    string RootPath,
    string SkillsRoot,
    string CommandsRoot,
    string AgentsRoot,
    string SettingsPath,
    string ClaudeMcpPath,
    string CodexMcpPath,
    string AntigravityMcpPath,
    IReadOnlyDictionary<string, McpServerDefinitionRecord> ManagedServers);
