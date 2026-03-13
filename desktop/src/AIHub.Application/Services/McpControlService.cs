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
        IMcpClientConfigService? mcpClientConfigService = null)
    {
        _hubRootLocator = hubRootLocator;
        _mcpProfileStoreFactory = mcpProfileStoreFactory;
        _mcpRuntimeStoreFactory = mcpRuntimeStoreFactory;
        _mcpProcessController = mcpProcessController;
        _mcpAutomationService = mcpAutomationService;
        _hubSettingsStoreFactory = hubSettingsStoreFactory;
        _mcpClientConfigService = mcpClientConfigService ?? new NoOpMcpClientConfigService();
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

    public async Task<OperationResult> SaveManifestAsync(ProfileKind profile, string rawJson, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法保存 MCP 清单。", string.Join(Environment.NewLine, resolution.Errors));
        }

        string normalizedJson;
        try
        {
            normalizedJson = NormalizeManifestJson(rawJson);
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("MCP 清单格式无效。", exception.Message);
        }

        await _mcpProfileStoreFactory(resolution.RootPath).SaveManifestAsync(profile, normalizedJson, cancellationToken);
        var manifestPath = Path.Combine(resolution.RootPath, "mcp", "manifest", profile.ToStorageValue() + ".json");
        return OperationResult.Ok("MCP 清单已保存。", manifestPath);
    }

    public async Task<OperationResult> GenerateConfigsAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法生成 MCP 配置。", string.Join(Environment.NewLine, resolution.Errors));
        }

        return await _mcpAutomationService.GenerateConfigsAsync(resolution.RootPath, cancellationToken);
    }

    public async Task<OperationResult> SaveManagedProcessAsync(string? originalName, McpRuntimeRecord draft, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法保存托管进程定义。", string.Join(Environment.NewLine, resolution.Errors));
        }

        McpRuntimeRecord normalizedRecord;
        try
        {
            normalizedRecord = NormalizeManagedProcess(draft);
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("托管进程定义无效。", exception.Message);
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
            return OperationResult.Fail("运行中的托管进程不能直接改名，请先停止后再修改名称。", originalRecord.Name);
        }

        var updatedRecords = existingRecords
            .Where(record => !NamesMatch(record.Name, originalName))
            .ToList();

        if (updatedRecords.Any(record => NamesMatch(record.Name, normalizedRecord.Name)))
        {
            return OperationResult.Fail("已存在同名托管进程，请更换名称。", normalizedRecord.Name);
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

        return OperationResult.Ok("托管型 MCP 定义已保存。", normalizedRecord.Name);
    }

    public async Task<OperationResult> DeleteManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("请先选择要删除的托管进程。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法删除托管进程定义。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("未找到要删除的托管进程。", name);
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
        return OperationResult.Ok("托管型 MCP 定义已删除。", name);
    }

    public Task<OperationResult> StartManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        return ExecuteManagedProcessAsync(
            name,
            (controller, record, token) => controller.StartAsync(record, token),
            "托管型 MCP 已启动。",
            cancellationToken);
    }

    public Task<OperationResult> StopManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        return ExecuteManagedProcessAsync(
            name,
            (controller, record, token) => controller.StopAsync(record, token),
            "托管型 MCP 已停止。",
            cancellationToken);
    }

    public async Task<OperationResult> RestartManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("请先选择要重启的托管进程。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法重启托管进程。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("未找到要重启的托管进程。", name);
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
            ? OperationResult.Ok("托管型 MCP 已重启。", startResult.Result.Details)
            : startResult.Result;
    }

    public async Task<OperationResult> RunHealthCheckAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("请先选择要检查的托管进程。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法执行健康检查。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("未找到要检查的托管进程。", name);
        }

        var refreshedRecord = await _mcpProcessController.RefreshAsync(existingRecord, cancellationToken);
        await SaveManagedProcessRecordAsync(runtimeStore, records, refreshedRecord, cancellationToken);

        return OperationResult.Ok(
            "健康检查已完成。",
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

        return OperationResult.Fail("首次运行托管 MCP 前，请先完成风险确认。", resolution.RootPath);
    }
    private async Task<OperationResult> ExecuteManagedProcessAsync(
        string name,
        Func<IMcpProcessController, McpRuntimeRecord, CancellationToken, Task<McpProcessCommandResult>> action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("请先选择一个托管进程。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法执行托管进程操作。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("未找到对应的托管进程。", name);
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
            return "托管进程名称不能为空。";
        }

        if (string.IsNullOrWhiteSpace(record.Command))
        {
            return "启动命令不能为空。";
        }

        if (!string.IsNullOrWhiteSpace(record.WorkingDirectory) && !Directory.Exists(record.WorkingDirectory))
        {
            return "工作目录不存在：" + record.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(record.HealthCheckUrl)
            && !Uri.TryCreate(record.HealthCheckUrl, UriKind.Absolute, out _))
        {
            return "健康检查地址无效：" + record.HealthCheckUrl;
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
            || (!string.IsNullOrWhiteSpace(record.LastHealthMessage)
                && (record.LastHealthMessage.Contains("失败", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("错误", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("异常", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? NormalizeHealthValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Contains("异常", StringComparison.OrdinalIgnoreCase) || value.Contains("寮傚父", StringComparison.OrdinalIgnoreCase))
        {
            return "异常";
        }

        if (value.Contains("健康", StringComparison.OrdinalIgnoreCase))
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
            throw new InvalidOperationException("MCP 娓呭崟鏍硅妭鐐瑰繀椤绘槸 JSON 瀵硅薄。");
        }

        if (rootNode["mcpServers"] is null)
        {
            rootNode["mcpServers"] = new JsonObject();
        }
        else if (rootNode["mcpServers"] is not JsonObject)
        {
            throw new InvalidOperationException("mcpServers 蹇呴』鏄?JSON 瀵硅薄。");
        }

        return rootNode.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}





