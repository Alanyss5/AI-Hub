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
    private readonly Func<string?, IProjectRegistry>? _projectRegistryFactory;
    private readonly IMcpProcessController _mcpProcessController;
    private readonly IMcpAutomationService _mcpAutomationService;
    private readonly IWorkspaceAutomationService? _workspaceAutomationService;
    private readonly IMcpClientConfigService _mcpClientConfigService;
    private readonly IMcpEffectiveConfigReader _mcpEffectiveConfigReader;
    private readonly ISourcePathLayout _sourcePathLayout;
    private readonly Func<string, string> _personalRootResolver;

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
            null,
            null,
            null)
    {
    }

    public McpControlService(
        IHubRootLocator hubRootLocator,
        IMcpProfileStore mcpProfileStore,
        IMcpRuntimeStore mcpRuntimeStore,
        IMcpProcessController mcpProcessController,
        IMcpAutomationService mcpAutomationService,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory,
        Func<string?, IProjectRegistry>? projectRegistryFactory = null,
        IWorkspaceAutomationService? workspaceAutomationService = null,
        IMcpClientConfigService? mcpClientConfigService = null,
        IMcpEffectiveConfigReader? mcpEffectiveConfigReader = null,
        ISourcePathLayout? sourcePathLayout = null,
        Func<string, string>? personalRootResolver = null)
        : this(
            hubRootLocator,
            _ => mcpProfileStore,
            _ => mcpRuntimeStore,
            mcpProcessController,
            mcpAutomationService,
            hubSettingsStoreFactory,
            projectRegistryFactory,
            workspaceAutomationService,
            mcpClientConfigService,
            mcpEffectiveConfigReader,
            sourcePathLayout,
            personalRootResolver)
    {
    }

    public McpControlService(
        IHubRootLocator hubRootLocator,
        Func<string?, IMcpProfileStore> mcpProfileStoreFactory,
        IMcpRuntimeStore mcpRuntimeStore,
        IMcpProcessController mcpProcessController,
        IMcpAutomationService mcpAutomationService,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory = null,
        Func<string?, IProjectRegistry>? projectRegistryFactory = null,
        IWorkspaceAutomationService? workspaceAutomationService = null,
        IMcpClientConfigService? mcpClientConfigService = null,
        IMcpEffectiveConfigReader? mcpEffectiveConfigReader = null,
        ISourcePathLayout? sourcePathLayout = null,
        Func<string, string>? personalRootResolver = null)
        : this(
            hubRootLocator,
            mcpProfileStoreFactory,
            _ => mcpRuntimeStore,
            mcpProcessController,
            mcpAutomationService,
            hubSettingsStoreFactory,
            projectRegistryFactory,
            workspaceAutomationService,
            mcpClientConfigService,
            mcpEffectiveConfigReader,
            sourcePathLayout,
            personalRootResolver)
    {
    }

    public McpControlService(
        IHubRootLocator hubRootLocator,
        IMcpProfileStore mcpProfileStore,
        Func<string?, IMcpRuntimeStore> mcpRuntimeStoreFactory,
        IMcpProcessController mcpProcessController,
        IMcpAutomationService mcpAutomationService,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory = null,
        Func<string?, IProjectRegistry>? projectRegistryFactory = null,
        IWorkspaceAutomationService? workspaceAutomationService = null,
        IMcpClientConfigService? mcpClientConfigService = null,
        IMcpEffectiveConfigReader? mcpEffectiveConfigReader = null,
        ISourcePathLayout? sourcePathLayout = null,
        Func<string, string>? personalRootResolver = null)
        : this(
            hubRootLocator,
            _ => mcpProfileStore,
            mcpRuntimeStoreFactory,
            mcpProcessController,
            mcpAutomationService,
            hubSettingsStoreFactory,
            projectRegistryFactory,
            workspaceAutomationService,
            mcpClientConfigService,
            mcpEffectiveConfigReader,
            sourcePathLayout,
            personalRootResolver)
    {
    }

    public McpControlService(
        IHubRootLocator hubRootLocator,
        Func<string?, IMcpProfileStore> mcpProfileStoreFactory,
        Func<string?, IMcpRuntimeStore> mcpRuntimeStoreFactory,
        IMcpProcessController mcpProcessController,
        IMcpAutomationService mcpAutomationService,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory,
        Func<string?, IProjectRegistry>? projectRegistryFactory = null,
        IWorkspaceAutomationService? workspaceAutomationService = null,
        IMcpClientConfigService? mcpClientConfigService = null,
        IMcpEffectiveConfigReader? mcpEffectiveConfigReader = null,
        ISourcePathLayout? sourcePathLayout = null,
        Func<string, string>? personalRootResolver = null)
    {
        _hubRootLocator = hubRootLocator;
        _mcpProfileStoreFactory = mcpProfileStoreFactory;
        _mcpRuntimeStoreFactory = mcpRuntimeStoreFactory;
        _mcpProcessController = mcpProcessController;
        _mcpAutomationService = mcpAutomationService;
        _hubSettingsStoreFactory = hubSettingsStoreFactory;
        _projectRegistryFactory = projectRegistryFactory;
        _workspaceAutomationService = workspaceAutomationService;
        _mcpClientConfigService = mcpClientConfigService ?? new NoOpMcpClientConfigService();
        _mcpEffectiveConfigReader = mcpEffectiveConfigReader ?? new ManifestBackedMcpEffectiveConfigReader(mcpProfileStoreFactory);
        _sourcePathLayout = sourcePathLayout ?? new DefaultSourcePathLayout();
        _personalRootResolver = personalRootResolver ?? GetDefaultPersonalRoot;
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
            return OperationResult.Fail("AI-Hub root is invalid. MCP manifest could not be saved.", string.Join(Environment.NewLine, resolution.Errors));
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
        var refreshProfiles = await GetProfilesForRefreshAsync(resolution.RootPath, normalizedProfile, cancellationToken);
        var refreshResult = await RefreshRuntimeAsync(resolution.RootPath, refreshProfiles, cancellationToken);
        if (!refreshResult.Success)
        {
            return refreshResult;
        }

        return OperationResult.Ok("MCP manifest saved and runtime refreshed.", GetManifestPath(resolution.RootPath, normalizedProfile));
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
        var publishedProfiles = new List<string>();

        foreach (var profile in profiles)
        {
            var manifest = ParseManifestObject(profile.RawJson);
            var servers = manifest["mcpServers"] as JsonObject ?? new JsonObject();
            manifest["mcpServers"] = servers;

            if (normalizedTargets.Contains(profile.Profile, StringComparer.OrdinalIgnoreCase))
            {
                servers[normalizedServerName] = parsedServerDefinition?.DeepClone();
                publishedProfiles.Add(profile.Profile);
            }
            else if (servers.ContainsKey(normalizedServerName))
            {
                servers.Remove(normalizedServerName);
            }

            await store.SaveManifestAsync(profile.Profile, NormalizeManifestJson(manifest.ToJsonString()), cancellationToken);
        }

        var draftPath = GetDraftPath(resolution.RootPath, normalizedServerName);
        if (normalizedTargets.Length == 0)
        {
            await SaveDraftAsync(draftPath, normalizedServerName, parsedServerDefinition, cancellationToken);
        }
        else
        {
            DeleteDraftIfExists(draftPath);
        }

        var refreshProfiles = normalizedTargets.Length == 0
            ? profiles.Select(profile => profile.Profile).ToArray()
            : normalizedTargets.Contains(WorkspaceProfiles.GlobalId, StringComparer.OrdinalIgnoreCase)
                ? profiles.Select(profile => profile.Profile).ToArray()
                : normalizedTargets;

        var refreshResult = await RefreshRuntimeAsync(resolution.RootPath, refreshProfiles, cancellationToken);
        if (!refreshResult.Success)
        {
            return refreshResult;
        }

        var details = new List<string>
        {
            $"{normalizedServerName} -> {(normalizedTargets.Length == 0 ? "draft" : string.Join(", ", normalizedTargets))}"
        };
        if (normalizedTargets.Length == 0)
        {
            details.Add("Draft: " + draftPath);
        }
        else if (publishedProfiles.Count > 0)
        {
            details.Add("Published profiles: " + string.Join(", ", publishedProfiles.Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        return OperationResult.Ok("MCP server bindings saved and runtime refreshed.", string.Join(Environment.NewLine, details));
    }

    public async Task<OperationResult> GenerateConfigsAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub root is invalid. MCP configs could not be generated.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var profiles = (await _mcpProfileStoreFactory(resolution.RootPath).GetAllAsync(cancellationToken))
            .Select(item => item.Profile)
            .DefaultIfEmpty(WorkspaceProfiles.GlobalId)
            .ToArray();

        var result = await RefreshRuntimeAsync(resolution.RootPath, profiles, cancellationToken);
        if (!result.Success)
        {
            return result;
        }

        return OperationResult.Ok(
            "MCP configs generated and active clients refreshed.",
            result.Details);
    }

    public async Task<OperationResult> SaveManagedProcessAsync(string? originalName, McpRuntimeRecord draft, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub root is invalid. Managed process definition could not be saved.", string.Join(Environment.NewLine, resolution.Errors));
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
            return OperationResult.Fail("A running managed process cannot be renamed directly. Stop it first, then rename it.", originalRecord.Name);
        }

        var updatedRecords = existingRecords
            .Where(record => !NamesMatch(record.Name, originalName))
            .ToList();

        if (updatedRecords.Any(record => NamesMatch(record.Name, normalizedRecord.Name)))
        {
            return OperationResult.Fail("A managed process with the same name already exists.", normalizedRecord.Name);
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

        return OperationResult.Ok("Managed MCP definition saved.", normalizedRecord.Name);
    }

    public async Task<OperationResult> DeleteManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("Select the managed process to delete first.");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub root is invalid. Managed process definition could not be deleted.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("The managed process to delete was not found.", name);
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
        return OperationResult.Ok("Managed MCP definition deleted.", name);
    }

    public Task<OperationResult> StartManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        return ExecuteManagedProcessAsync(
            name,
            (controller, record, token) => controller.StartAsync(record, token),
            "Managed MCP started.",
            cancellationToken);
    }

    public Task<OperationResult> StopManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        return ExecuteManagedProcessAsync(
            name,
            (controller, record, token) => controller.StopAsync(record, token),
            "Managed MCP stopped.",
            cancellationToken);
    }

    public async Task<OperationResult> RestartManagedProcessAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("Select the managed process to restart first.");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub root is invalid. Managed process could not be restarted.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("The managed process to restart was not found.", name);
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
            ? OperationResult.Ok("Managed MCP restarted.", startResult.Result.Details)
            : startResult.Result;
    }

    public async Task<OperationResult> RunHealthCheckAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("Select the managed process to health-check first.");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub root is invalid. Health check could not run.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("The managed process to health-check was not found.", name);
        }

        var refreshedRecord = await _mcpProcessController.RefreshAsync(existingRecord, cancellationToken);
        await SaveManagedProcessRecordAsync(runtimeStore, records, refreshedRecord, cancellationToken);

        return OperationResult.Ok(
            "Health check completed.",
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

        return OperationResult.Fail("Accept the managed MCP risk confirmation before the first run.", resolution.RootPath);
    }

    private Task<OperationResult> RefreshRuntimeAsync(
        string hubRoot,
        IReadOnlyList<string> affectedProfiles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProfiles = affectedProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedProfiles.Length == 0)
        {
            return Task.FromResult(OperationResult.Ok("Runtime refresh skipped."));
        }

        _ = _personalRootResolver(hubRoot);

        if (_workspaceAutomationService is not null)
        {
            return RefreshViaWorkspaceAutomationAsync(hubRoot, normalizedProfiles, cancellationToken);
        }

        return _mcpAutomationService.GenerateConfigsAsync(hubRoot, cancellationToken);
    }

    private async Task<OperationResult> RefreshViaWorkspaceAutomationAsync(
        string hubRoot,
        IReadOnlyList<string> normalizedProfiles,
        CancellationToken cancellationToken)
    {
        await RuntimeRefreshCoordinator.RefreshAsync(
            hubRoot,
            normalizedProfiles,
            _projectRegistryFactory,
            _hubSettingsStoreFactory,
            _workspaceAutomationService,
            cancellationToken);

        return OperationResult.Ok(
            "MCP runtime and active client configs refreshed.",
            string.Join(Environment.NewLine, normalizedProfiles));
    }

    private async Task SaveDraftAsync(
        string draftPath,
        string name,
        JsonNode? parsedServerDefinition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(draftPath)!);

        var draftRecord = new McpDraftRecord
        {
            Name = name,
            DraftPath = draftPath,
            RawJson = parsedServerDefinition is null
                ? "{}"
                : parsedServerDefinition.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        };

        await File.WriteAllTextAsync(
            draftPath,
            JsonSerializer.Serialize(draftRecord, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static void DeleteDraftIfExists(string draftPath)
    {
        if (File.Exists(draftPath))
        {
            File.Delete(draftPath);
        }
    }

    private async Task<IReadOnlyList<string>> GetProfilesForRefreshAsync(string hubRoot, string profile, CancellationToken cancellationToken)
    {
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        if (WorkspaceProfiles.IsGlobal(normalizedProfile))
        {
            var profiles = await _mcpProfileStoreFactory(hubRoot).GetAllAsync(cancellationToken);
            return profiles.Select(item => item.Profile).ToArray();
        }

        return new[] { normalizedProfile };
    }

    private async Task<OperationResult> ExecuteManagedProcessAsync(
        string name,
        Func<IMcpProcessController, McpRuntimeRecord, CancellationToken, Task<McpProcessCommandResult>> action,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail("Select a managed process first.");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub root is invalid. Managed process action could not run.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var runtimeStore = _mcpRuntimeStoreFactory(resolution.RootPath);
        var records = await runtimeStore.GetAllAsync(cancellationToken);
        var existingRecord = records.FirstOrDefault(record => NamesMatch(record.Name, name));
        if (existingRecord is null)
        {
            return OperationResult.Fail("The managed process was not found.", name);
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
            return "Managed process name is required.";
        }

        if (string.IsNullOrWhiteSpace(record.Command))
        {
            return "Command is required.";
        }

        if (!string.IsNullOrWhiteSpace(record.WorkingDirectory) && !Directory.Exists(record.WorkingDirectory))
        {
            return "Working directory does not exist: " + record.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(record.HealthCheckUrl)
            && !Uri.TryCreate(record.HealthCheckUrl, UriKind.Absolute, out _))
        {
            return "Health check URL is invalid: " + record.HealthCheckUrl;
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
            || string.Equals(NormalizeHealthValue(record.LastHealthStatus), "error", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(record.LastHealthMessage)
                && (record.LastHealthMessage.Contains("失败", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("错误", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("异常", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("unhealthy", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("failed", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("warning", StringComparison.OrdinalIgnoreCase)
                    || record.LastHealthMessage.Contains("fault", StringComparison.OrdinalIgnoreCase)));
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
            || value.Contains("fault", StringComparison.OrdinalIgnoreCase)
            || value.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return "异常";
        }

        if (value.Contains("健康", StringComparison.OrdinalIgnoreCase)
            || value.Contains("healthy", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ok", StringComparison.OrdinalIgnoreCase))
        {
            return "健康";
        }

        return value;
    }

    private static string GetDefaultPersonalRoot(string _)
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(Path.GetFullPath(userHome), "AI-Personal");
    }

    private string GetCompanySourceRoot(string hubRoot)
    {
        return _sourcePathLayout.GetCompanySourceRoot(hubRoot);
    }

    private string GetManifestPath(string hubRoot, string profile)
    {
        return _sourcePathLayout.GetProfileManifestPath(GetCompanySourceRoot(hubRoot), profile);
    }

    private string GetDraftPath(string hubRoot, string draftId)
    {
        return _sourcePathLayout.GetMcpDraftPath(GetCompanySourceRoot(hubRoot), draftId);
    }

    private static string NormalizeManifestJson(string rawJson)
    {
        var parsedNode = string.IsNullOrWhiteSpace(rawJson) ? new JsonObject() : JsonNode.Parse(rawJson);
        if (parsedNode is not JsonObject rootNode)
        {
            throw new InvalidOperationException("The MCP manifest root must be a JSON object.");
        }

        if (rootNode["mcpServers"] is null)
        {
            rootNode["mcpServers"] = new JsonObject();
        }
        else if (rootNode["mcpServers"] is not JsonObject)
        {
            throw new InvalidOperationException("The mcpServers node must be a JSON object.");
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
