using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Abstractions;
using AIHub.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace AIHub.Infrastructure;

public sealed class NativeMcpAutomationService : IMcpAutomationService
{
    public Task<OperationResult> GenerateConfigsAsync(string hubRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manifestDirectory = Path.Combine(hubRoot, "mcp", "manifest");
        if (!Directory.Exists(manifestDirectory))
        {
            return Task.FromResult(OperationResult.Fail("MCP manifest 目录不存在。", manifestDirectory));
        }

        var manifestFiles = Directory.EnumerateFiles(manifestDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (manifestFiles.Length == 0)
        {
            return Task.FromResult(OperationResult.Fail("未找到 MCP manifest 文件。", manifestDirectory));
        }

        var claudeOutput = Path.Combine(hubRoot, "mcp", "generated", "claude");
        var codexOutput = Path.Combine(hubRoot, "mcp", "generated", "codex");
        var antigravityOutput = Path.Combine(hubRoot, "mcp", "generated", "antigravity");
        Directory.CreateDirectory(claudeOutput);
        Directory.CreateDirectory(codexOutput);
        Directory.CreateDirectory(antigravityOutput);

        var manifests = manifestFiles.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path),
            path => ParseManifest(path),
            StringComparer.OrdinalIgnoreCase);

        manifests.TryGetValue("global", out var globalServers);
        globalServers ??= new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in manifests.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var effectiveServers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
            if (!string.Equals(manifest.Key, "global", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var server in globalServers)
                {
                    effectiveServers[server.Key] = server.Value;
                }
            }

            foreach (var server in manifest.Value)
            {
                effectiveServers[server.Key] = server.Value;
            }

            WriteJsonManifest(Path.Combine(claudeOutput, manifest.Key + ".mcp.json"), effectiveServers);
            WriteJsonManifest(Path.Combine(antigravityOutput, manifest.Key + ".mcp.json"), effectiveServers);
            WriteTomlManifest(Path.Combine(codexOutput, manifest.Key + ".config.toml"), effectiveServers);
        }

        return Task.FromResult(OperationResult.Ok(
            "MCP generated 配置已完成刷新。",
            string.Join(Environment.NewLine, new[]
            {
                "Claude 输出：" + claudeOutput,
                "Codex 输出：" + codexOutput,
                "Antigravity 输出：" + antigravityOutput,
                "Manifest 数量：" + manifests.Count
            })));
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseManifest(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException("Manifest 不是有效的 JSON 对象：" + path);
        var serverObject = root["mcpServers"] as JsonObject
            ?? throw new InvalidOperationException("Manifest 缺少 mcpServers：" + path);

        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in serverObject)
        {
            if (entry.Value is not JsonObject current)
            {
                continue;
            }

            var args = current["args"] is JsonArray argsArray
                ? argsArray.Select(item => item?.GetValue<string>()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray()
                : Array.Empty<string>();
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (current["env"] is JsonObject envObject)
            {
                foreach (var envEntry in envObject)
                {
                    env[envEntry.Key] = envEntry.Value?.GetValue<string>() ?? string.Empty;
                }
            }

            servers[entry.Key] = new McpServerDefinitionRecord(
                current["command"]?.GetValue<string>() ?? string.Empty,
                args,
                env);
        }

        return servers;
    }

    private static void WriteJsonManifest(string path, IReadOnlyDictionary<string, McpServerDefinitionRecord> servers)
    {
        var root = new JsonObject
        {
            ["mcpServers"] = CreateJsonServerObject(servers)
        };

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
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

            serverTable[entry.Key] = table;
        }

        root["mcp_servers"] = serverTable;
        File.WriteAllText(path, Toml.FromModel(root), new System.Text.UTF8Encoding(false));
    }
}
