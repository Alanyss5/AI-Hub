using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Application.Services;
using AIHub.Infrastructure;
using AIHub.Contracts;

namespace AIHub.Application.Tests;

public sealed class McpControlServiceTests
{
    [Fact]
    public async Task LoadAsync_ComputesRuntimeSummaryCounts()
    {
        using var scope = new TestHubRootScope();
        var runtimeStore = new JsonMcpRuntimeStore(scope.RootPath);
        await runtimeStore.SaveAllAsync(new[]
        {
            new McpRuntimeRecord { Name = "running-ok", IsEnabled = true, IsRunning = true },
            new McpRuntimeRecord { Name = "keepalive-down", IsEnabled = true, IsRunning = false, KeepAlive = true },
            new McpRuntimeRecord { Name = "manual-down", IsEnabled = true, IsRunning = false, KeepAlive = false },
            new McpRuntimeRecord { Name = "health-alert", IsEnabled = true, IsRunning = true, LastHealthStatus = "异常", LastHealthMessage = "健康检查失败" },
            new McpRuntimeRecord { Name = "disabled", IsEnabled = false, IsRunning = false }
        });

        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            new JsonMcpProfileStore(scope.RootPath),
            runtimeStore,
            new PassthroughMcpProcessController(),
            new NoOpMcpAutomationService());

        var snapshot = await service.LoadAsync();

        Assert.Equal(5, snapshot.RuntimeSummary.ManagedProcessCount);
        Assert.Equal(2, snapshot.RuntimeSummary.RunningProcessCount);
        Assert.Equal(3, snapshot.RuntimeSummary.StoppedProcessCount);
        Assert.Equal(3, snapshot.RuntimeSummary.AlertProcessCount);
        Assert.Equal(1, snapshot.RuntimeSummary.RecoverableProcessCount);
        Assert.Equal(2, snapshot.RuntimeSummary.AttentionProcessCount);
        Assert.Equal(0, snapshot.RuntimeSummary.SuspendedProcessCount);
    }

    [Fact]
    public async Task MaintainManagedProcessesAsync_Suspends_Process_After_Supervisor_Limit()
    {
        using var scope = new TestHubRootScope();
        var runtimeStore = new JsonMcpRuntimeStore(scope.RootPath);
        await runtimeStore.SaveAllAsync(new[]
        {
            new McpRuntimeRecord
            {
                Name = "unstable-mcp",
                Mode = McpServerMode.ProcessManaged,
                IsEnabled = true,
                KeepAlive = true,
                IsRunning = false,
                Command = "demo",
                BackoffSeconds = 5,
                MaxRestartAttemptsInWindow = 1,
                RestartWindowMinutes = 10
            }
        });

        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            new JsonMcpProfileStore(scope.RootPath),
            runtimeStore,
            new AlwaysFailStartMcpProcessController(),
            new NoOpMcpAutomationService());

        var result = await service.MaintainManagedProcessesAsync();
        var snapshot = await service.LoadAsync();
        var record = Assert.Single(snapshot.ManagedProcesses);

        Assert.True(result.Success, result.Details);
        Assert.Equal(McpSupervisorState.SuspendedBySupervisor, record.SupervisorState);
        Assert.Equal(1, record.ConsecutiveRestartFailures);
        Assert.Equal(1, snapshot.RuntimeSummary.SuspendedProcessCount);
        Assert.Equal(0, snapshot.RuntimeSummary.RecoverableProcessCount);
    }

    [Fact]
    public async Task ResumeSuspendedManagedProcessesAsync_Restarts_Suspended_Process()
    {
        using var scope = new TestHubRootScope();
        var runtimeStore = new JsonMcpRuntimeStore(scope.RootPath);
        await runtimeStore.SaveAllAsync(new[]
        {
            new McpRuntimeRecord
            {
                Name = "suspended-mcp",
                Mode = McpServerMode.ProcessManaged,
                IsEnabled = true,
                KeepAlive = true,
                IsRunning = false,
                Command = "demo",
                SupervisorState = McpSupervisorState.SuspendedBySupervisor,
                ConsecutiveRestartFailures = 3,
                MaxRestartAttemptsInWindow = 3,
                RestartWindowMinutes = 10
            }
        });

        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            new JsonMcpProfileStore(scope.RootPath),
            runtimeStore,
            new PassthroughMcpProcessController(),
            new NoOpMcpAutomationService());

        var result = await service.ResumeSuspendedManagedProcessesAsync();
        var snapshot = await service.LoadAsync();
        var record = Assert.Single(snapshot.ManagedProcesses);

        Assert.True(result.Success, result.Details);
        Assert.True(record.IsRunning);
        Assert.Equal(McpSupervisorState.Recovering, record.SupervisorState);
        Assert.Equal(1, record.ConsecutiveRestartFailures);
        Assert.Equal(0, snapshot.RuntimeSummary.SuspendedProcessCount);
    }

    [Fact]
    public async Task ValidateCurrentScopeAsync_FlagsGeneratedConfigDrift()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var profileStore = new JsonMcpProfileStore(scope.RootPath);
        await profileStore.SaveManifestAsync(
            ProfileKind.Global,
            """
            {
              "mcpServers": {
                "managed-one": {
                  "command": "cmd",
                  "args": ["/c", "echo managed"],
                  "env": {}
                }
              }
            }
            """);

        var generatedClaudePath = Path.Combine(scope.RootPath, "mcp", "generated", "claude", "global.mcp.json");
        var generatedCodexPath = Path.Combine(scope.RootPath, "mcp", "generated", "codex", "global.config.toml");
        var generatedAntigravityPath = Path.Combine(scope.RootPath, "mcp", "generated", "antigravity", "global.mcp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(generatedClaudePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(generatedCodexPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(generatedAntigravityPath)!);

        await File.WriteAllTextAsync(generatedClaudePath, "{ \"mcpServers\": {} }");
        await File.WriteAllTextAsync(generatedCodexPath, string.Empty);
        await File.WriteAllTextAsync(generatedAntigravityPath, "{ \"mcpServers\": {} }");

        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            _ => new JsonMcpProfileStore(scope.RootPath),
            _ => new JsonMcpRuntimeStore(scope.RootPath),
            new PassthroughMcpProcessController(),
            new NoOpMcpAutomationService(),
            null,
            new McpClientConfigService(userHome));

        var snapshot = await service.ValidateCurrentScopeAsync(WorkspaceScope.Global, ProfileKind.Global, null);

        Assert.Contains(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedClaudePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedCodexPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedAntigravityPath, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class AlwaysFailStartMcpProcessController : IMcpProcessController
    {
        public Task<McpRuntimeRecord> RefreshAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(record);

        public Task<McpProcessCommandResult> StartAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(new McpProcessCommandResult(OperationResult.Fail("start failed", "boom"), record));

        public Task<McpProcessCommandResult> StopAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(new McpProcessCommandResult(OperationResult.Ok("stopped"), record with { IsRunning = false, ProcessId = null }));
    }
}


