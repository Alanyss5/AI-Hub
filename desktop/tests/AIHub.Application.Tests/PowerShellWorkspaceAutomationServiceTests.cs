using AIHub.Infrastructure;

namespace AIHub.Application.Tests;

public sealed class PowerShellWorkspaceAutomationServiceTests
{
    [Fact]
    public async Task ApplyGlobalLinksAsync_Materializes_Global_Effective_Output_Before_Invoking_Script()
    {
        using var hubScope = new TestHubRootScope();
        using var userHomeScope = new TestHubRootScope();
        Directory.CreateDirectory(Path.Combine(hubScope.RootPath, "mcp", "manifest"));
        await File.WriteAllTextAsync(
            Path.Combine(hubScope.RootPath, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "demo-server": {
                  "command": "demo"
                }
              }
            }
            """);

        var expectedEffectiveRoot = Path.Combine(hubScope.RootPath, ".runtime", "effective", WorkspaceProfiles.GlobalId);
        var scriptExecutionService = new RecordingScriptExecutionService
        {
            OnRun = (_, _) =>
            {
                Assert.True(Directory.Exists(expectedEffectiveRoot));
                Assert.True(Directory.Exists(Path.Combine(expectedEffectiveRoot, "skills")));
                Assert.True(Directory.Exists(Path.Combine(expectedEffectiveRoot, "claude", "commands")));
                Assert.True(Directory.Exists(Path.Combine(expectedEffectiveRoot, "claude", "agents")));
                Assert.True(File.Exists(Path.Combine(expectedEffectiveRoot, "claude", "settings.json")));
                Assert.True(File.Exists(Path.Combine(expectedEffectiveRoot, "mcp", "claude.mcp.json")));
                Assert.True(File.Exists(Path.Combine(expectedEffectiveRoot, "mcp", "codex.config.toml")));
                Assert.True(File.Exists(Path.Combine(expectedEffectiveRoot, "mcp", "antigravity.mcp.json")));
            }
        };
        var service = new PowerShellWorkspaceAutomationService(scriptExecutionService, () => userHomeScope.RootPath);

        var result = await service.ApplyGlobalLinksAsync(hubScope.RootPath);

        Assert.True(result.Success, result.Details);
        var call = Assert.Single(scriptExecutionService.Calls);
        Assert.Equal(Path.Combine(hubScope.RootPath, "scripts", "setup-global.ps1"), call.ScriptPath);
        Assert.Equal(["-HubRoot", hubScope.RootPath, "-UserHome", userHomeScope.RootPath], call.Arguments);
        Assert.True(Directory.Exists(expectedEffectiveRoot));
    }

    [Fact]
    public async Task ApplyProjectProfileAsync_Materializes_Normalized_Profile_Effective_Output_Before_Invoking_Script()
    {
        using var hubScope = new TestHubRootScope();
        using var userHomeScope = new TestHubRootScope();
        var projectPath = Path.Combine(hubScope.RootPath, "projects", "demo");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(hubScope.RootPath, "mcp", "manifest"));
        await File.WriteAllTextAsync(
            Path.Combine(hubScope.RootPath, "mcp", "manifest", "data-ops.json"),
            """
            {
              "mcpServers": {
                "data-ops-server": {
                  "command": "demo"
                }
              }
            }
            """);

        var expectedEffectiveRoot = Path.Combine(hubScope.RootPath, ".runtime", "effective", "data-ops");
        var scriptExecutionService = new RecordingScriptExecutionService
        {
            OnRun = (_, _) =>
            {
                Assert.True(Directory.Exists(expectedEffectiveRoot));
                Assert.True(Directory.Exists(Path.Combine(expectedEffectiveRoot, "skills")));
                Assert.True(Directory.Exists(Path.Combine(expectedEffectiveRoot, "claude", "commands")));
                Assert.True(Directory.Exists(Path.Combine(expectedEffectiveRoot, "claude", "agents")));
                Assert.True(File.Exists(Path.Combine(expectedEffectiveRoot, "claude", "settings.json")));
                Assert.True(File.Exists(Path.Combine(expectedEffectiveRoot, "mcp", "claude.mcp.json")));
                Assert.True(File.Exists(Path.Combine(expectedEffectiveRoot, "mcp", "codex.config.toml")));
            }
        };
        var service = new PowerShellWorkspaceAutomationService(scriptExecutionService, () => userHomeScope.RootPath);

        var result = await service.ApplyProjectProfileAsync(hubScope.RootPath, projectPath, "Data Ops");

        Assert.True(result.Success, result.Details);
        var call = Assert.Single(scriptExecutionService.Calls);
        Assert.Equal(Path.Combine(hubScope.RootPath, "scripts", "use-profile.ps1"), call.ScriptPath);
        Assert.Equal(["-HubRoot", hubScope.RootPath, "-ProjectPath", projectPath, "-Profile", "data-ops"], call.Arguments);
        Assert.True(Directory.Exists(expectedEffectiveRoot));
    }
}
