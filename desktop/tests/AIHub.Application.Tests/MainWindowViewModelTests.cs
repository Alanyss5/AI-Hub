using AIHub.Application.Services;
using AIHub.Contracts;
using AIHub.Desktop.Services;
using AIHub.Desktop.ViewModels;
using AIHub.Infrastructure;
using System.Reflection;

namespace AIHub.Application.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SelectedSkillSourceReferenceOption_UpdatesSkillSourceReference()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SelectedSkillSource = new SkillSourceRecord
        {
            LocalName = "demo-source",
            Profile = WorkspaceProfiles.GlobalId,
            Kind = SkillSourceKind.GitRepository,
            Location = "https://example.invalid/repo.git",
            Reference = "main",
            AvailableReferences = new[] { "main", "release" }
        };

        Assert.Equal("main", viewModel.SelectedSkillSourceReferenceOption);

        viewModel.SelectedSkillSourceReferenceOption = "release";

        Assert.Equal("release", viewModel.SkillSourceReference);
    }

    [Fact]
    public void SelectedSkillSourceKindOption_LocalDirectory_Forces_Legacy_Version_Mode()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SelectedSkillSourceKindOption = new SkillSourceKindOption(
            SkillSourceKind.LocalDirectory,
            "本地目录",
            "本地目录来源");

        var option = Assert.IsType<SkillVersionTrackingOption>(viewModel.SelectedSkillVersionTrackingOption);
        Assert.Equal(SkillVersionTrackingMode.FollowReferenceLegacy, option.Value);
    }

    [Fact]
    public void RuntimeBuildIdentity_Uses_Current_Process_Metadata()
    {
        var viewModel = new MainWindowViewModel();

        Assert.Equal(DesktopBuildInfo.ExecutablePath, viewModel.RuntimeExecutablePathDisplay);
        Assert.Contains(DesktopBuildInfo.BuildLabel, viewModel.RuntimeBuildIdentityDisplay, StringComparison.Ordinal);
        Assert.Contains(DesktopBuildInfo.Version, viewModel.RuntimeBuildIdentityDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyProjectProfileCommand_Blocks_When_SelectedProject_Path_Differs_From_Form_Path()
    {
        using var scope = new TestHubRootScope();
        var registeredPath = Path.Combine(scope.RootPath, "registered-project");
        var draftPath = Path.Combine(scope.RootPath, "draft-project");
        Directory.CreateDirectory(registeredPath);
        Directory.CreateDirectory(draftPath);

        var automation = new RecordingWorkspaceAutomationService();
        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            automation,
            new ProjectRecord("OverSeaFramework", registeredPath, WorkspaceProfiles.FrontendId));
        viewModel.ProjectPath = draftPath;

        NoticeDialogRequest? notice = null;
        var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.NoticeDialogHandler = request =>
        {
            notice = request;
            shown.TrySetResult(true);
            return Task.CompletedTask;
        };

        viewModel.ApplyProjectProfileCommand.Execute(null);
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.NotNull(notice);
        Assert.Equal(0, automation.ApplyProjectProfileCallCount);
        Assert.Contains(registeredPath, notice!.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(draftPath, notice.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.Text.State.ProjectPathMismatchBlocked, viewModel.OperationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RescanProjectOnboardingCommand_Shows_Explicit_Notice_When_No_Candidates_Found()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "demo-project");
        Directory.CreateDirectory(projectPath);

        var automation = new RecordingWorkspaceAutomationService
        {
            ProjectPreviewResult = WorkspaceOnboardingPreviewResult.Ok(
                "ok",
                    new WorkspaceOnboardingPreview(
                        WorkspaceScope.Project,
                    WorkspaceProfiles.BackendId,
                    projectPath,
                    false,
                    false,
                    Array.Empty<WorkspaceOnboardingCandidate>(),
                    "扫描完成，无新增资源"))
        };

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            automation,
            new ProjectRecord("OverSeaFramework", projectPath, WorkspaceProfiles.BackendId));

        NoticeDialogRequest? notice = null;
        viewModel.NoticeDialogHandler = request =>
        {
            notice = request;
            return Task.CompletedTask;
        };

        await InvokePrivateAsync(viewModel, "ExecuteSelectedProjectOnboardingRescanAsync");

        Assert.True(notice is not null, $"Expected rescan notice, but got summary: {viewModel.OperationSummary}{Environment.NewLine}{viewModel.OperationDetails}");
        Assert.Equal(1, automation.PreviewProjectOnboardingCallCount);
        Assert.Equal(viewModel.Text.Dialogs.RescanResultTitle, notice!.Title);
        Assert.Contains(viewModel.Text.State.NoProjectReimportableResources, notice.Message, StringComparison.Ordinal);
        Assert.Contains(projectPath, notice.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, notice.Details, StringComparison.Ordinal);
    }

    private static async Task<MainWindowViewModel> CreateWorkspaceViewModelAsync(
        string rootPath,
        RecordingWorkspaceAutomationService workspaceAutomationService,
        ProjectRecord project)
    {
        var locator = new FixedHubRootLocator(rootPath);
        var settingsStore = new JsonHubSettingsStore(rootPath);
        await settingsStore.SaveAsync(new HubSettingsRecord
        {
            HubRoot = rootPath,
            AutoStartManagedMcpOnLoad = false,
            AutoCheckSkillUpdatesOnLoad = false,
            AutoSyncSafeSkillsOnLoad = false,
            ScriptExecutionRiskAccepted = true,
            ScriptExecutionRiskAcceptedAt = DateTimeOffset.UtcNow
        });

        var workspaceService = new WorkspaceControlService(
            locator,
            new JsonProjectRegistry(rootPath),
            settingsStore,
            workspaceAutomationService,
            new HubDashboardService());

        await workspaceService.SaveProjectAsync(project);
        await workspaceService.SetCurrentProjectAsync(project);

        var viewModel = new MainWindowViewModel(
            workspaceService,
            new McpControlService(
                locator,
                _ => new JsonMcpProfileStore(rootPath),
                _ => new JsonMcpRuntimeStore(rootPath),
                new PassthroughMcpProcessController(),
                new NoOpMcpAutomationService(),
                _ => settingsStore),
            new SkillsCatalogService(locator, _ => settingsStore),
            new ScriptCenterService(locator, new NoOpScriptExecutionService()));

        viewModel.ConfirmationHandler = _ => Task.FromResult(true);
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static async Task InvokePrivateAsync(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(target, null));
        await task;
    }
}
