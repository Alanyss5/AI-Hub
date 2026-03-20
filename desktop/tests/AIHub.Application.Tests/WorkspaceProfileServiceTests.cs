using System.Text.Json;
using AIHub.Application.Services;
using AIHub.Infrastructure;
using AIHub.Contracts;

namespace AIHub.Application.Tests;

public sealed class WorkspaceProfileServiceTests
{
    [Fact]
    public async Task LoadAsync_Seeds_Default_Profiles_When_Catalog_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var snapshot = await service.LoadAsync();

        Assert.Equal(
            [WorkspaceProfiles.GlobalId, WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId],
            snapshot.Profiles.Select(profile => profile.Id).ToArray());
        Assert.Equal(
            [WorkspaceProfiles.GlobalDisplayName, WorkspaceProfiles.FrontendDisplayName, WorkspaceProfiles.BackendDisplayName],
            snapshot.Profiles.Select(profile => profile.DisplayName).ToArray());
    }

    [Fact]
    public async Task SaveAsync_Adds_Custom_Profile_And_Normalizes_Id()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var result = await service.SaveAsync(
            null,
            new WorkspaceProfileRecord
            {
                Id = "Data Ops",
                DisplayName = "数据平台"
            });

        Assert.True(result.Success, result.Details);

        var snapshot = await service.LoadAsync();
        Assert.Contains(snapshot.Profiles, profile => profile.Id == "data-ops" && profile.DisplayName == "数据平台");
    }

    [Fact]
    public async Task SaveAsync_Rejects_Renaming_Existing_Profile_Id()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var createResult = await service.SaveAsync(
            null,
            new WorkspaceProfileRecord
            {
                Id = "data-ops",
                DisplayName = "数据平台"
            });
        Assert.True(createResult.Success, createResult.Details);

        var updateResult = await service.SaveAsync(
            "data-ops",
            new WorkspaceProfileRecord
            {
                Id = "data-engineering",
                DisplayName = "数据工程"
            });

        Assert.False(updateResult.Success);
        Assert.Contains("标识", updateResult.Message);
    }

    [Fact]
    public async Task LoadAsync_Reports_Custom_Profile_Usage_Across_Workspace_Features()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var saveResult = await service.SaveAsync(
            null,
            new WorkspaceProfileRecord
            {
                Id = "Data Ops",
                DisplayName = "Data Ops"
            });
        Assert.True(saveResult.Success, saveResult.Details);

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "config"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "projects"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills", "data-ops", "demo-skill"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "projects", "projects.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                projects = new[]
                {
                    new ProjectRecord("demo", "C:\\Project\\Demo", "data-ops")
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "config", "skills-installs.json"),
            JsonSerializer.Serialize(new
            {
                installs = new[]
                {
                    new SkillInstallRecord
                    {
                        Name = "demo-skill",
                        Profile = "data-ops",
                        InstalledRelativePath = "demo-skill"
                    }
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "config", "skills-state.json"),
            JsonSerializer.Serialize(new
            {
                states = new[]
                {
                    new SkillInstallStateRecord
                    {
                        Profile = "data-ops",
                        InstalledRelativePath = "demo-skill"
                    }
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "skills", "sources.json"),
            JsonSerializer.Serialize(new
            {
                sources = new[]
                {
                    new SkillSourceRecord
                    {
                        LocalName = "data-ops-source",
                        Profile = "data-ops",
                        Location = "C:\\skills",
                        Kind = SkillSourceKind.LocalDirectory
                    }
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "skills", "data-ops", "demo-skill", "SKILL.md"),
            "custom skill");

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "config", "hub-settings.json"),
            JsonSerializer.Serialize(new HubSettingsRecord
            {
                DefaultProfile = "data-ops"
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "data-ops.json"),
            """
            {
              "mcpServers": {
                "data-ops-server": {
                  "command": "demo"
                }
              }
            }
            """);

        var snapshot = await service.LoadAsync();
        var profile = Assert.Single(snapshot.Profiles, item => item.Id == "data-ops");

        Assert.Equal(1, profile.ProjectCount);
        Assert.Equal(1, profile.SkillSourceCount);
        Assert.Equal(1, profile.SkillInstallCount);
        Assert.Equal(1, profile.SkillStateCount);
        Assert.Equal(1, profile.SkillDirectoryCount);
        Assert.Equal(1, profile.McpServerCount);
        Assert.Equal(1, profile.SettingsCount);
        Assert.True(profile.HasReferences);
        Assert.Contains("MCP 1", profile.UsageSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAsync_Rejects_Global_Profile()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var result = await service.DeleteAsync(WorkspaceProfiles.GlobalId);

        Assert.False(result.Success);
        Assert.Contains("global", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_Rejects_Profile_With_Existing_References()
    {
        using var scope = new TestHubRootScope();
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "config"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "projects"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "skills", "frontend", "demo-skill"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "mcp", "manifest"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "projects", "projects.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                projects = new[]
                {
                    new ProjectRecord("demo", "C:\\Project\\Demo", WorkspaceProfiles.FrontendId)
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "config", "skills-installs.json"),
            JsonSerializer.Serialize(new
            {
                installs = new[]
                {
                    new SkillInstallRecord
                    {
                        Name = "demo-skill",
                        Profile = WorkspaceProfiles.FrontendId,
                        InstalledRelativePath = "demo-skill"
                    }
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "config", "skills-state.json"),
            JsonSerializer.Serialize(new
            {
                states = new[]
                {
                    new SkillInstallStateRecord
                    {
                        Profile = WorkspaceProfiles.FrontendId,
                        InstalledRelativePath = "demo-skill"
                    }
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "skills", "sources.json"),
            JsonSerializer.Serialize(new
            {
                sources = new[]
                {
                    new SkillSourceRecord
                    {
                        LocalName = "frontend-source",
                        Profile = WorkspaceProfiles.FrontendId,
                        Location = "C:\\skills",
                        Kind = SkillSourceKind.LocalDirectory
                    }
                }
            }));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "mcp", "manifest", "frontend.json"),
            """
            {
              "mcpServers": {
                "frontend-server": {
                  "command": "demo"
                }
              }
            }
            """);

        var service = CreateService(scope.RootPath);

        var result = await service.DeleteAsync(WorkspaceProfiles.FrontendId);

        Assert.False(result.Success);
        Assert.Contains("项目 1", result.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("来源 1", result.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("安装 1", result.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("状态 1", result.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("MCP 1", result.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAsync_Rejects_Profile_With_Command_And_Agent_Assets()
    {
        using var scope = new TestHubRootScope();
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "commands", "frontend"));
        Directory.CreateDirectory(Path.Combine(scope.RootPath, "claude", "agents", "frontend"));

        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "commands", "frontend", "demo.md"),
            "command");
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "claude", "agents", "frontend", "demo.md"),
            "agent");

        var service = CreateService(scope.RootPath);

        var result = await service.DeleteAsync(WorkspaceProfiles.FrontendId);

        Assert.False(result.Success);
        Assert.Contains("commands 1", result.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agents 1", result.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkspaceProfileService CreateService(string rootPath)
    {
        return new WorkspaceProfileService(
            new FixedHubRootLocator(rootPath),
            root => new JsonWorkspaceProfileCatalogStore(root),
            root => new JsonProjectRegistry(root),
            root => new JsonHubSettingsStore(root),
            root => new JsonMcpProfileStore(root));
    }
}
