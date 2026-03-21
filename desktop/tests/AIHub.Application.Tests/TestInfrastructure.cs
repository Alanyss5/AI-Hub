using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Tests;

internal sealed class TestHubRootScope : IDisposable
{
    public TestHubRootScope()
    {
        RootPath = Path.Combine("C:\\AI-Hub", ".test-temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
        {
            return;
        }

        DeleteDirectoryRobust(RootPath);
    }

    private static void DeleteDirectoryRobust(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }

                foreach (var directoryPath in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                             .OrderByDescending(item => item.Length))
                {
                    File.SetAttributes(directoryPath, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }

        Directory.Delete(path, recursive: true);
    }
}

internal sealed class FixedHubRootLocator : IHubRootLocator
{
    private readonly HubRootResolution _resolution;
    private string? _preferredRoot;

    public FixedHubRootLocator(string rootPath)
    {
        _preferredRoot = rootPath;
        _resolution = new HubRootResolution(rootPath, true, "tests", Array.Empty<string>());
    }

    public Task<HubRootResolution> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(_resolution);

    public Task<HubRootResolution> EvaluateAsync(string candidatePath, CancellationToken cancellationToken = default)
        => Task.FromResult(new HubRootResolution(candidatePath, Directory.Exists(candidatePath), "tests", Array.Empty<string>()));

    public void SetPreferredRoot(string? rootPath) => _preferredRoot = rootPath;

    public string? GetPreferredRoot() => _preferredRoot;
}

internal sealed class NoOpWorkspaceAutomationService : IWorkspaceAutomationService
{
    public Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(string hubRoot, CancellationToken cancellationToken = default)
        => Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "ok",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Global,
                WorkspaceProfiles.GlobalId,
                null,
                false,
                false,
                Array.Empty<WorkspaceOnboardingCandidate>(),
                "ok")));

    public Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
        string hubRoot,
        string projectPath,
        string profile,
        CancellationToken cancellationToken = default)
        => Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "ok",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Project,
                profile,
                projectPath,
                false,
                false,
                Array.Empty<WorkspaceOnboardingCandidate>(),
                "ok")));

    public Task<OperationResult> ApplyGlobalLinksAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok("ok", hubRoot));

    public Task<OperationResult> ApplyProjectProfileAsync(
        string hubRoot,
        string projectPath,
        string profile,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok("ok", projectPath));
}

internal sealed class RecordingWorkspaceAutomationService : IWorkspaceAutomationService
{
    public int ApplyGlobalLinksCallCount { get; private set; }

    public int ApplyProjectProfileCallCount { get; private set; }

    public string? LastAppliedProjectPath { get; private set; }

    public string? LastAppliedProjectProfile { get; private set; }

    public int PreviewGlobalOnboardingCallCount { get; private set; }

    public int PreviewProjectOnboardingCallCount { get; private set; }

    public WorkspaceOnboardingPreviewResult GlobalPreviewResult { get; set; } = WorkspaceOnboardingPreviewResult.Ok(
        "ok",
        new WorkspaceOnboardingPreview(WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null, false, false, Array.Empty<WorkspaceOnboardingCandidate>(), "ok"));

    public WorkspaceOnboardingPreviewResult ProjectPreviewResult { get; set; } = WorkspaceOnboardingPreviewResult.Ok(
        "ok",
        new WorkspaceOnboardingPreview(WorkspaceScope.Project, WorkspaceProfiles.GlobalId, null, false, false, Array.Empty<WorkspaceOnboardingCandidate>(), "ok"));

    public OperationResult ApplyGlobalLinksResult { get; set; } = OperationResult.Ok("ok");

    public OperationResult ApplyProjectProfileResult { get; set; } = OperationResult.Ok("ok");

    public Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(string hubRoot, CancellationToken cancellationToken = default)
    {
        PreviewGlobalOnboardingCallCount++;
        return Task.FromResult(GlobalPreviewResult);
    }

