using System.Text.Json;
using AIHub.Application.Services;
using AIHub.Application.Abstractions;
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

    [Fact]
    public async Task PreviewGlobalOnboardingAsync_FirstRun_WithCandidates_RequiresDecision()
    {
        using var scope = new TestHubRootScope();
        var automationService = new StubWorkspaceAutomationService(
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Global,
                ProfileKind.Global,
                null,
                false,
                false,
                new[]
                {
                    new WorkspaceOnboardingCandidate(
                        "skill|demo",
                        WorkspaceOnboardingResourceKind.Skill,
                        "demo-skill",
                        "C:\\legacy\\demo-skill",
                        "legacy",
                        Path.Combine(scope.RootPath, "skills", "global", "imported", "demo-skill"),
                        Path.Combine(scope.RootPath, "AI-Personal", "skills", "global", "imported", "demo-skill"),
                        false,
                        false)
                },
                "detected"));

        var service = CreateService(scope.RootPath, automationService);

        var result = await service.PreviewGlobalOnboardingAsync();

        Assert.True(result.Success, result.Details);
        Assert.NotNull(result.Preview);
        Assert.True(result.Preview!.IsFirstRun);
        Assert.True(result.Preview.RequiresDecision);
    }

    [Fact]
    public async Task ApplyGlobalLinksAsync_RecordsGlobalOnboardingCompletion()
    {
        using var scope = new TestHubRootScope();
        var service = CreateService(scope.RootPath);

        var result = await service.ApplyGlobalLinksAsync();

        Assert.True(result.Success, result.Details);
        var settings = await new JsonHubSettingsStore(scope.RootPath).LoadAsync();
        Assert.True(settings.GlobalOnboardingCompleted);
        Assert.NotNull(settings.GlobalOnboardingCompletedAt);
    }

    [Fact]
    public async Task ApplyProjectProfileAsync_RecordsProjectOnboardingCompletion()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "demo-project");
        Directory.CreateDirectory(projectPath);
        var project = new ProjectRecord("demo-project", projectPath, ProfileKind.Frontend);
        var service = CreateService(scope.RootPath);

        var result = await service.ApplyProjectProfileAsync(project);

        Assert.True(result.Success, result.Details);
        var settings = await new JsonHubSettingsStore(scope.RootPath).LoadAsync();
        Assert.Contains(settings.OnboardedProjectPaths, path =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteProjectAsync_RemovesProjectFromOnboardedProjectPaths()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "demo-project");
        Directory.CreateDirectory(projectPath);
        var project = new ProjectRecord("demo-project", projectPath, ProfileKind.Frontend);
        var service = CreateService(scope.RootPath);

        var applyResult = await service.ApplyProjectProfileAsync(project);
        Assert.True(applyResult.Success, applyResult.Details);

        var deleteResult = await service.DeleteProjectAsync(projectPath);

        Assert.True(deleteResult.Success, deleteResult.Details);
        var settings = await new JsonHubSettingsStore(scope.RootPath).LoadAsync();
        Assert.DoesNotContain(settings.OnboardedProjectPaths, path =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveProjectAsync_WithOriginalPath_Replaces_Registered_Path_And_State()
    {
        using var scope = new TestHubRootScope();
        var originalPath = Path.Combine(scope.RootPath, "project-old");
        var updatedPath = Path.Combine(scope.RootPath, "project-new");
        Directory.CreateDirectory(originalPath);
        Directory.CreateDirectory(updatedPath);

        var service = CreateService(scope.RootPath);
        var originalProject = new ProjectRecord("demo-project", originalPath, ProfileKind.Frontend);
        var updatedProject = originalProject with { Path = updatedPath, Profile = ProfileKind.Backend };

        Assert.True((await service.SaveProjectAsync(originalProject)).Success);
        Assert.True((await service.SetCurrentProjectAsync(originalProject)).Success);
        Assert.True((await service.ApplyProjectProfileAsync(originalProject)).Success);

        var result = await service.SaveProjectAsync(updatedProject, originalPath);

        Assert.True(result.Success, result.Details);
        var projects = await new JsonProjectRegistry(scope.RootPath).GetAllAsync();
        Assert.DoesNotContain(projects, item => string.Equals(Path.GetFullPath(item.Path), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projects, item => string.Equals(Path.GetFullPath(item.Path), Path.GetFullPath(updatedPath), StringComparison.OrdinalIgnoreCase));

        var settings = await new JsonHubSettingsStore(scope.RootPath).LoadAsync();
        Assert.True(
            string.Equals(Path.GetFullPath(updatedPath), Path.GetFullPath(settings.LastOpenedProject!), StringComparison.OrdinalIgnoreCase),
            $"Expected last opened project to be {updatedPath}, but was {settings.LastOpenedProject}.");
        Assert.Contains(settings.OnboardedProjectPaths, path =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(updatedPath), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(settings.OnboardedProjectPaths, path =>
            string.Equals(Path.GetFullPath(path), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase));
    }

    private static WorkspaceControlService CreateService(string rootPath, IWorkspaceAutomationService? workspaceAutomationService = null)
    {
        return new WorkspaceControlService(
            new FixedHubRootLocator(rootPath),
            new JsonProjectRegistry(rootPath),
            new JsonHubSettingsStore(rootPath),
            workspaceAutomationService ?? new NoOpWorkspaceAutomationService(),
            new HubDashboardService());
    }

    private sealed class StubWorkspaceAutomationService : IWorkspaceAutomationService
    {
        private readonly WorkspaceOnboardingPreview _globalPreview;

        public StubWorkspaceAutomationService(WorkspaceOnboardingPreview globalPreview)
        {
            _globalPreview = globalPreview;
        }

        public Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(string hubRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(WorkspaceOnboardingPreviewResult.Ok("ok", _globalPreview));

        public Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
            string hubRoot,
            string projectPath,
            ProfileKind profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
                "ok",
                new WorkspaceOnboardingPreview(WorkspaceScope.Project, profile, projectPath, false, false, Array.Empty<WorkspaceOnboardingCandidate>(), "ok")));

        public Task<OperationResult> ApplyGlobalLinksAsync(
            string hubRoot,
            IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok", hubRoot));

        public Task<OperationResult> ApplyProjectProfileAsync(
            string hubRoot,
            string projectPath,
            ProfileKind profile,
            IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok("ok", projectPath));
    }
}
