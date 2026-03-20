using System.Text.Json;
using System.Text.Json.Nodes;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class McpControlService
{
    private readonly IHubRootLocator _hubRootLocator;
    private readonly Func<string?, IMcpProfileStore> _mcpProfileStoreFactory;
    private readonly Func<string?, IMcpRuntimeStore> _mcpRuntimeStoreFactory;
    private readonly Func<string?, IHubSettingsStore>? _hubSettingsStoreFactory;
    private readonly IMcpProcessController _mcpProcessController;
    private readonly IMcpAutomationService _mcpAutomationService;
    private readonly IMcpClientConfigService _mcpClientConfigService;
    private readonly IMcpEffectiveConfigReader _mcpEffectiveConfigReader;

    public McpControlService(
        IHubRootLocator hubRootLocator,
        IMcpProfileStore mcpProfileStore,
        IMcpRuntimeStore mcpRuntimeStore,
        IMcpProcessController mcpProcessController,
        IMcpAutomationService mcpAutomationService)
        : this(
            hubRootLocator,
            _ => mcpProfileStore,
            _ => mcpRuntimeStore,
            mcpProcessController,
            mcpAutomationService,
            null,
            null,
            null)
    {
    }

    public McpControlService(
        IHubRootLocator hubRootLocator,
        Func<string?, IMcpProfileStore> mcpProfileStoreFactory,
        Func<string?, IMcpRuntimeStore> mcpRuntimeStoreFactory,
        IMcpProcessController mcpProcessController,
        IMcpAutomationService mcpAutomationService,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory,
        IMcpClientConfigService? mcpClientConfigService = null,
        IMcpEffectiveConfigReader? mcpEffectiveConfigReader = null)
    {
        _hubRootLocator = hubRootLocator;
        _mcpProfileStoreFactory = mcpProfileStoreFactory;
        _mcpRuntimeStoreFactory = mcpRuntimeStoreFactory;
        _mcpProcessController = mcpProcessController;
        _mcpAutomationService = mcpAutomationService;
        _hubSettingsStoreFactory = hubSettingsStoreFactory;
        _mcpClientConfigService = mcpClientConfigService ?? new NoOpMcpClientConfigService();
        _mcpEffectiveConfigReader = mcpEffectiveConfigReader ?? new ManifestBackedMcpEffectiveConfigReader(mcpProfileStoreFactory);
    }

    public async Task<McpWorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return new McpWorkspaceSnapshot(
                resolution,
                Array.Empty<McpProfileRecord>(),
                Array.Empty<McpRuntimeRecord>(),
                new McpRuntimeSummary(0, 0, 0, 0, 0, 0, 0, Array.Empty<string>()));
        }

        var profileStore = _mcpProfileStoreFactory(resolution.RootPath);
        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var existingRecords = await runtimeStore.GetAllAsync(cancellationToken);

        if (_hubSettingsStoreFactory is not null)
        {
            var settings = await _hubSettingsStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
            if (settings.AutoStartManagedMcpOnLoad)
            {
                existingRecords = await EnsureAutoStartAsync(runtimeStore, existingRecords, cancellationToken);
            }
        }

        var profiles = await profileStore.GetAllAsync(cancellationToken);
        var managedProcesses = await RefreshManagedProcessesAsync(runtimeStore, existingRecords, cancellationToken);
        var summary = BuildRuntimeSummary(managedProcesses);

        return new McpWorkspaceSnapshot(
            resolution,
            profiles,
            managedProcesses,
            summary);
    }

    public async Task<OperationResult> SaveManifestAsync(string profile, string rawJson, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶淇濆瓨 MCP 娓呭崟銆?, string.Join(Environment.NewLine, resolution.Errors)");
        }
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        string normalizedJson;
        try
        {
            normalizedJson = NormalizeManifestJson(rawJson);
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("The MCP manifest JSON is invalid.", exception.Message);
        }

        await _mcpProfileStoreFactory(resolution.RootPath).SaveManifestAsync(normalizedProfile, normalizedJson, cancellationToken);
        var manifestPath = Path.Combine(resolution.RootPath, "mcp", "manifest", normalizedProfile + ".json");
        return OperationResult.Ok("MCP 娓呭崟宸蹭繚瀛樸€?, manifestPath");
    }

    public async Task<OperationResult> SaveServerBindingsAsync(
        string serverName,
        string rawServerJson,
        IReadOnlyList<string> targetProfiles,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub hub root is invalid. MCP server bindings could not be saved.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedServerName = McpServerNameAliases.ToCanonical(serverName);
        if (string.IsNullOrWhiteSpace(normalizedServerName))
        {
            return OperationResult.Fail("Select or enter an MCP server name before saving bindings.");
        }

        JsonNode? parsedServerDefinition;
        try
        {
            parsedServerDefinition = string.IsNullOrWhiteSpace(rawServerJson)
                ? new JsonObject()
                : JsonNode.Parse(rawServerJson);
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("The MCP server definition is not valid JSON.", exception.Message);
        }

        var normalizedTargets = targetProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var store = _mcpProfileStoreFactory(resolution.RootPath);
        var profiles = await store.GetAllAsync(cancellationToken);

        foreach (var profile in profiles)
        {
            var manifest = ParseManifestObject(profile.RawJson);
            var servers = manifest["mcpServers"] as JsonObject ?? new JsonObject();
            manifest["mcpServers"] = servers;

            if (normalizedTargets.Contains(profile.Profile, StringComparer.OrdinalIgnoreCase))
            {
                servers[normalizedServerName] = parsedServerDefinition?.DeepClone();
            }
            else if (servers.ContainsKey(normalizedServerName))
            {
                servers.Remove(normalizedServerName);
            }

            await store.SaveManifestAsync(profile.Profile, NormalizeManifestJson(manifest.ToJsonString()), cancellationToken);
        }

        return OperationResult.Ok(
            "MCP server bindings saved.",
            $"{normalizedServerName}{Environment.NewLine}{string.Join(Environment.NewLine, normalizedTargets)}");
    }

    public async Task<OperationResult> GenerateConfigsAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鐢熸垚 MCP 閰嶇疆銆?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        return await _mcpAutomationService.GenerateConfigsAsync(resolution.RootPath, cancellationToken);
    }

    public async Task<OperationResult> SaveManagedProcessAsync(string? originalName, McpRuntimeRecord draft, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶淇濆瓨鎵樼杩涚▼瀹氫箟銆?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        McpRuntimeRecord normalizedRecord;
        try
        {
            normalizedRecord = NormalizeManagedProcess(draft);
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("The managed MCP process definition is invalid.", exception.Message);
        }

        var validationError = ValidateManagedProcess(normalizedRecord);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return OperationResult.Fail(validationError);
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var existingRecords = await runtimeStore.GetAllAsync(cancellationToken);
        var originalRecord = existingRecords.FirstOrDefault(record => NamesMatch(record.Name, originalName));

        if (originalRecord is not null
            && originalRecord.IsRunning
            && !NamesMatch(originalRecord.Name, normalizedRecord.Name))
        {
            return OperationResult.Fail("杩愯涓殑鎵樼杩涚▼涓嶈兘鐩存帴鏀瑰悕锛岃鍏堝仠姝㈠悗鍐嶄慨鏀瑰悕绉般€?, originalRecord.Name");
        }

        var updatedRecords = existingRecords
            .Where(record => !NamesMatch(record.Name, originalName))
            .ToList();

        if (updatedRecords.Any(record => NamesMatch(record.Name, normalizedRecord.Name)))
        {
            return OperationResult.Fail("宸插瓨鍦ㄥ悓鍚嶆墭绠¤繘绋嬶紝璇锋洿鎹㈠悕绉般€?, normalizedRecord.Name");
        }

        if (originalRecord is not null)
        {
            normalizedRecord = normalizedRecord with
            {
                StandardOutputLogPath = originalRecord.StandardOutputLogPath,
                StandardErrorLogPath = originalRecord.StandardErrorLogPath,
                LastOutputSnippet = originalRecord.LastOutputSnippet,
                LastErrorSnippet = originalRecord.LastErrorSnippet,
                ProcessId = originalRecord.ProcessId,
                ProcessStartedAt = originalRecord.ProcessStartedAt,
                IsRunning = originalRecord.IsRunning,
                LastExitCode = originalRecord.LastExitCode
            };
        }

        updatedRecords.Add(normalizedRecord);
        await runtimeStore.SaveAllAsync(SortManagedProcesses(updatedRecords), cancellationToken);

        return OperationResult.Ok("鎵樼鍨?MCP 瀹氫箟宸蹭繚瀛樸€?, normalizedRecord.Name");
    }

    public async Task<OperationResult> DeleteManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("璇峰厛閫夋嫨瑕佸垹闄ょ殑鎵樼杩涚▼銆?");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鍒犻櫎鎵樼杩涚▼瀹氫箟銆?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("鏈壘鍒拌鍒犻櫎鐨勬墭绠¤繘绋嬨€?, name");
        }

        if (existingRecord.IsRunning)
        {
            var stopResult = await _mcpProcessController.StopAsync(existingRecord, cancellationToken);
            existingRecord = stopResult.Record;
            if (!stopResult.Result.Success)
            {
                return stopResult.Result;
            }
        }

        var updatedRecords = records
            .Where(record => !NamesMatch(record.Name, name))
            .ToArray();

        await runtimeStore.SaveAllAsync(updatedRecords, cancellationToken);
        return OperationResult.Ok("鎵樼鍨?MCP 瀹氫箟宸插垹闄ゃ€?, name");
    }

    public Task<OperationResult> StartManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        return ExecuteManagedProcessAsync(
            name,
            (controller, record, token) => controller.StartAsync(record, token),
            "鎵樼鍨?MCP 宸插惎鍔ㄣ€?",
            cancellationToken);
    }

    public Task<OperationResult> StopManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        return ExecuteManagedProcessAsync(
            name,
            (controller, record, token) => controller.StopAsync(record, token),
            "鎵樼鍨?MCP 宸插仠姝€?",
            cancellationToken);
    }

    public async Task<OperationResult> RestartManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("璇峰厛閫夋嫨瑕侀噸鍚殑鎵樼杩涚▼銆?");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶閲嶅惎鎵樼杩涚▼銆?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("鏈壘鍒拌閲嶅惎鐨勬墭绠¤繘绋嬨€?, name");
        }

        var refreshedRecord = await _mcpProcessController.RefreshAsync(existingRecord, cancellationToken);
        if (refreshedRecord.IsRunning)
        {
            var stopResult = await _mcpProcessController.StopAsync(refreshedRecord, cancellationToken);
            if (!stopResult.Result.Success)
            {
                await SaveManagedProcessRecordAsync(runtimeStore, records, stopResult.Record, cancellationToken);
                return stopResult.Result;
            }

            refreshedRecord = stopResult.Record;
        }

        var startResult = await _mcpProcessController.StartAsync(refreshedRecord, cancellationToken);
        await SaveManagedProcessRecordAsync(runtimeStore, records, startResult.Record, cancellationToken);
        return startResult.Result.Success
            ? OperationResult.Ok("鎵樼鍨?MCP 宸查噸鍚€?", startResult.Result.Details)
            : startResult.Result;
    }

    public async Task<OperationResult> RunHealthCheckAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("璇峰厛閫夋嫨瑕佹鏌ョ殑鎵樼杩涚▼銆?");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鎵ц鍋ュ悍妫€鏌ャ€?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("鏈壘鍒拌妫€鏌ョ殑鎵樼杩涚▼銆?, name");
        }

        var refreshedRecord = await _mcpProcessController.RefreshAsync(existingRecord, cancellationToken);
        await SaveManagedProcessRecordAsync(runtimeStore, records, refreshedRecord, cancellationToken);

        return OperationResult.Ok(
            "鍋ュ悍妫€鏌ュ凡瀹屾垚銆?",
            string.Join(Environment.NewLine, new[]
            {
                refreshedRecord.LastHealthStatus,
                refreshedRecord.LastHealthMessage,
                refreshedRecord.StandardOutputLogPath,
                refreshedRecord.StandardErrorLogPath
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private async Task<OperationResult?> EnsureManagedMcpRiskAcceptedAsync(CancellationToken cancellationToken)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath) || _hubSettingsStoreFactory is null)
        {
            return null;
        }

        var settings = await _hubSettingsStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
        if (settings.ManagedMcpRiskAccepted)
        {
            return null;
        }

        return OperationResult.Fail("棣栨杩愯鎵樼 MCP 鍓嶏紝璇峰厛瀹屾垚椋庨櫓纭銆?, resolution.RootPath");
    }
    private async Task<OperationResult> ExecuteManagedProcessAsync(
        string name,
        Func<IMcpProcessController, McpRuntimeRecord, CancellationToken, Task<McpProcessCommandResult>> action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("璇峰厛閫夋嫨涓€涓墭绠¤繘绋嬨€?");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鎵ц鎵樼杩涚▼鎿嶄綔銆?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("鏈壘鍒板搴旂殑鎵樼杩涚▼銆?, name");
        }

        var result = await action(_mcpProcessController, existingRecord, cancellationToken);
        await SaveManagedProcessRecordAsync(runtimeStore, records, result.Record, cancellationToken);

        return result.Result.Success
            ? OperationResult.Ok(successMessage, result.Result.Details)
            : result.Result;
    }

    private async Task<IReadOnlyList<McpRuntimeRecord>> RefreshManagedProcessesAsync(
        IMcpRuntimeStore runtimeStore,
        IReadOnlyList<McpRuntimeRecord>? existingRecords,
        CancellationToken cancellationToken)
    {
        var records = existingRecords ?? await runtimeStore.GetAllAsync(cancellationToken);
        if (records.Count == 0)
        {
            return Array.Empty<McpRuntimeRecord>();
        }

        var refreshedRecords = new List<McpRuntimeRecord>(records.Count);
        foreach (var record in records)
        {
            refreshedRecords.Add(await _mcpProcessController.RefreshAsync(record, cancellationToken));
        }

        if (!records.SequenceEqual(refreshedRecords))
        {
            await runtimeStore.SaveAllAsync(SortManagedProcesses(refreshedRecords), cancellationToken);
        }

        return SortManagedProcesses(refreshedRecords);
    }

    private async Task<IReadOnlyList<McpRuntimeRecord>> EnsureAutoStartAsync(
        IMcpRuntimeStore runtimeStore,
        IReadOnlyList<McpRuntimeRecord> existingRecords,
        CancellationToken cancellationToken)
    {
        if (existingRecords.Count == 0)
        {
            return existingRecords;
        }

        var updatedRecords = new List<McpRuntimeRecord>(existingRecords.Count);
        var changed = false;

        foreach (var record in existingRecords)
        {
            var refreshedRecord = await _mcpProcessController.RefreshAsync(record, cancellationToken);
            if (refreshedRecord.AutoStart && refreshedRecord.IsEnabled && !refreshedRecord.IsRunning)
            {
                var startResult = await _mcpProcessController.StartAsync(refreshedRecord, cancellationToken);
                refreshedRecord = startResult.Record;
                changed = true;
            }

            updatedRecords.Add(refreshedRecord);
        }

        if (changed || !existingRecords.SequenceEqual(updatedRecords))
        {
            await runtimeStore.SaveAllAsync(SortManagedProcesses(updatedRecords), cancellationToken);
        }

        return updatedRecords;
    }

    private static async Task SaveManagedProcessRecordAsync(
        IMcpRuntimeStore runtimeStore,
        IReadOnlyList<McpRuntimeRecord> existingRecords,
        McpRuntimeRecord updatedRecord,
        CancellationToken cancellationToken)
    {
        var updatedRecords = existingRecords
            .Where(record => !NamesMatch(record.Name, updatedRecord.Name))
            .ToList();

        updatedRecords.Add(updatedRecord);
        await runtimeStore.SaveAllAsync(SortManagedProcesses(updatedRecords), cancellationToken);
    }

    private static McpRuntimeRecord NormalizeManagedProcess(McpRuntimeRecord record)
    {
        var normalizedArguments = record.Arguments
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .Select(argument => argument.Trim())
            .ToArray();

        var normalizedEnvironment = (record.EnvironmentVariables ?? new Dictionary<string, string>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .ToDictionary(
                entry => entry.Key.Trim(),
                entry => entry.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return record with
        {
            Name = record.Name.Trim(),
            Mode = McpServerMode.ProcessManaged,
            Command = record.Command.Trim(),
            Arguments = normalizedArguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(record.WorkingDirectory) ? null : Path.GetFullPath(record.WorkingDirectory),
            EnvironmentVariables = normalizedEnvironment,
            HealthCheckUrl = string.IsNullOrWhiteSpace(record.HealthCheckUrl) ? null : record.HealthCheckUrl.Trim(),
            HealthCheckTimeoutSeconds = Math.Clamp(record.HealthCheckTimeoutSeconds <= 0 ? 5 : record.HealthCheckTimeoutSeconds, 1, 60),
            LastCheckedAt = record.LastCheckedAt ?? DateTimeOffset.Now
        };
    }

    private static string? ValidateManagedProcess(McpRuntimeRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
        {
            return "鎵樼杩涚▼鍚嶇О涓嶈兘涓虹┖銆?";
        }

        if (string.IsNullOrWhiteSpace(record.Command))
        {
            return "鍚姩鍛戒护涓嶈兘涓虹┖銆?";
        }

        if (!string.IsNullOrWhiteSpace(record.WorkingDirectory) && !Directory.Exists(record.WorkingDirectory))
        {
            return "宸ヤ綔鐩綍涓嶅瓨鍦細" + record.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(record.HealthCheckUrl)
            && !Uri.TryCreate(record.HealthCheckUrl, UriKind.Absolute, out _))
        {
            return "鍋ュ悍妫€鏌ュ湴鍧€鏃犳晥锛? + record.HealthCheckUrl";
        }

        return null;
    }

    private static bool NamesMatch(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<McpRuntimeRecord> SortManagedProcesses(IEnumerable<McpRuntimeRecord> records)
    {
        return records
            .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static McpRuntimeSummary BuildRuntimeSummary(IReadOnlyList<McpRuntimeRecord> records)
    {
        var runningCount = records.Count(record => record.IsRunning);
        var stoppedCount = records.Count(record => !record.IsRunning);
        var suspendedCount = records.Count(IsSuspendedRecord);
        var recoverableCount = records.Count(IsRecoverableRecord);
        var attentionCount = records.Count(RequiresAttention);
        var alertCount = records.Count(record => IsRecoverableRecord(record) || RequiresAttention(record));

        return new McpRuntimeSummary(
            records.Count,
            runningCount,
            stoppedCount,
            alertCount,
            recoverableCount,
            attentionCount,
            suspendedCount,
            records.Select(record => record.Name).ToArray());
    }

    private static bool IsRecoverableRecord(McpRuntimeRecord record)
    {
        return record.Mode == McpServerMode.ProcessManaged
            && record.IsEnabled
            && record.KeepAlive
            && !record.IsRunning
            && record.SupervisorState != McpSupervisorState.SuspendedBySupervisor;
    }

    private static bool IsSuspendedRecord(McpRuntimeRecord record)
    {
        return record.Mode == McpServerMode.ProcessManaged
            && record.SupervisorState == McpSupervisorState.SuspendedBySupervisor;
    }

    private static bool RequiresAttention(McpRuntimeRecord record)
    {
        return record.Mode == McpServerMode.ProcessManaged
            && (IsSuspendedRecord(record)
                || HasHealthAlert(record)
                || (record.IsEnabled && !record.IsRunning && !record.KeepAlive));
    }

    private static bool HasHealthAlert(McpRuntimeRecord record)
    {
        return string.Equals(NormalizeHealthValue(record.LastHealthStatus), "异常", StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeHealthValue(record.LastHealthStatus), "寮傚父", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(record.LastHealthMessage)
                && (record.LastHealthMessage.Contains("失败", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("错误", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("异常", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("unhealthy", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("澶辫触", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("閿欒", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("寮傚父", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? NormalizeHealthValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Contains("异常", StringComparison.OrdinalIgnoreCase)
            || value.Contains("error", StringComparison.OrdinalIgnoreCase)
            || value.Contains("unhealthy", StringComparison.OrdinalIgnoreCase)
            || value.Contains("寮傚父", StringComparison.OrdinalIgnoreCase)
            || value.Contains("瀵倸鐖?", StringComparison.OrdinalIgnoreCase))
        {
            return "异常";
        }

        if (value.Contains("健康", StringComparison.OrdinalIgnoreCase)
            || value.Contains("healthy", StringComparison.OrdinalIgnoreCase)
            || value.Contains("鍋ュ悍", StringComparison.OrdinalIgnoreCase))
        {
            return "健康";
        }

        return value;
    }

    private static string NormalizeManifestJson(string rawJson)
    {
        var parsedNode = string.IsNullOrWhiteSpace(rawJson) ? new JsonObject() : JsonNode.Parse(rawJson);
        if (parsedNode is not JsonObject rootNode)
        {
            throw new InvalidOperationException("MCP 濞撳懎宕熼弽纭呭Ν閻愮懓绻€妞ょ粯妲?JSON 鐎电钖勩€?");
        }

        if (rootNode["mcpServers"] is null)
        {
            rootNode["mcpServers"] = new JsonObject();
        }
        else if (rootNode["mcpServers"] is not JsonObject)
        {
            throw new InvalidOperationException("mcpServers 韫囧懘銆忛弰?JSON 鐎电钖勩€?");
        }

        return rootNode.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static JsonObject ParseManifestObject(string rawJson)
    {
        var parsedNode = string.IsNullOrWhiteSpace(rawJson) ? new JsonObject() : JsonNode.Parse(rawJson);
        if (parsedNode is not JsonObject rootNode)
        {
            throw new InvalidOperationException("MCP manifest must be a JSON object.");
        }

        if (rootNode["mcpServers"] is null)
        {
            rootNode["mcpServers"] = new JsonObject();
        }
        else if (rootNode["mcpServers"] is not JsonObject)
        {
            throw new InvalidOperationException("mcpServers must be a JSON object.");
        }

        return rootNode;
    }

    private sealed class ManifestBackedMcpEffectiveConfigReader : IMcpEffectiveConfigReader
    {
        private readonly Func<string?, IMcpProfileStore> _profileStoreFactory;

        public ManifestBackedMcpEffectiveConfigReader(Func<string?, IMcpProfileStore> profileStoreFactory)
        {
            _profileStoreFactory = profileStoreFactory;
        }

        public async Task<IReadOnlyDictionary<string, McpServerDefinitionRecord>> GetEffectiveServersAsync(
            string hubRoot,
            string profile,
            CancellationToken cancellationToken = default)
        {
            var profileRecords = await _profileStoreFactory(hubRoot).GetAllAsync(cancellationToken);
            return BuildEffectiveServerMap(profileRecords, profile);
        }
    }
}


