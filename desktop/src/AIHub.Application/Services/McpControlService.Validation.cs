using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Abstractions;
using AIHub.Contracts;
using Tomlyn;
using Tomlyn.Model;

namespace AIHub.Application.Services;

public sealed partial class McpControlService
{
    public async Task<McpValidationSnapshot> ValidateCurrentScopeAsync(
        WorkspaceScope scope,
        string profile,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return new McpValidationSnapshot(
                scope,
                ResolveScopeProfile(scope, profile),
                projectPath,
                Array.Empty<McpClientConfigStatusRecord>(),
                [new McpValidationIssueRecord(McpValidationSeverity.Error, "AI-Hub 根目录无效。", string.Join(Environment.NewLine, resolution.Errors))],
                Array.Empty<McpExternalServerPreviewRecord>());
        }

        var targetProfile = ResolveScopeProfile(scope, profile);
        var profileRecords = await _mcpProfileStoreFactory(resolution.RootPath).GetAllAsync(cancellationToken);
        var managedServers = BuildEffectiveServerMap(profileRecords, targetProfile);
        var inspection = await _mcpClientConfigService.InspectAsync(
            resolution.RootPath,
            scope,
            targetProfile,
            projectPath,
            managedServers,
            cancellationToken);

        var issues = inspection.Issues.ToList();
        issues.AddRange(ValidateGeneratedConfigs(profileRecords, targetProfile, managedServers));
        issues.AddRange(await ValidateManagedProcessDefinitionsAsync(resolution.RootPath, cancellationToken));

        return inspection with
        {
            Profile = targetProfile,
            Issues = issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Summary, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public async Task<OperationResult> SyncCurrentScopeClientsAsync(
        WorkspaceScope scope,
        string profile,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法同步客户端配置。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var generateResult = await _mcpAutomationService.GenerateConfigsAsync(resolution.RootPath, cancellationToken);
        if (!generateResult.Success)
        {
            return generateResult;
        }

        var targetProfile = ResolveScopeProfile(scope, profile);
        var profileRecords = await _mcpProfileStoreFactory(resolution.RootPath).GetAllAsync(cancellationToken);
        var managedServers = BuildEffectiveServerMap(profileRecords, targetProfile);
        var syncResult = await _mcpClientConfigService.SyncAsync(
            resolution.RootPath,
            scope,
            targetProfile,
            projectPath,
            managedServers,
            cancellationToken);

        if (!syncResult.Success)
        {
            return syncResult;
        }

        return OperationResult.Ok(
            "当前作用域客户端配置已同步。",
            string.Join(Environment.NewLine, new[]
            {
                $"作用域：{(scope == WorkspaceScope.Project ? "项目级" : "全局级")}",
                $"Profile：{WorkspaceProfiles.ToDisplayName(targetProfile)}",
                string.IsNullOrWhiteSpace(projectPath) ? null : "项目路径：" + projectPath,
                generateResult.Details,
                syncResult.Details
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    public async Task<OperationResult> ImportExternalServersAsync(
        WorkspaceScope scope,
        string profile,
        string? projectPath,
        IReadOnlyList<McpExternalServerImportDecision> decisions,
        bool syncClients,
        CancellationToken cancellationToken = default)
    {
        if (decisions.Count == 0)
        {
            return OperationResult.Fail("请先选择要导入的外部 MCP。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法导入外部 MCP。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var targetProfile = ResolveScopeProfile(scope, profile);
        var profileStore = _mcpProfileStoreFactory(resolution.RootPath);
        var profileRecords = await profileStore.GetAllAsync(cancellationToken);
        var managedServers = BuildEffectiveServerMap(profileRecords, targetProfile);
        var inspection = await _mcpClientConfigService.InspectAsync(
            resolution.RootPath,
            scope,
            targetProfile,
            projectPath,
            managedServers,
            cancellationToken);

        var manifestRecord = profileRecords.First(record => record.Profile == targetProfile);
        var manifestRoot = ParseJsonObject(manifestRecord.RawJson);
        var manifestServers = manifestRoot["mcpServers"] as JsonObject ?? new JsonObject();
        manifestRoot["mcpServers"] = manifestServers;

        foreach (var decision in decisions)
        {
            var preview = inspection.ExternalServers.FirstOrDefault(item =>
                string.Equals(item.Name, decision.Name, StringComparison.OrdinalIgnoreCase));
            if (preview is null)
            {
                return OperationResult.Fail("要导入的外部 MCP 不存在于当前体检结果中。", decision.Name);
            }

            var selectedVariant = preview.Variants.FirstOrDefault(item => item.Client == decision.SourceClient);
            if (selectedVariant is null)
            {
                return OperationResult.Fail("所选来源客户端未提供该 MCP 定义。", decision.Name);
            }

            manifestServers[decision.Name] = CreateJsonServerDefinition(selectedVariant.Definition);
        }

        var saveResult = await SaveManifestAsync(targetProfile, manifestRoot.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        }), cancellationToken);
        if (!saveResult.Success)
        {
            return saveResult;
        }

        var generateResult = await _mcpAutomationService.GenerateConfigsAsync(resolution.RootPath, cancellationToken);
        if (!generateResult.Success)
        {
            return generateResult;
        }

        if (syncClients)
        {
            var syncResult = await SyncCurrentScopeClientsAsync(scope, targetProfile, projectPath, cancellationToken);
            if (!syncResult.Success)
            {
                return syncResult;
            }

            return OperationResult.Ok(
                "外部 MCP 已导入并同步到客户端。",
                string.Join(Environment.NewLine, new[]
                {
                    saveResult.Details,
                    generateResult.Details,
                    syncResult.Details
                }.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        return OperationResult.Ok(
            "外部 MCP 已导入到 AI-Hub。",
            string.Join(Environment.NewLine, new[]
            {
                $"导入数量：{decisions.Count}",
                $"目标 Profile：{WorkspaceProfiles.ToDisplayName(targetProfile)}",
                generateResult.Details
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private async Task<IReadOnlyList<McpValidationIssueRecord>> ValidateManagedProcessDefinitionsAsync(
        string hubRoot,
        CancellationToken cancellationToken)
    {
        var runtimeStore = _mcpRuntimeStoreFactory(hubRoot);
        var runtimeRecords = await runtimeStore.GetAllAsync(cancellationToken);
        var issues = new List<McpValidationIssueRecord>();

        foreach (var record in runtimeRecords.Where(item => item.Mode == McpServerMode.ProcessManaged))
        {
            var validationError = ValidateManagedProcess(record);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Error,
                    $"托管 MCP {record.Name} 定义无效。",
                    validationError,
                    null,
                    record.Name));
                continue;
            }

            if (!TryResolveCommandPath(record.Command, out var resolvedPath))
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Error,
                    $"托管 MCP {record.Name} 的命令无法解析。",
                    record.Command,
                    null,
                    record.Name));
            }
            else
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Info,
                    $"托管 MCP {record.Name} 命令可解析。",
                    resolvedPath,
                    null,
                    record.Name));
            }

            if (HasHealthAlert(record))
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Warning,
                    $"托管 MCP {record.Name} 最近一次健康检查异常。",
                    record.LastHealthMessage,
                    null,
                    record.Name));
            }
        }

        return issues;
    }

