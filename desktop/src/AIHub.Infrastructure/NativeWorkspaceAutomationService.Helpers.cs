using System.Text;
using System.Text.Json.Nodes;
using AIHub.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace AIHub.Infrastructure;

public sealed partial class NativeWorkspaceAutomationService
{
    private static void CopyFileWithBackup(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        WriteTextIfChanged(destinationPath, File.ReadAllText(sourcePath, Encoding.UTF8));
    }

    private static void CopyDirectoryWithBackup(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            BackupIfExists(destinationPath);
        }

        Directory.CreateDirectory(destinationPath);
        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var currentDestination = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(currentDestination)!);
            File.Copy(filePath, currentDestination, overwrite: true);
        }
    }

    private static string SafeReadTextPreview(string path)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
        return content.Length <= 1000
            ? content
            : content[..1000] + Environment.NewLine + "...";
    }

    private static string NormalizeText(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseJsonServers(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var root = JsonNode.Parse(File.ReadAllText(filePath, Encoding.UTF8)) as JsonObject ?? new JsonObject();
        var serverObject = root["mcpServers"] as JsonObject;
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        if (serverObject is null)
        {
            return servers;
        }

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

            servers[entry.Key] = new McpServerDefinitionRecord(current["command"]?.GetValue<string>() ?? string.Empty, args, env);
        }

        return servers;
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseTomlServers(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var root = Toml.ToModel(File.ReadAllText(filePath, Encoding.UTF8)) as TomlTable ?? new TomlTable();
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetValue("mcp_servers", out var rawServers) || rawServers is not TomlTable serverTable)
        {
            return servers;
        }

        foreach (var entry in serverTable)
        {
            if (entry.Value is not TomlTable current)
            {
                continue;
            }

            var command = current.TryGetValue("command", out var commandValue) ? Convert.ToString(commandValue) ?? string.Empty : string.Empty;
            var args = current.TryGetValue("args", out var argsValue) ? ConvertTomlArray(argsValue) : Array.Empty<string>();
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (current.TryGetValue("env", out var envValue) && envValue is TomlTable envTable)
            {
                foreach (var envEntry in envTable)
                {
                    env[envEntry.Key] = Convert.ToString(envEntry.Value) ?? string.Empty;
                }
            }

            servers[entry.Key] = new McpServerDefinitionRecord(command, args, env);
        }

        return servers;
    }

    private static string[] ConvertTomlArray(object? rawValue)
    {
        if (rawValue is TomlArray tomlArray)
        {
            return tomlArray.Select(item => Convert.ToString(item)).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
        }

        if (rawValue is IEnumerable<object> enumerable)
        {
            return enumerable.Select(item => Convert.ToString(item)).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
        }

        return Array.Empty<string>();
    }

    private static Dictionary<string, McpServerDefinitionRecord> ReadManifestServers(string path)
    {
        return ParseJsonServers(path);
    }

    private static JsonObject CreateJsonServerDefinition(McpServerDefinitionRecord definition)
    {
        var env = new JsonObject();
        foreach (var entry in definition.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            env[entry.Key] = entry.Value;
        }

        return new JsonObject
        {
            ["command"] = definition.Command,
            ["args"] = new JsonArray(definition.Arguments.Select(argument => (JsonNode?)argument).ToArray()),
            ["env"] = env
        };
    }
}
