using System.Diagnostics;
using AIHub.Application.Services;
using System.Text.Json.Nodes;
using AIHub.Infrastructure;
using AIHub.Contracts;
using AIHub.Platform.Windows;

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
    public void Setup_Global_Script_Uses_Runtime_Effective_Skills_Entrypoints()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "setup-global.ps1")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var scriptPath = Path.Combine(directory!.FullName, "scripts", "setup-global.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("Join-Path $effectiveRoot 'skills'", script, StringComparison.Ordinal);
        Assert.Contains("Ensure-SkillsOverlay (Join-Path $normalizedUserHome '.claude\\skills') $effectiveSkills $effectiveSkills", script, StringComparison.Ordinal);
        Assert.DoesNotContain("skills\\global", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Setup_Global_Script_Does_Not_Retain_Legacy_Codex_Skills_Skip()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "setup-global.ps1")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var scriptPath = Path.Combine(directory!.FullName, "scripts", "setup-global.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("SkipLegacyCodexPath", script, StringComparison.Ordinal);
        Assert.Contains("Ensure-Junction (Join-Path $normalizedUserHome '.codex\\skills\\ai-hub') $effectiveSkills -IgnoreIfLocked", script, StringComparison.Ordinal);
        Assert.Contains("Ensure-Junction (Join-Path $normalizedUserHome '.codex\\skills\\personal') $effectiveSkills -IgnoreIfLocked", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Use_Profile_Script_Consumes_Runtime_Effective_Output_For_All_Entrypoints()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "use-profile.ps1")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var scriptPath = Path.Combine(directory!.FullName, "scripts", "use-profile.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains(".runtime\\effective\\$normalizedProfile", script, StringComparison.Ordinal);
        Assert.Contains("Ensure-Junction (Join-Path $normalizedProjectPath '.claude\\skills') (Join-Path $effectiveRoot 'skills')", script, StringComparison.Ordinal);
        Assert.Contains("Ensure-TextCopy (Join-Path $effectiveRoot 'claude\\settings.json') (Join-Path $normalizedProjectPath '.claude\\settings.json')", script, StringComparison.Ordinal);
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
        Assert.Contains(linkService.Junctions, item =>
            item.LinkPath.EndsWith(Path.Combine(".claude", "skills", "company"), StringComparison.OrdinalIgnoreCase)
            && item.TargetPath.EndsWith(Path.Combine(".runtime", "effective", WorkspaceProfiles.GlobalId, "skills"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(linkService.Junctions, item =>
            item.LinkPath.EndsWith(Path.Combine(".claude", "skills", "personal"), StringComparison.OrdinalIgnoreCase)
            && item.TargetPath.EndsWith(Path.Combine(".runtime", "effective", WorkspaceProfiles.GlobalId, "skills"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(linkService.Junctions, item => item.LinkPath.EndsWith(Path.Combine(".agents", "agents"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(linkService.Junctions, item => item.LinkPath.EndsWith(Path.Combine(".claude", "commands"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(linkService.Junctions, item => item.LinkPath.EndsWith(Path.Combine(".codex", "skills", "ai-hub"), StringComparison.OrdinalIgnoreCase));
        var bootstrapContent = await File.ReadAllTextAsync(Path.Combine(userHome, ".agents", "AGENTS.md"));
        Assert.Contains("# AI-Hub AGENTS Bootstrap", bootstrapContent, StringComparison.Ordinal);
        Assert.Contains("ProfileId: global", bootstrapContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyGlobalLinksAsync_Rebuilds_Stale_Global_Entrypoints_With_Junctions()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var staleRoot = Path.Combine(scope.RootPath, "stale-worktree");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills", "global"));
        Directory.CreateDirectory(Path.Combine(staleRoot, "skills", "global"));
        Directory.CreateDirectory(Path.Combine(staleRoot, "claude", "commands"));
        Directory.CreateDirectory(Path.Combine(staleRoot, "claude", "agents"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "commands", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "agents", "global"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            "{\"hubRoot\":\"__AI_HUB_ROOT_JSON__\"}");

        var linkService = new WindowsPlatformLinkService();
        linkService.EnsureDirectory(Path.Combine(userHome, ".claude"));
        linkService.EnsureDirectory(Path.Combine(userHome, ".claude", "skills"));
        linkService.EnsureJunction(Path.Combine(userHome, ".claude", "skills", "company"), Path.Combine(staleRoot, "skills", "global"));
        linkService.EnsureJunction(Path.Combine(userHome, ".claude", "commands"), Path.Combine(staleRoot, "claude", "commands"));
        linkService.EnsureJunction(Path.Combine(userHome, ".claude", "agents"), Path.Combine(staleRoot, "claude", "agents"));

        var service = new NativeWorkspaceAutomationService(
            linkService,
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyGlobalLinksAsync(scope.RootPath);

        Assert.True(result.Success, result.Details);
        Assert.Contains("全局链接已应用", result.Message, StringComparison.Ordinal);

        var companyLink = new DirectoryInfo(Path.Combine(userHome, ".claude", "skills", "company"));
        var commandsLink = new DirectoryInfo(Path.Combine(userHome, ".claude", "commands"));
        var agentsLink = new DirectoryInfo(Path.Combine(userHome, ".claude", "agents"));

        Assert.Equal(
            NormalizePath(Path.Combine(scope.RootPath, ".runtime", "effective", WorkspaceProfiles.GlobalId, "skills")),
            NormalizePath(companyLink.ResolveLinkTarget(false)!.FullName));
        Assert.Equal(
            NormalizePath(Path.Combine(scope.RootPath, ".runtime", "effective", WorkspaceProfiles.GlobalId, "claude", "commands")),
            NormalizePath(commandsLink.ResolveLinkTarget(false)!.FullName));
        Assert.Equal(
            NormalizePath(Path.Combine(scope.RootPath, ".runtime", "effective", WorkspaceProfiles.GlobalId, "claude", "agents")),
            NormalizePath(agentsLink.ResolveLinkTarget(false)!.FullName));

        Assert.Single(Directory.GetDirectories(Path.Combine(userHome, ".claude", "skills"), "company.bak.*"));
        Assert.Single(Directory.GetDirectories(Path.Combine(userHome, ".claude"), "commands.bak.*"));
        Assert.Single(Directory.GetDirectories(Path.Combine(userHome, ".claude"), "agents.bak.*"));
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyGlobalLinksAsync_Returns_Actionable_Link_Error_Details()
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

        var service = new NativeWorkspaceAutomationService(
            new ThrowingPlatformLinkService(new UnauthorizedAccessException("客户端没有所需的特权。")),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyGlobalLinksAsync(scope.RootPath);

        Assert.False(result.Success);
        Assert.Contains("全局工作区接管失败", result.Message, StringComparison.Ordinal);
        Assert.Contains("客户端没有所需的特权", result.Details, StringComparison.Ordinal);
        Assert.Contains("用户目录：", result.Details, StringComparison.Ordinal);
        Assert.Contains("全局有效输出：", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyGlobalLinksAsync_Points_Personal_Skill_Entrypoints_To_Private_Layer()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var personalRoot = Path.Combine(userHome, "AI-Personal");
        await WriteSkillAsync(Path.Combine(personalRoot, "skills", "global", "private-skill"), "private");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"), "{}");

        var linkService = new RecordingPlatformLinkService();
        var service = new NativeWorkspaceAutomationService(
            linkService,
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyGlobalLinksAsync(scope.RootPath);

        Assert.True(result.Success, result.Details);
        Assert.Contains(linkService.Junctions, item =>
            item.LinkPath.EndsWith(Path.Combine(".claude", "skills", "personal"), StringComparison.OrdinalIgnoreCase)
            && item.TargetPath.EndsWith(Path.Combine(".runtime", "effective", WorkspaceProfiles.GlobalId, "skills"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(linkService.Junctions, item =>
            item.LinkPath.EndsWith(Path.Combine(".codex", "skills", "personal"), StringComparison.OrdinalIgnoreCase)
            && item.TargetPath.EndsWith(Path.Combine(".runtime", "effective", WorkspaceProfiles.GlobalId, "skills"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_PreviewGlobalOnboardingAsync_Detects_Unmanaged_Global_Resources()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            "{\"hubRoot\":\"__AI_HUB_ROOT_JSON__\"}");

        var skillPath = Path.Combine(userHome, ".claude", "skills", "demo-skill");
        Directory.CreateDirectory(skillPath);
        await File.WriteAllTextAsync(Path.Combine(skillPath, "SKILL.md"), "# demo");
        Directory.CreateDirectory(Path.Combine(userHome, ".claude", "commands"));
        Directory.CreateDirectory(Path.Combine(userHome, ".claude", "agents"));
        await File.WriteAllTextAsync(Path.Combine(userHome, ".claude", "commands", "review.md"), "review");
        await File.WriteAllTextAsync(Path.Combine(userHome, ".claude", "agents", "helper.md"), "helper");
        await File.WriteAllTextAsync(Path.Combine(userHome, ".claude", "settings.json"), "{\"theme\":\"legacy\"}");
        await File.WriteAllTextAsync(
            Path.Combine(userHome, ".claude.json"),
            """
            {
              "mcpServers": {
                "external-one": {
                  "command": "cmd",
                  "args": ["/c", "echo external"],
                  "env": {
                    "SOURCE": "claude"
                  }
                }
              }
            }
            """);

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var previewResult = await service.PreviewGlobalOnboardingAsync(scope.RootPath);

        Assert.True(previewResult.Success, previewResult.Details);
        var preview = Assert.IsType<WorkspaceOnboardingPreview>(previewResult.Preview);
        Assert.Contains(preview.Candidates, item => item.ResourceKind == WorkspaceOnboardingResourceKind.Skill);
        Assert.Contains(preview.Candidates, item => item.ResourceKind == WorkspaceOnboardingResourceKind.ClaudeCommand);
        Assert.Contains(preview.Candidates, item => item.ResourceKind == WorkspaceOnboardingResourceKind.ClaudeAgent);
        Assert.Contains(preview.Candidates, item => item.ResourceKind == WorkspaceOnboardingResourceKind.ClaudeSettings);
        Assert.Contains(preview.Candidates, item => item.ResourceKind == WorkspaceOnboardingResourceKind.McpServer);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_PreviewGlobalOnboardingAsync_Defaults_Conflicting_Mcp_To_Ignore()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(userHome, ".codex"));
        await File.WriteAllTextAsync(
            Path.Combine(userHome, ".claude.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "node",
                  "args": ["claude.js"],
                  "env": {}
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(userHome, ".codex", "config.toml"),
            """
            [mcp_servers.shared]
            command = "node"
            args = ["codex.js"]
            """);

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var previewResult = await service.PreviewGlobalOnboardingAsync(scope.RootPath);

        Assert.True(previewResult.Success, previewResult.Details);
        var preview = Assert.IsType<WorkspaceOnboardingPreview>(previewResult.Preview);
        var candidate = Assert.Single(preview.Candidates.Where(item => item.ResourceKind == WorkspaceOnboardingResourceKind.McpServer));
        Assert.Equal(WorkspaceImportTargetKind.Ignore, candidate.SuggestedTarget);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyGlobalLinksAsync_Rejects_Conflicting_Mcp_Import()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(userHome, ".codex"));
        await File.WriteAllTextAsync(
            Path.Combine(userHome, ".claude.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "node",
                  "args": ["claude.js"],
                  "env": {}
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(userHome, ".codex", "config.toml"),
            """
            [mcp_servers.shared]
            command = "node"
            args = ["codex.js"]
            """);

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var previewResult = await service.PreviewGlobalOnboardingAsync(scope.RootPath);
        var preview = Assert.IsType<WorkspaceOnboardingPreview>(previewResult.Preview);
        var candidate = Assert.Single(preview.Candidates.Where(item => item.ResourceKind == WorkspaceOnboardingResourceKind.McpServer));

        var result = await service.ApplyGlobalLinksAsync(
            scope.RootPath,
            new[]
            {
                new WorkspaceImportDecisionRecord(candidate.Id, WorkspaceImportTargetKind.AIHub)
            });

        Assert.False(result.Success);
        Assert.Contains("不一致", result.Message + Environment.NewLine + result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_PreviewGlobalOnboardingAsync_Suggests_Private_Target_For_Personal_Sources()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var personalSkillPath = Path.Combine(userHome, ".codex", "skills", "personal", "legacy-private");
        Directory.CreateDirectory(personalSkillPath);
        await File.WriteAllTextAsync(Path.Combine(personalSkillPath, "SKILL.md"), "# private");

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var previewResult = await service.PreviewGlobalOnboardingAsync(scope.RootPath);

        Assert.True(previewResult.Success, previewResult.Details);
        var preview = Assert.IsType<WorkspaceOnboardingPreview>(previewResult.Preview);
        var candidate = Assert.Single(preview.Candidates.Where(item => item.DisplayName == "legacy-private"));
        Assert.Equal(WorkspaceImportTargetKind.Private, candidate.SuggestedTarget);
        Assert.EndsWith(Path.Combine("source", "profiles", "global", "skills", "imported", "codex", "legacy-private"), candidate.PrivateDestinationPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "personal" + Path.DirectorySeparatorChar, candidate.PrivateDestinationPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_PreviewGlobalOnboardingAsync_Preserves_Nested_Skill_Paths()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var teamASkill = Path.Combine(userHome, ".claude", "skills", "team-a", "shared-skill");
        var teamBSkill = Path.Combine(userHome, ".claude", "skills", "team-b", "shared-skill");
        Directory.CreateDirectory(teamASkill);
        Directory.CreateDirectory(teamBSkill);
        await File.WriteAllTextAsync(Path.Combine(teamASkill, "SKILL.md"), "# team a");
        await File.WriteAllTextAsync(Path.Combine(teamBSkill, "SKILL.md"), "# team b");

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var previewResult = await service.PreviewGlobalOnboardingAsync(scope.RootPath);

        Assert.True(previewResult.Success, previewResult.Details);
        var preview = Assert.IsType<WorkspaceOnboardingPreview>(previewResult.Preview);
        var teamACandidate = Assert.Single(preview.Candidates.Where(item => item.SourcePath.EndsWith(Path.Combine("team-a", "shared-skill"), StringComparison.OrdinalIgnoreCase)));
        var teamBCandidate = Assert.Single(preview.Candidates.Where(item => item.SourcePath.EndsWith(Path.Combine("team-b", "shared-skill"), StringComparison.OrdinalIgnoreCase)));
        Assert.EndsWith(Path.Combine("source", "profiles", "global", "skills", "imported", "claude", "team-a", "shared-skill"), teamACandidate.CompanyDestinationPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("source", "profiles", "global", "skills", "imported", "claude", "team-b", "shared-skill"), teamBCandidate.CompanyDestinationPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_PreviewGlobalOnboardingAsync_Rebuilds_Effective_Settings_Before_Comparing()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, ".runtime", "effective", "global", "claude"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            """
            {
              "theme": "new-effective"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, ".runtime", "effective", "global", "claude", "settings.json"),
            """
            {
              "theme": "stale-effective"
            }
            """);

        Directory.CreateDirectory(Path.Combine(userHome, ".claude"));
        await File.WriteAllTextAsync(
            Path.Combine(userHome, ".claude", "settings.json"),
            """
            {
              "theme": "stale-effective"
            }
            """);

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var previewResult = await service.PreviewGlobalOnboardingAsync(scope.RootPath);

        Assert.True(previewResult.Success, previewResult.Details);
        var preview = Assert.IsType<WorkspaceOnboardingPreview>(previewResult.Preview);
        var settingsCandidate = Assert.Single(preview.Candidates.Where(item => item.ResourceKind == WorkspaceOnboardingResourceKind.ClaudeSettings));
        Assert.Contains("\"theme\": \"stale-effective\"", settingsCandidate.SourceDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyGlobalLinksAsync_Does_Not_Rebuild_Unrelated_Broken_Profile()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            """
            {
              "theme": "global-ok"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "frontend.settings.json"),
            "{ invalid json");

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyGlobalLinksAsync(scope.RootPath);

        Assert.True(result.Success, result.Details);
        Assert.Contains("global-ok", await File.ReadAllTextAsync(Path.Combine(userHome, ".claude", "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyGlobalLinksAsync_Refreshes_Project_Profile_Effective_Output()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "claude"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            """
            {
              "theme": "global-updated"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "frontend.settings.json"),
            """
            {
              "profile": "frontend"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "claude", "settings.json"),
            """
            {
              "theme": "stale"
            }
            """);

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyGlobalLinksAsync(scope.RootPath);

        Assert.True(result.Success, result.Details);
        var frontendEffectiveSettings = await File.ReadAllTextAsync(Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "claude", "settings.json"));
        Assert.Contains("global-updated", frontendEffectiveSettings, StringComparison.Ordinal);
        Assert.Contains("frontend", frontendEffectiveSettings, StringComparison.Ordinal);
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
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "agents", "frontend"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "claude", "agents", "global", "AGENTS.md"), "# company bootstrap");
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "agents", "frontend", "AGENTS.md"),
            """
            # AI-Hub AGENTS Bootstrap
            ProfileId: frontend
            """);
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"), "{\"hubRoot\":\"__AI_HUB_ROOT_JSON__\"}");
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "demo": {
                  "command": "demo",
                  "args": [],
                  "env": {}
                }
              }
            }
            """);
        Directory.CreateDirectory(Path.Combine(projectPath, ".codex"));
        await File.WriteAllTextAsync(Path.Combine(projectPath, ".mcp.json"), "old");

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService());

        var result = await service.ApplyProjectProfileAsync(scope.RootPath, projectPath, WorkspaceProfiles.GlobalId);

        Assert.True(result.Success, result.Details);
        Assert.Contains("demo", await File.ReadAllTextAsync(Path.Combine(projectPath, ".mcp.json")), StringComparison.Ordinal);
        Assert.Contains("mcp_servers.demo", await File.ReadAllTextAsync(Path.Combine(projectPath, ".codex", "config.toml")), StringComparison.Ordinal);
        Assert.Contains("hubRoot", await File.ReadAllTextAsync(Path.Combine(projectPath, ".claude", "settings.json")), StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(projectPath, ".mcp.json.bak.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyProjectProfileAsync_Materializes_Four_Layer_Effective_Output()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
        var personalRoot = Path.Combine(userHome, "AI-Personal");
        var projectPath = Path.Combine(scope.RootPath, "project-a");
        Directory.CreateDirectory(projectPath);

        await WriteSkillAsync(Path.Combine(scope.RootPath, "skills", "global", "shared-company"), "company-global");
        await WriteSkillAsync(Path.Combine(personalRoot, "skills", "global", "shared-private"), "private-global");
        await WriteSkillAsync(Path.Combine(scope.RootPath, "skills", "frontend", "frontend-company"), "company-frontend");
        await WriteSkillAsync(Path.Combine(personalRoot, "skills", "frontend", "frontend-private"), "private-frontend");
        await WriteSkillAsync(Path.Combine(scope.RootPath, "skills", "global", "override-me"), "company-global");
        await WriteSkillAsync(Path.Combine(personalRoot, "skills", "frontend", "override-me"), "private-frontend-wins");

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "commands", "global"));
        Directory.CreateDirectory(Path.Combine(personalRoot, "claude", "commands", "frontend"));
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "claude", "commands", "global", "shared.md"), "company-global-command");
        await File.WriteAllTextAsync(Path.Combine(personalRoot, "claude", "commands", "frontend", "shared.md"), "private-frontend-command");

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "agents", "global"));
        Directory.CreateDirectory(Path.Combine(personalRoot, "claude", "agents", "frontend"));
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, "claude", "agents", "global", "shared.md"), "company-global-agent");
        await File.WriteAllTextAsync(Path.Combine(personalRoot, "claude", "agents", "frontend", "shared.md"), "private-frontend-agent");

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        Directory.CreateDirectory(Path.Combine(personalRoot, "claude", "settings"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            """
            {
              "theme": "company-global",
              "nested": {
                "source": "company-global"
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(personalRoot, "claude", "settings", "global.settings.json"),
            """
            {
              "nested": {
                "source": "private-global"
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "frontend.settings.json"),
            """
            {
              "project": "company-frontend"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(personalRoot, "claude", "settings", "frontend.settings.json"),
            """
            {
              "project": "private-frontend"
            }
            """);

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));
        Directory.CreateDirectory(Path.Combine(personalRoot, "mcp", "manifest"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "global.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "cmd",
                  "args": ["/c", "echo company-global"],
                  "env": {
                    "LAYER": "company-global"
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
                  "args": ["/c", "echo private-global"],
                  "env": {
                    "LAYER": "private-global"
                  }
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "frontend.json"),
            """
            {
              "mcpServers": {
                "profile-only": {
                  "command": "cmd",
                  "args": ["/c", "echo company-frontend"],
                  "env": {
                    "LAYER": "company-frontend"
                  }
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(personalRoot, "mcp", "manifest", "frontend.json"),
            """
            {
              "mcpServers": {
                "shared": {
                  "command": "cmd",
                  "args": ["/c", "echo private-frontend"],
                  "env": {
                    "LAYER": "private-frontend"
                  }
                }
              }
            }
            """);

        var linkService = new RecordingPlatformLinkService();
        var service = new NativeWorkspaceAutomationService(
            linkService,
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyProjectProfileAsync(scope.RootPath, projectPath, WorkspaceProfiles.FrontendId);

        Assert.True(result.Success, result.Details);
        Assert.Contains("项目目录：" + projectPath, result.Details, StringComparison.Ordinal);
        Assert.Contains("Profile：前端", result.Details, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(".runtime", "effective", "frontend"), result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".claude\\skills -> " + Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills"), result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".agents\\skills -> " + Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills"), result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".agent\\skills -> " + Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills"), result.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(linkService.Junctions, item =>
            item.LinkPath.EndsWith(Path.Combine(projectPath, ".agents", "agents"), StringComparison.OrdinalIgnoreCase)
            && item.TargetPath.EndsWith(Path.Combine(".runtime", "effective", "frontend", "claude", "agents"), StringComparison.OrdinalIgnoreCase));
        var bootstrapContent = await File.ReadAllTextAsync(Path.Combine(projectPath, ".agents", "AGENTS.md"));
        Assert.Contains("# AI-Hub AGENTS Bootstrap", bootstrapContent, StringComparison.Ordinal);
        Assert.Contains("ProfileId: frontend", bootstrapContent, StringComparison.Ordinal);
        var effectiveSkillTarget = Assert.Single(linkService.Junctions.Where(item =>
            item.LinkPath.EndsWith(Path.Combine(projectPath, ".claude", "skills"), StringComparison.OrdinalIgnoreCase))).TargetPath;
        Assert.EndsWith(Path.Combine(".runtime", "effective", "frontend", "skills"), effectiveSkillTarget, StringComparison.OrdinalIgnoreCase);

        var effectiveSkillContent = await File.ReadAllTextAsync(Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills", "override-me", "SKILL.md"));
        Assert.Contains("private-frontend-wins", effectiveSkillContent, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills", "shared-company", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills", "shared-private", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills", "frontend-company", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(scope.RootPath, ".runtime", "effective", "frontend", "skills", "frontend-private", "SKILL.md")));

        var settingsRoot = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(projectPath, ".claude", "settings.json")))!.AsObject();
        Assert.Equal("company-global", settingsRoot["theme"]!.GetValue<string>());
        Assert.Equal("private-global", settingsRoot["nested"]!["source"]!.GetValue<string>());
        Assert.Equal("private-frontend", settingsRoot["project"]!.GetValue<string>());

        var mcpRoot = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(projectPath, ".mcp.json")))!.AsObject();
        var mcpServers = mcpRoot["mcpServers"]!.AsObject();
        Assert.Equal("private-frontend", mcpServers["shared"]!["env"]!["LAYER"]!.GetValue<string>());
        Assert.Equal("company-frontend", mcpServers["profile-only"]!["env"]!["LAYER"]!.GetValue<string>());
        Assert.Contains("[mcp_servers.shared]", await File.ReadAllTextAsync(Path.Combine(projectPath, ".codex", "config.toml")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyProjectProfileAsync_Does_Not_Rebuild_Unrelated_Broken_Profile()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "project-a");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "settings"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "global.settings.json"),
            """
            {
              "theme": "global-ok"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "frontend.settings.json"),
            """
            {
              "theme": "frontend-ok"
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "settings", "backend.settings.json"),
            "{ invalid json");

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService());

        var result = await service.ApplyProjectProfileAsync(scope.RootPath, projectPath, WorkspaceProfiles.FrontendId);

        Assert.True(result.Success, result.Details);
        Assert.Contains("frontend-ok", await File.ReadAllTextAsync(Path.Combine(projectPath, ".claude", "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeMcpAutomationService_GenerateConfigsAsync_Writes_All_Client_Outputs()
    {
        using var scope = new TestHubRootScope();
        var userHome = Path.Combine(scope.RootPath, "user-home");
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
                },
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
        await File.WriteAllTextAsync(
            Path.Combine(manifestRoot, "frontend.json"),
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

        var service = new NativeMcpAutomationService(() => userHome);

        var result = await service.GenerateConfigsAsync(scope.RootPath);

        Assert.True(result.Success, result.Details);
        var claudePath = Path.Combine(scope.RootPath, "mcp", "generated", "claude", "frontend.mcp.json");
        var codexPath = Path.Combine(scope.RootPath, "mcp", "generated", "codex", "frontend.config.toml");
        var antigravityPath = Path.Combine(scope.RootPath, "mcp", "generated", "antigravity", "frontend.mcp.json");
        Assert.Contains("shared", await File.ReadAllTextAsync(claudePath), StringComparison.Ordinal);
        Assert.Contains("coplay-mcp", await File.ReadAllTextAsync(claudePath), StringComparison.Ordinal);
        Assert.Contains("MCP_TOOL_TIMEOUT", await File.ReadAllTextAsync(claudePath), StringComparison.Ordinal);
        Assert.Contains("project", await File.ReadAllTextAsync(claudePath), StringComparison.Ordinal);
        Assert.Contains("mcp_servers", await File.ReadAllTextAsync(codexPath), StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.coplay_mcp]", await File.ReadAllTextAsync(codexPath), StringComparison.Ordinal);
        Assert.Contains("MCP_TOOL_TIMEOUT = \"720000\"", await File.ReadAllTextAsync(codexPath), StringComparison.Ordinal);
        Assert.Contains("shared", await File.ReadAllTextAsync(antigravityPath), StringComparison.Ordinal);
        Assert.Contains("coplay-mcp", await File.ReadAllTextAsync(antigravityPath), StringComparison.Ordinal);
    }

    private static async Task WriteSkillAsync(string skillDirectory, string content)
    {
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), content);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed class ThrowingPlatformLinkService(Exception exception) : AIHub.Application.Abstractions.IPlatformLinkService
    {
        public void EnsureDirectory(string path)
        {
        }

        public void EnsureJunction(string linkPath, string targetPath, bool ignoreIfLocked = false)
        {
            throw exception;
        }
    }
}