    private static IReadOnlyList<McpValidationIssueRecord> ValidateGeneratedConfigs(
        IReadOnlyList<McpProfileRecord> profiles,
        string profile,
        IReadOnlyDictionary<string, McpServerDefinitionRecord> managedServers)
    {
        var issues = new List<McpValidationIssueRecord>();
        var targetProfile = profiles.FirstOrDefault(item => string.Equals(item.Profile, profile, StringComparison.OrdinalIgnoreCase));
        if (targetProfile is null)
        {
            issues.Add(new McpValidationIssueRecord(
                McpValidationSeverity.Error,
                "未找到当前 Profile 的 MCP manifest。"));
            return issues;
        }

        foreach (var generatedClient in targetProfile.GeneratedClients)
        {
            if (!File.Exists(generatedClient.FilePath))
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Warning,
                    $"{generatedClient.ClientName} 生成文件不存在。",
                    generatedClient.FilePath,
                    generatedClient.FilePath));
                continue;
            }

            try
            {
                var currentServers = ParseGeneratedClientServers(generatedClient);
                if (!DefinitionsEqual(currentServers, managedServers))
                {
                    issues.Add(new McpValidationIssueRecord(
                        McpValidationSeverity.Warning,
                        $"{generatedClient.ClientName} 生成文件不是最新结果。",
                        generatedClient.FilePath,
                        generatedClient.FilePath));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new McpValidationIssueRecord(
                    McpValidationSeverity.Error,
                    $"{generatedClient.ClientName} 生成文件无法解析。",
                    exception.Message,
                    generatedClient.FilePath));
            }
        }

        return issues;
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseGeneratedClientServers(McpGeneratedClientConfig config)
    {
        return string.Equals(config.ClientName, "Codex", StringComparison.OrdinalIgnoreCase)
            ? ParseTomlServerDefinitions(config.Content)
            : ParseJsonServerDefinitions(config.Content);
    }

    private static Dictionary<string, McpServerDefinitionRecord> BuildEffectiveServerMap(
        IReadOnlyList<McpProfileRecord> profiles,
        string targetProfile)
    {
        var normalizedTargetProfile = WorkspaceProfiles.NormalizeId(targetProfile);
        var globalRecord = profiles.FirstOrDefault(item => string.Equals(item.Profile, WorkspaceProfiles.GlobalId, StringComparison.OrdinalIgnoreCase));
        var globalServers = globalRecord is null
            ? new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase)
            : ParseJsonServerDefinitions(globalRecord.RawJson);
        if (WorkspaceProfiles.IsGlobal(normalizedTargetProfile))
        {
            return globalServers;
        }

        var profileRecord = profiles.FirstOrDefault(item => string.Equals(item.Profile, normalizedTargetProfile, StringComparison.OrdinalIgnoreCase));
        if (profileRecord is null)
        {
            return globalServers;
        }

        foreach (var entry in ParseJsonServerDefinitions(profileRecord.RawJson))
        {
            globalServers[entry.Key] = entry.Value;
        }

        return globalServers;
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseJsonServerDefinitions(string rawJson)
    {
        var root = ParseJsonObject(rawJson);
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        if (root["mcpServers"] is not JsonObject serverObject)
        {
            return servers;
        }

        foreach (var entry in serverObject)
        {
            if (entry.Value is not JsonObject currentObject)
            {
                continue;
            }

            var args = currentObject["args"] is JsonArray argsArray
                ? argsArray
                    .Select(item => item?.GetValue<string>())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .ToArray()
                : Array.Empty<string>();
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (currentObject["env"] is JsonObject envObject)
            {
                foreach (var envEntry in envObject)
                {
                    env[envEntry.Key] = envEntry.Value?.GetValue<string>() ?? string.Empty;
                }
            }

            servers[entry.Key] = new McpServerDefinitionRecord(
                currentObject["command"]?.GetValue<string>() ?? string.Empty,
                args,
                env);
        }

        return servers;
    }

    private static Dictionary<string, McpServerDefinitionRecord> ParseTomlServerDefinitions(string rawToml)
    {
        var root = Toml.ToModel(rawToml) as TomlTable ?? new TomlTable();
        var servers = new Dictionary<string, McpServerDefinitionRecord>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetValue("mcp_servers", out var rawServers) || rawServers is not TomlTable serverTable)
        {
            return servers;
        }

        foreach (var entry in serverTable)
        {
            if (entry.Value is not TomlTable currentTable)
            {
                continue;
            }

            var args = currentTable.TryGetValue("args", out var rawArgs)
                ? ConvertTomlArguments(rawArgs)
                : Array.Empty<string>();
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (currentTable.TryGetValue("env", out var rawEnv) && rawEnv is TomlTable envTable)
            {
                foreach (var envEntry in envTable)
                {
                    env[envEntry.Key] = Convert.ToString(envEntry.Value) ?? string.Empty;
                }
            }

            servers[entry.Key] = new McpServerDefinitionRecord(
                currentTable.TryGetValue("command", out var rawCommand) ? Convert.ToString(rawCommand) ?? string.Empty : string.Empty,
                args,
                env);
        }

        return servers;
    }

    private static string[] ConvertTomlArguments(object? rawValue)
    {
        return rawValue switch
        {
            TomlArray tomlArray => tomlArray
                .Select(item => Convert.ToString(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray(),
            IEnumerable<object> enumerable => enumerable
                .Select(item => Convert.ToString(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray(),
            _ => Array.Empty<string>()
        };
    }

    private static JsonObject ParseJsonObject(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(rawJson) as JsonObject ?? new JsonObject();
    }

    private static JsonObject CreateJsonServerDefinition(McpServerDefinitionRecord definition)
    {
        var environmentVariables = new JsonObject();
        foreach (var environmentVariable in definition.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            environmentVariables[environmentVariable.Key] = environmentVariable.Value;
        }

        return new JsonObject
        {
            ["command"] = definition.Command,
            ["args"] = new JsonArray(definition.Arguments.Select(argument => (JsonNode?)argument).ToArray()),
            ["env"] = environmentVariables
        };
    }

    private static bool DefinitionsEqual(
        IReadOnlyDictionary<string, McpServerDefinitionRecord> left,
        IReadOnlyDictionary<string, McpServerDefinitionRecord> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var rightValue) || entry.Value != rightValue)
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveScopeProfile(WorkspaceScope scope, string profile)
    {
        return scope == WorkspaceScope.Global ? WorkspaceProfiles.GlobalId : WorkspaceProfiles.NormalizeId(profile);
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
            if (TryResolvePathCandidate(Path.Combine(pathSegment.Trim(), expandedCommand), out resolvedPath))
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

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe;.cmd;.bat;.ps1")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var extension in extensions)
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

    private sealed class NoOpMcpClientConfigService : IMcpClientConfigService
    {
        public Task<McpValidationSnapshot> InspectAsync(
            string hubRoot,
            WorkspaceScope scope,
            string profile,
            string? projectPath,
            IReadOnlyDictionary<string, McpServerDefinitionRecord> managedServers,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new McpValidationSnapshot(
                scope,
                profile,
                projectPath,
                Array.Empty<McpClientConfigStatusRecord>(),
                [new McpValidationIssueRecord(McpValidationSeverity.Warning, "当前未接入客户端配置服务。")],
                Array.Empty<McpExternalServerPreviewRecord>()));
        }

        public Task<OperationResult> SyncAsync(
            string hubRoot,
            WorkspaceScope scope,
            string profile,
            string? projectPath,
            IReadOnlyDictionary<string, McpServerDefinitionRecord> managedServers,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Fail("当前未接入客户端配置服务。"));
        }
    }
}
