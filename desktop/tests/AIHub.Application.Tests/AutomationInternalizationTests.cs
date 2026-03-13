using System.Diagnostics;
using AIHub.Application.Services;
using AIHub.Infrastructure;
using AIHub.Contracts;

namespace AIHub.Application.Tests;

public sealed class AutomationInternalizationTests
{
    [Fact]
    public async Task ScriptCenterService_Hides_Internalized_Scripts_From_Default_List()
    {
        using var scope = new TestHubRootScope();
        var scriptsRoot = Path.Combine(scope.RootPath, "scripts");
        Directory.CreateDirectory(Path.Combine(scriptsRoot, "hooks"));
        await File.WriteAllTextAsync(Path.Combine(scriptsRoot, "setup-global.ps1"), "# hidden");
        await File.WriteAllTextAsync(Path.Combine(scriptsRoot, "sync-mcp.ps1"), "# hidden");
        await File.WriteAllTextAsync(Path.Combine(scriptsRoot, "use-profile.ps1"), "# hidden");
        await File.WriteAllTextAsync(Path.Combine(scriptsRoot, "hooks", "pre-tool-check.ps1"), "# visible");

        var service = new ScriptCenterService(new FixedHubRootLocator(scope.RootPath), new NoOpScriptExecutionService());

        var snapshot = await service.LoadAsync();

        Assert.DoesNotContain(snapshot.Scripts, script => string.Equals(script.RelativePath, "setup-global.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Scripts, script => string.Equals(script.RelativePath, "sync-mcp.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.Scripts, script => string.Equals(script.RelativePath, "use-profile.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Scripts, script => string.Equals(script.RelativePath, "hooks/pre-tool-check.ps1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyGlobalLinksAsync_Renders_Template_And_Records_Junctions()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "commands", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "agents", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            "{\"hubRoot\":\"__AI_HUB_ROOT_JSON__\"}");

        var linkService = new RecordingPlatformLinkService();
        var service = new NativeWorkspaceAutomationService(
            linkService,
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyGlobalLinksAsync(scope.RootPath);

        Assert.True(result.Success, result.Details);
        var settingsPath = Path.Combine(userHome, ".claude", "settings.json");
        Assert.True(File.Exists(settingsPath));
        var content = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains(scope.RootPath.Replace("\\", "\\\\", StringComparison.Ordinal), content, StringComparison.Ordinal);
        Assert.Contains(linkService.Junctions, item => item.LinkPath.EndsWith(Path.Combine(".claude", "commands"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(linkService.Junctions, item => item.LinkPath.EndsWith(Path.Combine(".codex", "skills", "ai-hub"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyProjectProfileAsync_Writes_Project_Config_And_Backup()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "project-a");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "commands", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "agents", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "generated", "claude"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "generated", "codex"));
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"), "{\"hubRoot\":\"__AI_HUB_ROOT_JSON__\"}");
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "mcp", "generated", "claude", "global.mcp.json"), "{\"mcpServers\":{\"demo\":{}}}");
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "mcp", "generated", "codex", "global.config.toml"), "[mcp_servers.demo]\ncommand = \"demo\"");
        Directory.CreateDirectory(Path.Combine(projectPath, ".codex"));
        await File.WriteAllTextAsync(Path.Combine(projectPath, ".mcp.json"), "old");

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService());

        var result = await service.ApplyProjectProfileAsync(scope.RootPath, projectPath, ProfileKind.Global);

        Assert.True(result.Success, result.Details);
        Assert.Contains("demo", await File.ReadAllTextAsync(Path.Combine(projectPath, ".mcp.json")), StringComparison.Ordinal);
        Assert.Contains("mcp_servers.demo", await File.ReadAllTextAsync(Path.Combine(projectPath, ".codex", "config.toml")), StringComparison.Ordinal);
        Assert.Contains("hubRoot", await File.ReadAllTextAsync(Path.Combine(projectPath, ".claude", "settings.json")), StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(projectPath, ".mcp.json.bak.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task NativeMcpAutomationService_GenerateConfigsAsync_Writes_All_Client_Outputs()
    {
        using var scope = new TestHubRootScope();
        var manifestRoot = Path.Combine(scope.RootPath, "mcp", "manifest");
        Directory.CreateDirectory(manifestRoot);
        await File.WriteAllTextAsync(
            Path.Combine(manifestRoot, "global.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "node",
                  "args": ["shared.js"],
                  "env": {
                    "SHARED": "1"
                  }
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(manifestRoot, "project-a.json"),
            """
            {
              "mcpServers": {
                "project": {
                  "command": "dotnet",
                  "args": ["run"],
                  "env": {
                    "PROJECT": "1"
                  }
                }
              }
            }
            """);

        var service = new NativeMcpAutomationService();

        var result = await service.GenerateConfigsAsync(scope.RootPath);

        Assert.True(result.Success, result.Details);
        var claudePath = Path.Combine(scope.RootPath, "mcp", "generated", "claude", "project-a.mcp.json");
        var codexPath = Path.Combine(scope.RootPath, "mcp", "generated", "codex", "project-a.config.toml");
        var antigravityPath = Path.Combine(scope.RootPath, "mcp", "generated", "antigravity", "project-a.mcp.json");
        Assert.Contains("shared", await File.ReadAllTextAsync(claudePath), StringComparison.Ordinal);
        Assert.Contains("project", await File.ReadAllTextAsync(claudePath), StringComparison.Ordinal);
        Assert.Contains("mcp_servers", await File.ReadAllTextAsync(codexPath), StringComparison.Ordinal);
        Assert.Contains("shared", await File.ReadAllTextAsync(antigravityPath), StringComparison.Ordinal);
    }
}

