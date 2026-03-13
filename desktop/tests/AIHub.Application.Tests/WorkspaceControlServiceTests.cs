using System.Text.Json;
using AIHub.Application.Services;
using AIHub.Infrastructure;
using AIHub.Contracts;

namespace AIHub.Application.Tests;

public sealed class WorkspaceControlServiceTests
{
    [Fact]
    public async Task PreviewConfigurationPackageImportAsync_RejectsUnsupportedVersion()
    {
        using var scope = new TestHubRootScope();
        var packagePath = Path.Combine(scope.RootPath, "invalid-package.json");
        await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(new
        {
            version = "2.0",
            exportedAt = DateTimeOffset.UtcNow,
            settings = new HubSettingsRecord(),
            projects = Array.Empty<ProjectRecord>()
        }));

        var service = CreateService(scope.RootPath);

        var result = await service.PreviewConfigurationPackageImportAsync(packagePath);

        Assert.False(result.Success);
        Assert.Contains("1.0", result.Details);
    }

    [Fact]
    public async Task PreviewConfigurationPackageImportAsync_ReturnsBackupPlanAndSections()
    {
        using var scope = new TestHubRootScope();
        var packagePath = Path.Combine(scope.RootPath, "valid-package.json");
        await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(new
        {
            version = "1.0",
            exportedAt = DateTimeOffset.UtcNow,
            settings = new HubSettingsRecord { HubRoot = scope.RootPath },
            projects = new[] { new ProjectRecord("demo", scope.RootPath, ProfileKind.Global) },
            skillsSourcesJson = "{\"sources\":[]}",
            skillsInstallsJson = "{\"installs\":[]}",
            mcpManifestJson = new Dictionary<string, string> { ["global"] = "{\"mcpServers\":{}}" }
        }));

        var service = CreateService(scope.RootPath);

        var result = await service.PreviewConfigurationPackageImportAsync(packagePath);

        Assert.True(result.Success, result.Details);
        Assert.NotNull(result.Preview);
        Assert.Equal("1.0", result.Preview!.Version);
        Assert.Contains(Path.Combine("backups", "config-packages"), result.Preview.PlannedBackupPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Preview.IncludedSections);
        Assert.NotEmpty(result.Preview.ReplaceTargets);
    }

    private static WorkspaceControlService CreateService(string rootPath)
    {
        return new WorkspaceControlService(
            new FixedHubRootLocator(rootPath),
            new JsonProjectRegistry(rootPath),
            new JsonHubSettingsStore(rootPath),
            new NoOpWorkspaceAutomationService(),
            new HubDashboardService());
    }
}