    public Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
        string hubRoot,
        string projectPath,
        string profile,
        CancellationToken cancellationToken = default)
    {
        PreviewProjectOnboardingCallCount++;
        var result = ProjectPreviewResult.Preview is null
            ? ProjectPreviewResult
            : ProjectPreviewResult with
            {
                Preview = ProjectPreviewResult.Preview with
                {
                    Scope = WorkspaceScope.Project,
                    Profile = profile,
                    ProjectPath = projectPath
                }
            };
        return Task.FromResult(result);
    }

    public Task<OperationResult> ApplyGlobalLinksAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        ApplyGlobalLinksCallCount++;
        return Task.FromResult(ApplyGlobalLinksResult);
    }

    public Task<OperationResult> ApplyProjectProfileAsync(
        string hubRoot,
        string projectPath,
        string profile,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        ApplyProjectProfileCallCount++;
        LastAppliedProjectPath = projectPath;
        LastAppliedProjectProfile = profile;
        return Task.FromResult(ApplyProjectProfileResult);
    }
}

internal sealed class NoOpMcpAutomationService : IMcpAutomationService
{
    public Task<OperationResult> GenerateConfigsAsync(string hubRoot, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok("ok", hubRoot));
}

internal sealed class NoOpScriptExecutionService : IScriptExecutionService
{
    public Task<OperationResult> RunAsync(string scriptPath, IReadOnlyList<string> arguments, string successMessage, string failureMessage, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok(successMessage, scriptPath + Environment.NewLine + string.Join(' ', arguments)));
}

internal sealed class RecordingScriptExecutionService : IScriptExecutionService
{
    public List<(string ScriptPath, IReadOnlyList<string> Arguments, string SuccessMessage, string FailureMessage)> Calls { get; } = new();

    public OperationResult Result { get; set; } = OperationResult.Ok("ok");

    public Action<string, IReadOnlyList<string>>? OnRun { get; set; }

    public Task<OperationResult> RunAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken = default)
    {
        OnRun?.Invoke(scriptPath, arguments);
        Calls.Add((scriptPath, arguments.ToArray(), successMessage, failureMessage));
        return Task.FromResult(Result);
    }
}

internal sealed class PassthroughMcpProcessController : IMcpProcessController
{
    public Task<McpRuntimeRecord> RefreshAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
        => Task.FromResult(record);

    public Task<McpProcessCommandResult> StartAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
        => Task.FromResult(new McpProcessCommandResult(OperationResult.Ok("started"), record with { IsRunning = true, ProcessId = record.ProcessId ?? 1001 }));

    public Task<McpProcessCommandResult> StopAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
        => Task.FromResult(new McpProcessCommandResult(OperationResult.Ok("stopped"), record with { IsRunning = false, ProcessId = null }));
}

internal sealed class FakePlatformCapabilitiesService : IPlatformCapabilitiesService
{
    private readonly PlatformCapabilitySnapshot _snapshot;

    public FakePlatformCapabilitiesService(
        bool supportsJunctionLinks = true,
        bool supportsTrayIcon = true,
        bool supportsNotifications = true,
        bool supportsManagedProcessSupervisor = true,
        bool isSupported = true,
        string summary = "ok")
    {
        _snapshot = new PlatformCapabilitySnapshot(
            PlatformName: "tests",
            SupportsJunctionLinks: supportsJunctionLinks,
            SupportsTrayIcon: supportsTrayIcon,
            SupportsNotifications: supportsNotifications,
            SupportsManagedProcessSupervisor: supportsManagedProcessSupervisor,
            IsSupported: isSupported,
            Summary: summary);
    }

    public PlatformCapabilitySnapshot Describe() => _snapshot;
}

internal sealed class RecordingPlatformLinkService : IPlatformLinkService
{
    public List<string> EnsuredDirectories { get; } = new();

    public List<(string LinkPath, string TargetPath, bool IgnoreIfLocked)> Junctions { get; } = new();

    public void EnsureDirectory(string path)
    {
        var normalized = Path.GetFullPath(path);
        EnsuredDirectories.Add(normalized);
        Directory.CreateDirectory(normalized);
    }

    public void EnsureJunction(string linkPath, string targetPath, bool ignoreIfLocked = false)
    {
        Junctions.Add((Path.GetFullPath(linkPath), Path.GetFullPath(targetPath), ignoreIfLocked));
        var parent = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
