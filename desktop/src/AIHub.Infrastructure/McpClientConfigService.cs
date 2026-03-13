using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Abstractions;
using AIHub.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace AIHub.Infrastructure;

public sealed class McpClientConfigService : IMcpClientConfigService
{
    private readonly string _userHome;

    public McpClientConfigService()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public McpClientConfigService(string userHome)
    {
        _userHome = userHome;
    }

    public Task<McpValidationSnapshot> InspectAsync(
        string hubRoot,
        WorkspaceScope scope,
        ProfileKind profile,
        string? projectPath,
        IReadOnlyDictionary<string, McpServerDefinitionRecord> managedServers,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<McpValidationIssueRecord>();
        var externalServers = new Dictionary<string, List<McpExternalServerVariantRecord>>(StringComparer.OrdinalIgnoreCase);
        var statuses = new List<McpClientConfigStatusRecord>();

        foreach (var target in GetTargets(scope, projectPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!target.IsSupported)
            {
                statuses.Add(new McpClientConfigStatusRecord(
                    target.Client,
                    target.FilePath,
                    false,
                    false,
                    true,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    "当前作用域不支持该客户端。"));
                continue;
            }

            var parsed = target.Format == ClientConfigFormat.Toml
                ? ReadTomlConfig(target.FilePath)
                : ReadJsonConfig(target.FilePath);
            var managedNames = managedServers.Keys
                .Where(name => parsed.Servers.TryGetValue(name, out var current) && current == managedServers[name])
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var externalNames = parsed.Servers.Keys
                .Except(managedServers.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var inSync = managedServers.All(entry =>
                parsed.Servers.TryGetValue(entry.Key, out var current) &&
                current == entry.Value);

            statuses.Add(new McpClientConfigStatusRecord(
                target.Client,
                target.FilePath,
                true,
                parsed.Exists,
                inSync,
                managedNames,
                externalNames,
                BuildStatusSummary(parsed.Exists, inSync, managedNames.Length, externalNames.Length, parsed.ParseError)));

            if (!string.IsNullOrWhiteSpace(parsed.ParseError))
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Error,
                    $"{target.Client} 配置文件无法解析。",
                    parsed.ParseError,
                    target.FilePath));
                continue;
            }

