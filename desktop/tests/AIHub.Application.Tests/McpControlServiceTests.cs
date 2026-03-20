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
            new McpRuntimeRecord { Name = "running-ok", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = true },
            new McpRuntimeRecord { Name = "keepalive-down", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = false, KeepAlive = true },
            new McpRuntimeRecord { Name = "manual-down", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = false, KeepAlive = false },
            new McpRuntimeRecord { Name = "health-alert", Mode = McpServerMode.ProcessManaged, IsEnabled = true, IsRunning = true, LastHealthStatus = "异常", LastHealthMessage = "健康检查失败" },
            new McpRuntimeRecord { Name = "disabled", Mode = McpServerMode.ProcessManaged, IsEnabled = false, IsRunning = false }
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
            WorkspaceProfiles.GlobalId,
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
            new McpClientConfigService(userHome),
            new LayeredMcpEffectiveConfigReader(() => userHome));

        var snapshot = await service.ValidateCurrentScopeAsync(WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null);

        Assert.Contains(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedClaudePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedCodexPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedAntigravityPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateCurrentScopeAsync_Uses_Layered_Effective_Servers_For_Generated_Config_Drift()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var personalRoot = Path.Combine(userHome, "AI-Personal");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));
        Directory.CreateDirectory(Path.Combine(personalRoot, "mcp", "manifest"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "cmd",
                  "args": ["/c", "echo hub"],
                  "env": {
                    "LAYER": "hub"
                  }
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(personalRoot, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "cmd",
                  "args": ["/c", "echo private"],
                  "env": {
                    "LAYER": "private"
                  }
                }
              }
            }
            """);

        LayeredWorkspaceMaterializer.GenerateLegacyMcpOutputs(scope.RootPath, personalRoot, [WorkspaceProfiles.GlobalId]);

        var generatedClaudePath = Path.Combine(scope.RootPath, "mcp", "generated", "claude", "global.mcp.json");
        var generatedCodexPath = Path.Combine(scope.RootPath, "mcp", "generated", "codex", "global.config.toml");
        var generatedAntigravityPath = Path.Combine(scope.RootPath, "mcp", "generated", "antigravity", "global.mcp.json");

        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            _ => new JsonMcpProfileStore(scope.RootPath),
            _ => new JsonMcpRuntimeStore(scope.RootPath),
            new PassthroughMcpProcessController(),
            new NoOpMcpAutomationService(),
            null,
            new McpClientConfigService(userHome));

        var snapshot = await service.ValidateCurrentScopeAsync(WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null);

        Assert.DoesNotContain(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedClaudePath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedCodexPath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedAntigravityPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SyncCurrentScopeClientsAsync_Preserves_Private_Layer_Overrides()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var personalRoot = Path.Combine(userHome, "AI-Personal");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));
        Directory.CreateDirectory(Path.Combine(personalRoot, "mcp", "manifest"));
        Directory.CreateDirectory(Path.Combine(userHome, ".codex"));
        Directory.CreateDirectory(Path.Combine(userHome, ".gemini", "antigravity"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "cmd",
                  "args": ["/c", "echo hub"],
                  "env": {
                    "LAYER": "hub"
                  }
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(personalRoot, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "cmd",
                  "args": ["/c", "echo private"],
                  "env": {
                    "LAYER": "private"
                  }
                }
              }
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(userHome, ".claude.json"), "{ \"mcpServers\": {} }");
        await File.WriteAllTextAsync(Path.Combine(userHome, ".codex", "config.toml"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(userHome, ".gemini", "antigravity", "mcp_config.json"), "{ \"mcpServers\": {} }");

        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            _ => new JsonMcpProfileStore(scope.RootPath),
            _ => new JsonMcpRuntimeStore(scope.RootPath),
            new PassthroughMcpProcessController(),
            new NativeMcpAutomationService(() => userHome),
            null,
            new McpClientConfigService(userHome),
            new LayeredMcpEffectiveConfigReader(() => userHome));

        var result = await service.SyncCurrentScopeClientsAsync(WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null);

        Assert.True(result.Success, result.Details);
        Assert.Contains("private", await File.ReadAllTextAsync(Path.Combine(userHome, ".claude.json")), StringComparison.Ordinal);
        Assert.Contains("private", await File.ReadAllTextAsync(Path.Combine(userHome, ".codex", "config.toml")), StringComparison.Ordinal);
        Assert.Contains("private", await File.ReadAllTextAsync(Path.Combine(userHome, ".gemini", "antigravity", "mcp_config.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateCurrentScopeAsync_Treats_Coplay_Alias_As_Up_To_Date()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var personalRoot = Path.Combine(userHome, "AI-Personal");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));
        Directory.CreateDirectory(Path.Combine(personalRoot, "mcp", "manifest"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "coplay-mcp": {
                  "command": "uvx",
                  "args": ["--python", ">=3.11", "coplay-mcp-server@latest"],
                  "env": {
                    "MCP_TOOL_TIMEOUT": "720000"
                  }
                }
              }
            }
            """);

        LayeredWorkspaceMaterializer.GenerateLegacyMcpOutputs(scope.RootPath, personalRoot, [WorkspaceProfiles.GlobalId]);

        var generatedCodexPath = Path.Combine(scope.RootPath, "mcp", "generated", "codex", "global.config.toml");
        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            _ => new JsonMcpProfileStore(scope.RootPath),
            _ => new JsonMcpRuntimeStore(scope.RootPath),
            new PassthroughMcpProcessController(),
            new NoOpMcpAutomationService(),
            null,
            new McpClientConfigService(userHome),
            new LayeredMcpEffectiveConfigReader(() => userHome));

        var snapshot = await service.ValidateCurrentScopeAsync(WorkspaceScope.Global, WorkspaceProfiles.GlobalId, null);

        Assert.DoesNotContain(snapshot.Issues, issue => string.Equals(issue.FilePath, generatedCodexPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadAsync_Includes_Custom_Profile_Manifests_And_Generated_Client_Paths()
    {
        using var scope = new TestHubRootScope();

        var profileService = new WorkspaceProfileService(
            new FixedHubRootLocator(scope.RootPath),
            root => new JsonWorkspaceProfileCatalogStore(root),
            root => new JsonProjectRegistry(root),
            root => new JsonHubSettingsStore(root),
            root => new JsonMcpProfileStore(root));

        var saveResult = await profileService.SaveAsync(
            null,
            new WorkspaceProfileRecord
            {
                Id = "Data Ops",
                DisplayName = "Data Ops"
            });
        Assert.True(saveResult.Success, saveResult.Details);

        var profileStore = new JsonMcpProfileStore(scope.RootPath);
        await profileStore.SaveManifestAsync(
            "Data Ops",
            """
            {
              "mcpServers": {
                "data-ops-server": {
                  "command": "demo"
                }
              }
            }
            """);

        var runtimeStore = new JsonMcpRuntimeStore(scope.RootPath);
        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            profileStore,
            runtimeStore,
            new PassthroughMcpProcessController(),
            new NoOpMcpAutomationService());

        var snapshot = await service.LoadAsync();
        var profile = Assert.Single(snapshot.Profiles, item => item.Profile == "data-ops");

        Assert.Equal("Data Ops", profile.ProfileDisplayName);
        Assert.EndsWith(Path.Combine("mcp", "manifest", "data-ops.json"), profile.ManifestPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "data-ops-server" }, profile.ServerNames.ToArray());
        Assert.Contains(profile.GeneratedClients, client => client.FilePath.EndsWith(Path.Combine("mcp", "generated", "claude", "data-ops.mcp.json"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.GeneratedClients, client => client.FilePath.EndsWith(Path.Combine("mcp", "generated", "codex", "data-ops.config.toml"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profile.GeneratedClients, client => client.FilePath.EndsWith(Path.Combine("mcp", "generated", "antigravity", "data-ops.mcp.json"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveServerBindingsAsync_Fans_Out_Server_Across_Selected_Profiles()
    {
        using var scope = new TestHubRootScope();
        var service = new McpControlService(
            new FixedHubRootLocator(scope.RootPath),
            new JsonMcpProfileStore(scope.RootPath),
            new JsonMcpRuntimeStore(scope.RootPath),
            new PassthroughMcpProcessController(),
            new NoOpMcpAutomationService());

        var firstResult = await service.SaveServerBindingsAsync(
            "filesystem",
            """
            {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem", "."]
            }
            """,
            new[] { WorkspaceProfiles.GlobalId, WorkspaceProfiles.FrontendId });

        Assert.True(firstResult.Success, firstResult.Details);

        var snapshot = await service.LoadAsync();
        Assert.Contains(snapshot.Profiles, profile => profile.Profile == WorkspaceProfiles.GlobalId && profile.ServerNames.Contains("filesystem"));
        Assert.Contains(snapshot.Profiles, profile => profile.Profile == WorkspaceProfiles.FrontendId && profile.ServerNames.Contains("filesystem"));

        var secondResult = await service.SaveServerBindingsAsync(
            "filesystem",
            """
            {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem", "."]
            }
            """,
            new[] { WorkspaceProfiles.BackendId });

        Assert.True(secondResult.Success, secondResult.Details);

        snapshot = await service.LoadAsync();
        Assert.DoesNotContain(snapshot.Profiles, profile => profile.Profile == WorkspaceProfiles.GlobalId && profile.ServerNames.Contains("filesystem"));
        Assert.DoesNotContain(snapshot.Profiles, profile => profile.Profile == WorkspaceProfiles.FrontendId && profile.ServerNames.Contains("filesystem"));
        Assert.Contains(snapshot.Profiles, profile => profile.Profile == WorkspaceProfiles.BackendId && profile.ServerNames.Contains("filesystem"));
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
