using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Application.Services;
using AIHub.Contracts;
using AIHub.Infrastructure;

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
            new McpRuntimeRecord { Name = "running-ok", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = true },
            new McpRuntimeRecord { Name = "keepalive-down", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = false, KeepAlive = true },
            new McpRuntimeRecord { Name = "manual-down", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = false, KeepAlive = false },
            new McpRuntimeRecord { Name = "health-alert", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = true, LastHealthStatus = "error", LastHealthMessage = "health check failed" },
            new McpRuntimeRecord { Name = "disabled", Mode = McpServerMode.ProcessManaged, IsEnabled = false, IsRunning = false }
        });

        var service = CreateService(scope.RootPath, runtimeStore: runtimeStore);
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
    public async Task SaveManifestAsync_Writes_Source_Manifest_And_Refreshes_Runtime()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var result = await service.SaveManifestAsync(
            WorkspaceProfiles.GlobalId,
            """
            {
              "mcpServers": {
                "managed-one": {
                  "command": "cmd",
                  "args": ["/c", "echo managed"]
                }
              }
            }
            """);

        if (!result.Success)
        {
            throw new Xunit.Sdk.XunitException($"SaveManifestAsync failed: {result.Message} | {result.Details}");
        }
        Assert.True(File.Exists(Path.Combine(scope.RootPath, "source", "profiles", "global", "mcp", "manifest.json")));
    }

    [Fact]
    public async Task SaveServerBindingsAsync_Publishes_To_Selected_Profiles_And_Deletes_Draft()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var result = await service.SaveServerBindingsAsync(
            "filesystem",
            """
            {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem", "."]
            }
            """,
            new[] { WorkspaceProfiles.GlobalId, WorkspaceProfiles.FrontendId });

        if (!result.Success)
        {
            throw new Xunit.Sdk.XunitException($"SaveServerBindingsAsync failed: {result.Message} | {result.Details}");
        }

        var snapshot = await service.LoadAsync();
        Assert.Contains(snapshot.Profiles, profile => profile.Profile == WorkspaceProfiles.GlobalId && profile.ServerNames.Contains("filesystem"));
        Assert.Contains(snapshot.Profiles, profile => profile.Profile == WorkspaceProfiles.FrontendId && profile.ServerNames.Contains("filesystem"));
        Assert.False(File.Exists(Path.Combine(scope.RootPath, "source", "library", "mcp-drafts", "filesystem.json")));

    }

    [Fact]
    public async Task SaveServerBindingsAsync_With_No_Targets_Persists_Draft_And_Removes_Published_Server()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);
        await service.SaveServerBindingsAsync(
            "filesystem",
            """
            {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem", "."]
            }
            """,
            new[] { WorkspaceProfiles.BackendId });

        var result = await service.SaveServerBindingsAsync(
            "filesystem",
            """
            {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem", "."]
            }
            """,
            Array.Empty<string>());

        if (!result.Success)
        {
            throw new Xunit.Sdk.XunitException($"SaveServerBindingsAsync(empty) failed: {result.Message} | {result.Details}");
        }
        var draftPath = Path.Combine(scope.RootPath, "source", "library", "mcp-drafts", "filesystem.json");
        Assert.True(File.Exists(draftPath));

        var snapshot = await service.LoadAsync();
        Assert.DoesNotContain(snapshot.Profiles, profile => profile.ServerNames.Contains("filesystem"));

    }

    [Fact]
    public async Task GenerateConfigsAsync_Refreshes_Runtime_From_Source_Manifests()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);
        await service.SaveManifestAsync(
            WorkspaceProfiles.BackendId,
            """
            {
              "mcpServers": {
                "backend-only": {
                  "command": "cmd"
                }
              }
            }
            """);

        var backendRuntimePath = Path.Combine(scope.RootPath, ".runtime", "effective", "backend", "mcp", "claude.mcp.json");
        if (File.Exists(backendRuntimePath))
        {
            File.Delete(backendRuntimePath);
        }

        var result = await service.GenerateConfigsAsync();

        if (!result.Success)
        {
            throw new Xunit.Sdk.XunitException($"GenerateConfigsAsync failed: {result.Message} | {result.Details}");
        }
        Assert.True(File.Exists(backendRuntimePath));
        Assert.Contains("backend-only", await File.ReadAllTextAsync(backendRuntimePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveManifestAsync_Reapplies_Global_Workspace_When_Workspace_Automation_Is_Available()
    {
        using var scope = new TestHubRootScope();
        var automation = new RecordingWorkspaceAutomationService();
        var service = CreateService(scope.RootPath, workspaceAutomationService: automation);

        var result = await service.SaveManifestAsync(
            WorkspaceProfiles.GlobalId,
            """
            {
              "mcpServers": {
                "global-managed": {
                  "command": "cmd"
                }
              }
            }
            """);

        Assert.True(result.Success, result.Details);
        Assert.Equal(1, automation.ApplyGlobalLinksCallCount);
    }

    [Fact]
    public async Task SaveServerBindingsAsync_Reapplies_Onboarded_Project_Profiles()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "project");
        Directory.CreateDirectory(projectPath);

        var settingsStore = new JsonHubSettingsStore(scope.RootPath);
        await settingsStore.SaveAsync(new HubSettingsRecord
        {
            HubRoot = scope.RootPath,
            OnboardedProjectPaths = new[] { projectPath }
        });

        var projectRegistry = new JsonProjectRegistry(scope.RootPath);
        await projectRegistry.SaveAllAsync(new[]
        {
            new ProjectRecord("demo", projectPath, WorkspaceProfiles.BackendId)
        });

        var automation = new RecordingWorkspaceAutomationService();
        var service = CreateService(
            scope.RootPath,
            workspaceAutomationService: automation,
            hubSettingsStoreFactory: _ => settingsStore,
            projectRegistryFactory: _ => projectRegistry);

        var result = await service.SaveServerBindingsAsync(
            "filesystem",
            """
            {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem", "."]
            }
            """,
            new[] { WorkspaceProfiles.BackendId });

        Assert.True(result.Success, result.Details);
        Assert.Equal(1, automation.ApplyGlobalLinksCallCount);
        Assert.Equal(1, automation.ApplyProjectProfileCallCount);
        Assert.Equal(projectPath, automation.LastAppliedProjectPath);
        Assert.Equal(WorkspaceProfiles.BackendId, automation.LastAppliedProjectProfile);
    }

    private static McpControlService CreateService(
        string hubRoot,
        JsonMcpRuntimeStore? runtimeStore = null,
        RecordingWorkspaceAutomationService? workspaceAutomationService = null,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory = null,
        Func<string?, IProjectRegistry>? projectRegistryFactory = null)
    {
        var personalRoot = Path.Combine(hubRoot, "AI-Personal");
        return new McpControlService(
            new FixedHubRootLocator(hubRoot),
            _ => new JsonMcpProfileStore(hubRoot),
            _ => runtimeStore ?? new JsonMcpRuntimeStore(hubRoot),
            new PassthroughMcpProcessController(),
            new NativeMcpAutomationService(() => personalRoot),
            hubSettingsStoreFactory,
            projectRegistryFactory,
            workspaceAutomationService,
            null,
            null,
            null,
            _ => personalRoot);
    }
}