            if (!parsed.Exists)
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Warning,
                    $"{target.Client} 当前作用域配置文件不存在。",
                    target.FilePath,
                    target.FilePath));
            }
            else if (!inSync)
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Warning,
                    $"{target.Client} 当前作用域配置与 AI-Hub 生成结果不一致。",
                    target.FilePath,
                    target.FilePath));
            }

            foreach (var externalName in externalNames)
            {
                externalServers.TryAdd(externalName, []);
                externalServers[externalName].Add(new McpExternalServerVariantRecord(
                    target.Client,
                    target.FilePath,
                    parsed.Servers[externalName]));
            }
        }

        foreach (var server in managedServers.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var issue in ValidateServerDefinition(server.Key, server.Value))
            {
                issues.Add(issue);
            }
        }

        var preview = externalServers
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new McpExternalServerPreviewRecord(entry.Key, entry.Value))
            .ToArray();

        return Task.FromResult(new McpValidationSnapshot(
            scope,
            profile,
            projectPath,
            statuses,
            issues,
            preview));
    }

    public async Task<OperationResult> SyncAsync(
        string hubRoot,
        WorkspaceScope scope,
        ProfileKind profile,
        string? projectPath,
        IReadOnlyDictionary<string, McpServerDefinitionRecord> managedServers,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var detailLines = new List<string>();
        foreach (var target in GetTargets(scope, projectPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!target.IsSupported)
            {
                detailLines.Add($"{target.Client}: 当前作用域不支持，已跳过。");
                continue;
            }

            if (scope == WorkspaceScope.Project && string.IsNullOrWhiteSpace(projectPath))
            {
                return OperationResult.Fail("项目级同步需要有效的项目路径。");
            }

            var parsed = target.Format == ClientConfigFormat.Toml
                ? ReadTomlConfig(target.FilePath)
                : ReadJsonConfig(target.FilePath);
            var mergedServers = parsed.Servers
                .Where(entry => !managedServers.ContainsKey(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var managed in managedServers)
            {
                mergedServers[managed.Key] = managed.Value;
            }

            var newContent = target.Format == ClientConfigFormat.Toml
                ? BuildTomlContent(parsed.RootTable, mergedServers)
                : BuildJsonContent(parsed.RootObject, mergedServers);

            if (string.Equals((parsed.RawText ?? string.Empty).Trim(), newContent.Trim(), StringComparison.Ordinal))
            {
                detailLines.Add($"{target.Client}: 已是最新，无需写回。");
                continue;
            }

            await BackupAndWriteAsync(target.FilePath, newContent, cancellationToken);
            detailLines.Add($"{target.Client}: 已同步 {mergedServers.Count} 个 MCP 条目。");
        }

        return OperationResult.Ok("客户端 MCP 配置已同步。", string.Join(Environment.NewLine, detailLines));
    }

    private IEnumerable<McpValidationIssueRecord> ValidateServerDefinition(string serverName, McpServerDefinitionRecord definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Command))
        {
            yield return new McpValidationIssueRecord(
                McpValidationSeverity.Error,
                $"服务器 {serverName} 缺少启动命令。",
                null,
                null,
                serverName);
            yield break;
        }

        if (!TryResolveCommandPath(definition.Command, out var resolvedCommand))
        {
            yield return new McpValidationIssueRecord(
                McpValidationSeverity.Error,
                $"服务器 {serverName} 的命令无法解析。",
                definition.Command,
                null,
                serverName);
        }
        else
        {
            yield return new McpValidationIssueRecord(
                McpValidationSeverity.Info,
                $"服务器 {serverName} 命令可解析。",
                resolvedCommand,
                null,
                serverName);
        }

        foreach (var environmentVariable in definition.EnvironmentVariables.Where(item => string.IsNullOrWhiteSpace(item.Value)))
        {
            yield return new McpValidationIssueRecord(
                McpValidationSeverity.Warning,
                $"服务器 {serverName} 存在空环境变量值。",
                environmentVariable.Key,
                null,
                serverName);
        }
    }

    private ParsedClientConfig ReadJsonConfig(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ParsedClientConfig(false, string.Empty, new JsonObject(), null, new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase));
        }

        var rawText = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new ParsedClientConfig(true, rawText, new JsonObject(), null, new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            var root = JsonNode.Parse(rawText) as JsonObject ?? new JsonObject();
            var servers = ParseJsonServers(root["mcpServers"] as JsonObject);
            return new ParsedClientConfig(true, rawText, root, null, servers);
        }
        catch (Exception exception)
        {
            return new ParsedClientConfig(true, rawText, new JsonObject(), exception.Message, new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private ParsedClientConfig ReadTomlConfig(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ParsedClientConfig(false, string.Empty, null, null, new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase), new TomlTable());
        }

        var rawText = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new ParsedClientConfig(true, rawText, null, null, new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase), new TomlTable());
        }

        try
        {
            var root = Toml.ToModel(rawText) as TomlTable ?? new TomlTable();
            var servers = ParseTomlServers(root);
            return new ParsedClientConfig(true, rawText, null, null, servers, root);
        }
        catch (Exception exception)
        {
            return new ParsedClientConfig(true, rawText, null, exception.Message, new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase), new TomlTable());
        }
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

            var command = serverObject["command"]?.GetValue<string>() ?? string.Empty;
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

            servers[entry.Key] = new McpServerDefinitionRecord(command, args, env);
        }

        return servers;
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseTomlServers(TomlTable root)
    {
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetValue("mcp_servers", out var rawServers) || rawServers is not TomlTable serverTable)
        {
            return servers;
        }

        foreach (var entry in serverTable)
        {
            if (entry.Value is not TomlTable currentServer)
            {
                continue;
            }

            var command = currentServer.TryGetValue("command", out var commandValue)
                ? Convert.ToString(commandValue) ?? string.Empty
                : string.Empty;
            var arguments = currentServer.TryGetValue("args", out var argsValue)
                ? ConvertTomlArray(argsValue)
                : Array.Empty<string>();
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (currentServer.TryGetValue("env", out var envValue) && envValue is TomlTable envTable)
            {
                foreach (var envEntry in envTable)
                {
                    environment[envEntry.Key] = Convert.ToString(envEntry.Value) ?? string.Empty;
                }
            }

            servers[entry.Key] = new McpServerDefinitionRecord(command, arguments, environment);
        }

        return servers;
    }

    private static string[] ConvertTomlArray(object? rawValue)
    {
        if (rawValue is TomlArray tomlArray)
        {
            return tomlArray
                .Select(item => Convert.ToString(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }

        if (rawValue is IEnumerable<object> enumerable)
        {
            return enumerable
                .Select(item => Convert.ToString(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static string BuildJsonContent(JsonObject? existingRoot, IReadOnlyDictionary<string, McpServerDefinitionRecord> mergedServers)
    {
        var root = existingRoot?.DeepClone() as JsonObject ?? new JsonObject();
        root["mcpServers"] = CreateJsonServerObject(mergedServers);
        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static JsonObject CreateJsonServerObject(IReadOnlyDictionary<string, McpServerDefinitionRecord> servers)
    {
        var objectNode = new JsonObject();
        foreach (var entry in servers.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var envObject = new JsonObject();
            foreach (var envEntry in entry.Value.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                envObject[envEntry.Key] = envEntry.Value;
            }

            objectNode[entry.Key] = new JsonObject
            {
                ["command"] = entry.Value.Command,
                ["args"] = new JsonArray(entry.Value.Arguments.Select(argument => (JsonNode?)argument).ToArray()),
                ["env"] = envObject
            };
        }

        return objectNode;
    }

    private static string BuildTomlContent(TomlTable? existingRoot, IReadOnlyDictionary<string, McpServerDefinitionRecord> mergedServers)
    {
        var root = CloneTomlTable(existingRoot) ?? new TomlTable();
        var serverTable = new TomlTable();
        foreach (var entry in mergedServers.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var itemTable = new TomlTable
            {
                ["command"] = entry.Value.Command
            };

            if (entry.Value.Arguments.Count > 0)
            {
                var argsArray = new TomlArray();
                foreach (var argument in entry.Value.Arguments)
                {
                    argsArray.Add(argument);
                }

                itemTable["args"] = argsArray;
            }

            if (entry.Value.EnvironmentVariables.Count > 0)
            {
                var envTable = new TomlTable();
                foreach (var envEntry in entry.Value.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    envTable[envEntry.Key] = envEntry.Value;
                }

                itemTable["env"] = envTable;
            }

            serverTable[entry.Key] = itemTable;
        }

        root["mcp_servers"] = serverTable;
        return Toml.FromModel(root);
    }

    private static TomlTable? CloneTomlTable(TomlTable? source)
    {
        if (source is null)
        {
            return null;
        }

        var clone = new TomlTable();
        foreach (var entry in source)
        {
            clone[entry.Key] = CloneTomlValue(entry.Value)!;
        }

        return clone;
    }

    private static object? CloneTomlValue(object? value)
    {
        return value switch
        {
            TomlTable table => CloneTomlTable(table),
            TomlArray array => CloneTomlArray(array),
            _ => value
        };
    }

    private static TomlArray CloneTomlArray(TomlArray source)
    {
        var clone = new TomlArray();
        foreach (var item in source)
        {
            clone.Add(CloneTomlValue(item));
        }

        return clone;
    }

    private static async Task BackupAndWriteAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(filePath))
        {
            var backupPath = filePath + ".bak." + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(filePath, backupPath, overwrite: true);
        }

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }

    private static string BuildStatusSummary(bool exists, bool inSync, int managedCount, int externalCount, string? parseError)
    {
        if (!string.IsNullOrWhiteSpace(parseError))
        {
            return "解析失败";
        }

        if (!exists)
        {
            return "文件不存在";
        }

        if (!inSync)
        {
            return $"存在漂移 / AI-Hub 管理 {managedCount} / 外部 {externalCount}";
        }

        return $"已同步 / AI-Hub 管理 {managedCount} / 外部 {externalCount}";
    }

    private static bool TryResolveCommandPath(string command, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var expandedCommand = Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
        if (Path.IsPathRooted(expandedCommand) || expandedCommand.Contains(Path.DirectorySeparatorChar) || expandedCommand.Contains(Path.AltDirectorySeparatorChar))
        {
            return TryResolvePathCandidate(expandedCommand, out resolvedPath);
        }

        var pathEnvironment = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var pathSegment in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(pathSegment.Trim(), expandedCommand);
            if (TryResolvePathCandidate(candidate, out resolvedPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolvePathCandidate(string candidate, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (File.Exists(candidate))
        {
            resolvedPath = Path.GetFullPath(candidate);
            return true;
        }

        if (Path.HasExtension(candidate))
        {
            return false;
        }

        var pathExtensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe;.cmd;.bat;.ps1")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var extension in pathExtensions)
        {
            var currentCandidate = candidate + extension;
            if (!File.Exists(currentCandidate))
            {
                continue;
            }

            resolvedPath = Path.GetFullPath(currentCandidate);
            return true;
        }

        return false;
    }

    private IEnumerable<ClientConfigTarget> GetTargets(WorkspaceScope scope, string? projectPath)
    {
        if (scope == WorkspaceScope.Project)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                yield return new ClientConfigTarget(McpClientKind.Claude, "未选择项目", ClientConfigFormat.Json, IsSupported: false);
                yield return new ClientConfigTarget(McpClientKind.Codex, "未选择项目", ClientConfigFormat.Toml, IsSupported: false);
                yield return new ClientConfigTarget(McpClientKind.Antigravity, "项目级作用域未定义", ClientConfigFormat.Json, IsSupported: false);
                yield break;
            }

            yield return new ClientConfigTarget(McpClientKind.Claude, Path.Combine(projectPath, ".mcp.json"), ClientConfigFormat.Json);
            yield return new ClientConfigTarget(McpClientKind.Codex, Path.Combine(projectPath, ".codex", "config.toml"), ClientConfigFormat.Toml);
            yield return new ClientConfigTarget(McpClientKind.Antigravity, "项目级作用域未定义", ClientConfigFormat.Json, IsSupported: false);
            yield break;
        }

        yield return new ClientConfigTarget(McpClientKind.Claude, Path.Combine(_userHome, ".claude.json"), ClientConfigFormat.Json);
        yield return new ClientConfigTarget(McpClientKind.Codex, Path.Combine(_userHome, ".codex", "config.toml"), ClientConfigFormat.Toml);
        yield return new ClientConfigTarget(McpClientKind.Antigravity, Path.Combine(_userHome, ".gemini", "antigravity", "mcp_config.json"), ClientConfigFormat.Json);
    }

    private sealed record ClientConfigTarget(
        McpClientKind Client,
        string FilePath,
        ClientConfigFormat Format,
        bool IsSupported = true);

    private sealed record ParsedClientConfig(
        bool Exists,
        string RawText,
        JsonObject? RootObject,
        string? ParseError,
        Dictionary<string, McpServerDefinitionRecord> Servers,
        TomlTable? RootTable = null);

    private enum ClientConfigFormat
    {
        Json = 0,
        Toml = 1
    }
}
