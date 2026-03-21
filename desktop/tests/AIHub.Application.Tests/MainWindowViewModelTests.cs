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
            "Local Directory",
            "Use a local directory as the source of truth.");

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
    public async Task ApplyProjectProfileCommand_Uses_Selected_Project_Record_Instead_Of_Hidden_Form_Draft()
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

        Assert.True(viewModel.ApplyProjectProfileCommand.CanExecute(null));
        viewModel.ApplyProjectProfileCommand.Execute(null);
        await WaitForAsync(() => automation.ApplyProjectProfileCallCount == 1);

        Assert.Equal(1, automation.ApplyProjectProfileCallCount);
        Assert.Equal(registeredPath, automation.LastAppliedProjectPath);
        Assert.Equal(WorkspaceProfiles.FrontendId, automation.LastAppliedProjectProfile);
    }

    [Fact]
    public async Task NativeWorkspaceAutomationService_ApplyProjectProfileAsync_Writes_Agents_Bootstrap_Artifact()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "project");
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(projectPath);

        var service = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);

        var result = await service.ApplyProjectProfileAsync(scope.RootPath, projectPath, WorkspaceProfiles.BackendId);

        Assert.True(result.Success, result.Details);

        var bootstrapPath = Path.Combine(
            scope.RootPath,
            ".runtime",
            "effective",
            WorkspaceProfiles.BackendId,
            ".agents",
            "AGENTS.md");

        Assert.True(File.Exists(bootstrapPath));

        var content = await File.ReadAllTextAsync(bootstrapPath);
        Assert.Contains(WorkspaceProfiles.ToDisplayName(WorkspaceProfiles.BackendId), content, StringComparison.Ordinal);
        Assert.Contains(".agents\\skills", content, StringComparison.Ordinal);
        Assert.Contains(".agents\\agents", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceDiagnostics_Surface_Agents_Bootstrap_And_Agents_Agents_Health()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "project");
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, ".agents", "skills"));
        Directory.CreateDirectory(Path.Combine(projectPath, ".agents", "agents"));

        var automation = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);
        await automation.ApplyProjectProfileAsync(scope.RootPath, projectPath, WorkspaceProfiles.BackendId);

        var viewModel = new MainWindowViewModel();
        viewModel.HubRootInput = scope.RootPath;
        viewModel.SelectedProject = new ProjectRecord("Demo", projectPath, WorkspaceProfiles.BackendId);

        var bootstrapPath = Path.Combine(
            projectPath,
            ".agents",
            "AGENTS.md");

        Assert.Contains(".claude\\skills ->", viewModel.ProjectWorkspaceBindingDetails, StringComparison.Ordinal);
        Assert.Contains(".agents\\skills ->", viewModel.ProjectWorkspaceBindingDetails, StringComparison.Ordinal);
        Assert.Contains(".agents\\agents ->", viewModel.ProjectWorkspaceBindingDetails, StringComparison.Ordinal);
        Assert.Contains("AGENTS bootstrap：", viewModel.ProjectWorkspaceBindingDetails, StringComparison.Ordinal);
        Assert.Contains(bootstrapPath, viewModel.ProjectWorkspaceBindingDetails, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceDiagnostics_Warns_When_Project_Agents_Bootstrap_Copy_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "project");
        var userHome = Path.Combine(scope.RootPath, "user-home");
        Directory.CreateDirectory(projectPath);

        var automation = new NativeWorkspaceAutomationService(
            new RecordingPlatformLinkService(),
            new FakePlatformCapabilitiesService(),
            userHomeResolver: () => userHome);
        await automation.ApplyProjectProfileAsync(scope.RootPath, projectPath, WorkspaceProfiles.BackendId);

        File.Delete(Path.Combine(projectPath, ".agents", "AGENTS.md"));

        var viewModel = new MainWindowViewModel();
        viewModel.HubRootInput = scope.RootPath;
        viewModel.SelectedProject = new ProjectRecord("Demo", projectPath, WorkspaceProfiles.BackendId);
        var expectedBootstrapPath = Path.Combine(projectPath, ".agents", "AGENTS.md");

        Assert.Contains("AGENTS bootstrap：", viewModel.ProjectWorkspaceBindingDetails, StringComparison.Ordinal);
        Assert.Contains(expectedBootstrapPath, viewModel.ProjectWorkspaceBindingDetails, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("缺少 AGENTS bootstrap。", viewModel.ProjectWorkspaceBindingWarningDisplay, StringComparison.Ordinal);
        Assert.True(viewModel.HasProjectWorkspaceBindingWarning);
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
                    "No reimportable resources were found."))
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

        Assert.True(viewModel.RescanProjectOnboardingCommand.CanExecute(null));
        viewModel.RescanProjectOnboardingCommand.Execute(null);
        await WaitForAsync(() => notice is not null && automation.PreviewProjectOnboardingCallCount == 1);

        Assert.True(notice is not null, $"Expected rescan notice, but got summary: {viewModel.OperationSummary}{Environment.NewLine}{viewModel.OperationDetails}");
        Assert.Equal(1, automation.PreviewProjectOnboardingCallCount);
        Assert.Equal(viewModel.Text.Dialogs.RescanResultTitle, notice!.Title);
        Assert.Contains(viewModel.Text.State.NoProjectReimportableResources, notice.Message, StringComparison.Ordinal);
        Assert.Contains(projectPath, notice.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, notice.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switching_To_Project_Scope_Aligns_Skills_Filter_With_Current_Project_Profile()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "scope-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ScopeProject", projectPath, WorkspaceProfiles.BackendId));

        await InvokePrivateAsync(viewModel, "SwitchToSelectedProjectScopeAsync");

        Assert.Equal(WorkspaceScope.Project, viewModel.CurrentWorkspaceScope);
        Assert.NotNull(viewModel.SkillsPageContext.SelectedTarget);
        Assert.Equal("全局", viewModel.SkillsPageContext.SelectedTarget!.DisplayName);
    }

    [Fact]
    public async Task Switching_Back_To_Global_Scope_Updates_Mcp_And_Skills_Context()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "scope-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ScopeProject", projectPath, WorkspaceProfiles.BackendId));

        await InvokePrivateAsync(viewModel, "SwitchToSelectedProjectScopeAsync");
        await InvokePrivateAsync(viewModel, "SwitchToGlobalScopeAsync");

        Assert.Equal(WorkspaceScope.Global, viewModel.CurrentWorkspaceScope);
        Assert.True(viewModel.McpPageContext.IsGlobalScope);
        Assert.NotNull(viewModel.SkillsPageContext.SelectedTarget);
        Assert.Equal("全局", viewModel.SkillsPageContext.SelectedTarget!.DisplayName);
    }

    [Fact]
    public async Task SkillsPageContext_Selecting_Project_Target_Updates_Filter_Without_Changing_Workspace_Scope()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "skills-project");
        var skillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("SkillsProject", projectPath, WorkspaceProfiles.BackendId));

        var projectTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.Project?.Path == projectPath));
        viewModel.SkillsPageContext.SelectedTarget = projectTarget;

        Assert.Equal(WorkspaceScope.Project, viewModel.CurrentWorkspaceScope);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SelectedSkillFilterOption?.Value);
        Assert.Contains("SkillsProject", viewModel.SkillsPageContext.CurrentContextDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_Skill_Bindings_Rebuilds_Card_Binding_Summary_After_Rebinding()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "binding-project");
        var librarySkillDirectory = Path.Combine(scope.RootPath, "source", "library", "skills", "demo-skill");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var backendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(librarySkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "demo");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("BindingProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Equal("未绑定", viewModel.SelectedInstalledSkill.BindingSummaryDisplay);

        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "demo");

        await InvokePrivateAsync(viewModel, "LoadSkillsAsync", null, null);

        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.SelectedInstalledSkill.BindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.SelectedInstalledSkill.BindingSummaryDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_Skill_Bindings_Uses_Current_Skills_Context_Profile_As_Copy_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "context-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var backendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "global-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.GlobalId, "demo-skill");
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.CaptureBaselineAsync(WorkspaceProfiles.BackendId, "demo-skill");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ContextProject", projectPath, WorkspaceProfiles.BackendId));

        var projectTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.Project?.Path == projectPath));
        viewModel.SkillsPageContext.SelectedTarget = projectTarget;
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await InvokePrivateAsync(viewModel, "SaveSelectedSkillBindingsAsync");
        await WaitForAsync(() => File.Exists(Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "demo-skill", "SKILL.md")));

        var frontendContent = await File.ReadAllTextAsync(Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "demo-skill", "SKILL.md"));
        Assert.Equal("backend-version", frontendContent);
    }

    [Fact]
    public async Task Skill_Filter_Remains_On_User_Selection_After_Profile_Reload()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "filter-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("FilterProject", projectPath, WorkspaceProfiles.BackendId));

        var projectTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.Project?.Path == projectPath));
        viewModel.SkillsPageContext.SelectedTarget = projectTarget;
        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));

        await InvokePrivateAsync(viewModel, "LoadWorkspaceProfilesAsync");

        Assert.Equal("__all__", viewModel.SelectedSkillFilterOption?.Value);
    }

    [Fact]
    public async Task SwitchToProjectScope_Blocks_When_No_Project_Is_Selected_In_Page_Context()
    {
        using var scope = new TestHubRootScope();
        var savedPath = Path.Combine(scope.RootPath, "saved-project");
        var draftPath = Path.Combine(scope.RootPath, "draft-project");
        Directory.CreateDirectory(savedPath);
        Directory.CreateDirectory(draftPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("SavedProject", savedPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedProject = null;
        viewModel.ProjectName = "DraftOnly";
        viewModel.ProjectPath = draftPath;
        viewModel.SelectedProfileOption = viewModel.ProfileOptions.First(option => option.Value == WorkspaceProfiles.FrontendId);

        await InvokePrivateAsync(viewModel, "SwitchToSelectedProjectScopeAsync");

        Assert.DoesNotContain("DraftOnly", viewModel.LastOpenedProjectDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.Text.State.WorkspaceBindingNotSelected, viewModel.OperationSummary, StringComparison.Ordinal);
        Assert.False(viewModel.SwitchToSelectedProjectScopeCommand.CanExecute(null));
        Assert.False(viewModel.SetCurrentProjectCommand.CanExecute(null));
        Assert.False(viewModel.WorkspacePage.HasSelectedProject);
    }

    [Fact]
    public async Task SwitchToProjectScope_Blocks_When_Selected_Project_Profile_Is_No_Longer_Valid()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "invalid-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("InvalidProject", projectPath, WorkspaceProfiles.BackendId));

        await InvokePrivateAsync(viewModel, "SwitchToGlobalScopeAsync");

        var applyCatalog = typeof(MainWindowViewModel)
            .GetMethod("ApplyWorkspaceProfileCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(applyCatalog);
        applyCatalog!.Invoke(viewModel, new object[]
        {
            new[]
            {
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.GlobalId,
                    DisplayName = WorkspaceProfiles.GlobalDisplayName,
                    IsBuiltin = true,
                    IsDeletable = false,
                    SortOrder = 0
                }
            }
        });

        await InvokePrivateAsync(viewModel, "SwitchToSelectedProjectScopeAsync");

        var combinedMessage = viewModel.OperationSummary + viewModel.OperationDetails;
        Assert.Contains("项目当前引用的分类", combinedMessage, StringComparison.Ordinal);
        Assert.Contains("分类目录", combinedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("profile catalog", combinedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WorkspaceScope.Global, viewModel.CurrentWorkspaceScope);
        Assert.False(viewModel.SwitchToSelectedProjectScopeCommand.CanExecute(null));
        Assert.False(viewModel.SetCurrentProjectCommand.CanExecute(null));
    }


    [Fact]
    public async Task Workspace_Project_Commands_Enable_When_Selected_Project_Is_Valid()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "valid-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ValidProject", projectPath, WorkspaceProfiles.BackendId));

        Assert.True(viewModel.WorkspacePage.HasSelectedProject);
        Assert.True(viewModel.ApplyProjectProfileCommand.CanExecute(null));
        Assert.True(viewModel.SetCurrentProjectCommand.CanExecute(null));
        Assert.True(viewModel.SwitchToSelectedProjectScopeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Workspace_Project_Commands_Disable_When_Selected_Project_Directory_Is_Missing()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "missing-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("MissingProject", projectPath, WorkspaceProfiles.BackendId));

        Directory.Delete(projectPath, recursive: true);
        viewModel.SelectedProject = new ProjectRecord("MissingProject", projectPath, WorkspaceProfiles.BackendId);

        Assert.False(Directory.Exists(projectPath));
        Assert.True(viewModel.WorkspacePage.HasSelectedProject);
        Assert.False(viewModel.ApplyProjectProfileCommand.CanExecute(null));
        Assert.False(viewModel.SetCurrentProjectCommand.CanExecute(null));
        Assert.False(viewModel.SwitchToSelectedProjectScopeCommand.CanExecute(null));
        Assert.False(viewModel.RescanProjectOnboardingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Workspace_Project_Commands_Disable_When_Selected_Project_Directory_Disappears_Without_Reselect()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "vanishing-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("VanishingProject", projectPath, WorkspaceProfiles.BackendId));

        Assert.True(viewModel.ApplyProjectProfileCommand.CanExecute(null));

        Directory.Delete(projectPath, recursive: true);

        Assert.False(Directory.Exists(projectPath));
        Assert.NotNull(viewModel.SelectedProject);
        Assert.False(viewModel.ApplyProjectProfileCommand.CanExecute(null));
        Assert.False(viewModel.SetCurrentProjectCommand.CanExecute(null));
        Assert.False(viewModel.SwitchToSelectedProjectScopeCommand.CanExecute(null));
        Assert.False(viewModel.RescanProjectOnboardingCommand.CanExecute(null));
    }

    [Fact]
    public void Workspace_ViewModel_Uses_Workspace_Text_Mappings()
    {
        var viewModel = new MainWindowViewModel();
        var property = typeof(MainWindowViewModel).GetProperty(
            nameof(MainWindowViewModel.ProjectWorkspaceHealthStatus),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);

        property!.SetValue(viewModel, WorkspaceProjectHealthStatus.NotOnboarded);
        Assert.Equal(viewModel.Text.Workspace.ProjectNotOnboardedStatus, viewModel.WorkspacePage.ProjectStatusTitle);
        Assert.Equal(viewModel.Text.Workspace.ProjectStartButton, viewModel.WorkspacePage.ProjectPrimaryActionLabel);

        property.SetValue(viewModel, WorkspaceProjectHealthStatus.Legacy);
        Assert.Equal(viewModel.Text.Workspace.ProjectLegacyStatus, viewModel.WorkspacePage.ProjectStatusTitle);
        Assert.Equal(viewModel.Text.Workspace.ProjectUpgradeButton, viewModel.WorkspacePage.ProjectPrimaryActionLabel);

        property.SetValue(viewModel, WorkspaceProjectHealthStatus.Incomplete);
        Assert.Equal(viewModel.Text.Workspace.ProjectIncompleteStatus, viewModel.WorkspacePage.ProjectStatusTitle);
        Assert.Equal(viewModel.Text.Workspace.ProjectRepairButton, viewModel.WorkspacePage.ProjectPrimaryActionLabel);

        property.SetValue(viewModel, WorkspaceProjectHealthStatus.Healthy);
        Assert.Equal(viewModel.Text.Workspace.ProjectHealthyStatus, viewModel.WorkspacePage.ProjectStatusTitle);
        Assert.Equal(viewModel.Text.Workspace.ProjectReapplyButton, viewModel.WorkspacePage.ProjectPrimaryActionLabel);
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
                _ => settingsStore,
                _ => new JsonProjectRegistry(rootPath),
                workspaceAutomationService),
            new SkillsCatalogService(locator, _ => settingsStore, _ => new JsonProjectRegistry(rootPath), workspaceAutomationService),
            new ScriptCenterService(locator, new NoOpScriptExecutionService()));

        viewModel.ConfirmationHandler = _ => Task.FromResult(true);
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static SkillsCatalogService CreateSkillsService(string rootPath)
    {
        return new SkillsCatalogService(new FixedHubRootLocator(rootPath), null, new RecordingWorkspaceAutomationService());
    }

    private static async Task InvokePrivateAsync(object target, string methodName, params object?[]? arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(target, arguments));
        await task;
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("Timed out waiting for asynchronous command completion.");
            }

            await Task.Delay(20);
        }
    }
}


