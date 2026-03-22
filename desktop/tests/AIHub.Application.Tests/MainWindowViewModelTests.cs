using AIHub.Application.Services;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
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
        viewModel.SelectedSkillsSection = SkillsSection.Sources;
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
    public async Task Switching_To_Project_Scope_Does_Not_Reset_Skills_Category_Context()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "scope-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ScopeProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));

        await InvokePrivateAsync(viewModel, "SwitchToSelectedProjectScopeAsync");

        Assert.Equal(WorkspaceScope.Project, viewModel.CurrentWorkspaceScope);
        Assert.NotNull(viewModel.SkillsPageContext.SelectedTarget);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SkillsPageContext.SelectedTarget!.ProfileId);
        Assert.Equal("__all__", viewModel.SelectedSkillFilterOption?.Value);
    }

    [Fact]
    public async Task Switching_Back_To_Global_Scope_Does_Not_Reset_Skills_Category_Context()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "scope-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ScopeProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));

        await InvokePrivateAsync(viewModel, "SwitchToSelectedProjectScopeAsync");
        await InvokePrivateAsync(viewModel, "SwitchToGlobalScopeAsync");

        Assert.Equal(WorkspaceScope.Global, viewModel.CurrentWorkspaceScope);
        Assert.True(viewModel.McpPageContext.IsGlobalScope);
        Assert.NotNull(viewModel.SkillsPageContext.SelectedTarget);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SkillsPageContext.SelectedTarget!.ProfileId);
        Assert.Equal("__all__", viewModel.SelectedSkillFilterOption?.Value);
    }

    [Fact]
    public async Task SkillsPageContext_Selecting_Category_Target_Updates_Filter_Without_Changing_Workspace_Scope()
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

        var backendTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SkillsPageContext.SelectedTarget = backendTarget;

        Assert.Equal(WorkspaceScope.Project, viewModel.CurrentWorkspaceScope);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SelectedSkillFilterOption?.Value);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.SkillsPageContext.CurrentContextDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("SkillsProject", viewModel.SkillsPageContext.CurrentContextDisplay, StringComparison.Ordinal);
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
    public async Task Saving_Skill_Bindings_Uses_Current_Category_Context_Profile_As_Copy_Source()
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

        var backendTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SkillsPageContext.SelectedTarget = backendTarget;
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await InvokePrivateAsync(viewModel, "SaveSelectedSkillBindingsAsync");
        await WaitForAsync(() => File.Exists(Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "demo-skill", "SKILL.md")));

        var frontendContent = await File.ReadAllTextAsync(Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "demo-skill", "SKILL.md"));
        Assert.Equal("backend-version", frontendContent);
    }

    [Fact]
    public async Task Skill_Binding_Impact_Display_Is_Separated_From_Category_Context_Impact()
    {
        using var scope = new TestHubRootScope();
        var backendProjectPath = Path.Combine(scope.RootPath, "backend-project");
        var frontendProjectPath = Path.Combine(scope.RootPath, "frontend-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        Directory.CreateDirectory(backendProjectPath);
        Directory.CreateDirectory(frontendProjectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("BackendProject", backendProjectPath, WorkspaceProfiles.BackendId));
        viewModel.Projects.Add(new ProjectRecord("FrontendProject", frontendProjectPath, WorkspaceProfiles.FrontendId));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedInstalledSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            RelativePath = "demo-skill",
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId, WorkspaceProfiles.BackendId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName, WorkspaceProfiles.BackendDisplayName },
            IsRegistered = true
        };
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.CurrentSkillBindingImpactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, viewModel.CurrentSkillBindingImpactDisplay, StringComparison.Ordinal);
        Assert.Contains("FrontendProject", viewModel.CurrentSkillBindingImpactDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.CurrentSkillsContextImpactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.FrontendDisplayName, viewModel.CurrentSkillsContextImpactDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyWorkspaceProfileCatalog_Reconciles_Current_Skill_And_Group_Binding_Drafts()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "catalog-binding-draft-project");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        var globalDispatching = Path.Combine(globalGroupRoot, "dispatching-parallel-agents");
        var backendBrainstorming = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "superpowers", "brainstorming");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalBrainstorming);
        Directory.CreateDirectory(globalDispatching);
        Directory.CreateDirectory(backendBrainstorming);

        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "global-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(globalDispatching, "SKILL.md"), "global-dispatching");
        await File.WriteAllTextAsync(Path.Combine(backendBrainstorming, "SKILL.md"), "backend-brainstorming");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("CatalogBindingDraftProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item =>
            string.Equals(item.RelativePath, "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Profile, WorkspaceProfiles.GlobalId, StringComparison.OrdinalIgnoreCase)));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        Assert.True(viewModel.HasPendingSkillBindingChanges);
        Assert.True(viewModel.HasPendingSkillGroupBindingChanges);

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
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.FrontendId,
                    DisplayName = WorkspaceProfiles.FrontendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 1
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.BackendId,
                    DisplayName = WorkspaceProfiles.BackendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 2
                },
                new WorkspaceProfileRecord
                {
                    Id = "design-system",
                    DisplayName = "Design System",
                    IsBuiltin = false,
                    IsDeletable = true,
                    SortOrder = 3
                }
            }
        });

        Assert.True(viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected);
        Assert.True(viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected);
        Assert.True(viewModel.HasPendingSkillBindingChanges);
        Assert.True(viewModel.HasPendingSkillGroupBindingChanges);
    }

    [Fact]
    public async Task Selected_Binding_Targets_Impact_Follows_Visible_Binding_Editor_Tab()
    {
        using var scope = new TestHubRootScope();
        var frontendProjectPath = Path.Combine(scope.RootPath, "frontend-project");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        var globalDispatching = Path.Combine(globalGroupRoot, "dispatching-parallel-agents");
        Directory.CreateDirectory(frontendProjectPath);
        Directory.CreateDirectory(globalBrainstorming);
        Directory.CreateDirectory(globalDispatching);

        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "global-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(globalDispatching, "SKILL.md"), "global-dispatching");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("FrontendProject", frontendProjectPath, WorkspaceProfiles.FrontendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item =>
            string.Equals(item.RelativePath, "superpowers/brainstorming", StringComparison.OrdinalIgnoreCase)));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = true;

        var bindingEditorIndex = typeof(MainWindowViewModel).GetProperty(
            "SelectedSkillsBindingEditorIndex",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(bindingEditorIndex);

        bindingEditorIndex!.SetValue(viewModel, 0);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);

        bindingEditorIndex.SetValue(viewModel, 1);
        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.FrontendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void HasPendingBindingChanges_Normalizes_Distincts_And_Sorts_Both_Sides()
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            "HasPendingBindingChanges",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var draftOptions = new[]
        {
            new ProfileBindingOption(WorkspaceProfiles.BackendId, WorkspaceProfiles.BackendDisplayName, true),
            new ProfileBindingOption("FRONTEND", WorkspaceProfiles.FrontendDisplayName, true),
            new ProfileBindingOption(WorkspaceProfiles.FrontendId, WorkspaceProfiles.FrontendDisplayName, true)
        };

        var hasPending = Assert.IsType<bool>(method!.Invoke(null, new object?[]
        {
            new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId, "frontend", "BACKEND" },
            draftOptions
        }));

        Assert.False(hasPending);
    }

    [Fact]
    public async Task Skill_Binding_Editor_Separates_Persisted_Summary_From_Current_Draft()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "draft-summary-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("DraftSummaryProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));

        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.SelectedSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.False(viewModel.HasPendingSkillBindingChanges);

        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.SelectedSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.True(viewModel.HasPendingSkillBindingChanges);
    }

    [Fact]
    public async Task Save_Skill_Bindings_Command_Requires_Draft_Changes_After_Preview_Resolves()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "save-command-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("SaveCommandProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains(WorkspaceProfiles.GlobalDisplayName, StringComparison.Ordinal));

        Assert.False(viewModel.HasPendingSkillBindingChanges);
        Assert.False(viewModel.SaveSkillBindingsCommand.CanExecute(null));

        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await WaitForAsync(() => viewModel.SaveSkillBindingsCommand.CanExecute(null));

        Assert.True(viewModel.HasPendingSkillBindingChanges);
        Assert.True(viewModel.SaveSkillBindingsCommand.CanExecute(null));

        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = false;

        await WaitForAsync(() => !viewModel.SaveSkillBindingsCommand.CanExecute(null));
        Assert.False(viewModel.HasPendingSkillBindingChanges);
    }

    [Fact]
    public async Task Save_Skill_Group_Bindings_Command_Requires_Draft_Changes_After_Preview_Resolves()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "save-group-command-project");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalBrainstorming);
        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("SaveGroupCommandProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        await WaitForAsync(() => viewModel.PendingSkillGroupBindingSourceDisplay.Contains(WorkspaceProfiles.GlobalDisplayName, StringComparison.Ordinal));

        Assert.False(viewModel.HasPendingSkillGroupBindingChanges);
        Assert.False(viewModel.SaveSkillGroupBindingsCommand.CanExecute(null));

        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await WaitForAsync(() => viewModel.SaveSkillGroupBindingsCommand.CanExecute(null));

        Assert.True(viewModel.HasPendingSkillGroupBindingChanges);
        Assert.True(viewModel.SaveSkillGroupBindingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Binding_Preview_Display_Refreshes_When_Context_Source_Preference_Changes_Through_Real_Queue()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "preview-context-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var backendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "global");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("PreviewContextProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.GlobalId));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));

        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains(WorkspaceProfiles.GlobalDisplayName, StringComparison.Ordinal));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));

        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains(WorkspaceProfiles.BackendDisplayName, StringComparison.Ordinal));
        Assert.DoesNotContain("未选择Skill", viewModel.PendingSkillBindingSourceDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_Skill_Selection_Resets_Binding_Preview_Display_Through_Selection_Pipeline()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "preview-clear-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var backendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "demo-backend");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("PreviewClearProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.GlobalId));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains(WorkspaceProfiles.GlobalDisplayName, StringComparison.Ordinal));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));

        viewModel.SelectedInstalledSkill = null;
        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains("未选择Skill", StringComparison.Ordinal));
        await Task.Delay(100);

        Assert.Contains("未选择Skill", viewModel.PendingSkillBindingSourceDisplay, StringComparison.Ordinal);
        Assert.False(viewModel.SaveSkillBindingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selected_Binding_Targets_Impact_Display_Tracks_Visible_Editor_Tab()
    {
        using var scope = new TestHubRootScope();
        var backendProjectPath = Path.Combine(scope.RootPath, "backend-project");
        var frontendProjectPath = Path.Combine(scope.RootPath, "frontend-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        Directory.CreateDirectory(backendProjectPath);
        Directory.CreateDirectory(frontendProjectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(globalBrainstorming);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");
        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("BackendProject", backendProjectPath, WorkspaceProfiles.BackendId),
            new ProjectRecord("FrontendProject", frontendProjectPath, WorkspaceProfiles.FrontendId));

        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;

        var bindingEditorIndex = typeof(MainWindowViewModel).GetProperty(
            "SelectedSkillsBindingEditorIndex",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(bindingEditorIndex);

        bindingEditorIndex!.SetValue(viewModel, 1);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.FrontendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);

        bindingEditorIndex.SetValue(viewModel, 0);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, viewModel.SelectedBindingTargetsImpactDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshCommand_Preserves_Skill_And_Group_Binding_Drafts_After_Real_Profile_Catalog_Reload()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "refresh-drafts-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(globalBrainstorming);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");
        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "brainstorming");

        var locator = new FixedHubRootLocator(scope.RootPath);
        var profileService = CreateWorkspaceProfileService(scope.RootPath, locator);
        var skillsService = CreateSkillsService(scope.RootPath, locator);
        await skillsService.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await skillsService.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            locator,
            new RecordingWorkspaceAutomationService(),
            profileService,
            new ProjectRecord("RefreshDraftsProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;

        Assert.True(viewModel.HasPendingSkillBindingChanges);
        Assert.True(viewModel.HasPendingSkillGroupBindingChanges);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.PendingSkillGroupBindingSummaryDisplay, StringComparison.Ordinal);

        var saveProfileResult = await profileService.SaveAsync(
            null,
            new WorkspaceProfileRecord
            {
                Id = "design-system",
                DisplayName = "设计系统"
            });
        Assert.True(saveProfileResult.Success, saveProfileResult.Details);

        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        viewModel.RefreshCommand.Execute(null);

        await WaitForAsync(() =>
            !viewModel.IsBusy
            && viewModel.SkillsPageContext.TargetOptions.Any(item => item.ProfileId == "design-system"));

        Assert.True(viewModel.HasPendingSkillBindingChanges);
        Assert.True(viewModel.HasPendingSkillGroupBindingChanges);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.PendingSkillGroupBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillGroupBindingSummaryDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_Failure_Does_Not_Clear_Skill_Binding_Draft_After_Followup_Refresh()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "save-failure-refresh-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "demo");

        var locator = new ProgrammableHubRootLocator(scope.RootPath);
        var profileService = CreateWorkspaceProfileService(scope.RootPath, locator);
        var skillsService = CreateSkillsService(scope.RootPath, locator);
        await skillsService.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            locator,
            new RecordingWorkspaceAutomationService(),
            profileService,
            new ProjectRecord("SaveFailureRefreshProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await WaitForAsync(() => viewModel.SaveSkillBindingsCommand.CanExecute(null));

        locator.FailNextResolve("Simulated save failure.");
        viewModel.SaveSkillBindingsCommand.Execute(null);

        await WaitForAsync(() => !viewModel.IsBusy);

        Assert.True(viewModel.HasPendingSkillBindingChanges);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);

        var saveProfileResult = await profileService.SaveAsync(
            null,
            new WorkspaceProfileRecord
            {
                Id = "design-system",
                DisplayName = "设计系统"
            });
        Assert.True(saveProfileResult.Success, saveProfileResult.Details);

        viewModel.RefreshCommand.Execute(null);
        await WaitForAsync(() =>
            !viewModel.IsBusy
            && viewModel.SkillsPageContext.TargetOptions.Any(item => item.ProfileId == "design-system"));

        Assert.True(viewModel.HasPendingSkillBindingChanges);
        Assert.Contains(WorkspaceProfiles.FrontendDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switching_Selection_During_Binding_Preview_Load_Keeps_Stale_Result_From_Overwriting_New_Skill()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "preview-race-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var backendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-backend-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md"), "global");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend");

        var locator = new ProgrammableHubRootLocator(scope.RootPath);
        var skillsService = CreateSkillsService(scope.RootPath, locator);
        await skillsService.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await skillsService.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-backend-skill",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "demo-backend-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            locator,
            new RecordingWorkspaceAutomationService(),
            null,
            new ProjectRecord("PreviewRaceProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = null;
        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains("未选择Skill", StringComparison.Ordinal));

        locator.BlockNextResolve();
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        await locator.WaitForBlockedResolveAsync();

        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-backend-skill"));
        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains(WorkspaceProfiles.BackendDisplayName, StringComparison.Ordinal));

        locator.ReleaseBlockedResolve();
        await Task.Delay(100);

        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.PendingSkillBindingSourceDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillBindingSourceDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skill_Binding_Editor_Preserves_Orphaned_Binding_Profile_Selections()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "orphaned-binding-project");
        var orphanedSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", "design-system", "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(orphanedSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(orphanedSkillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = "design-system",
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("OrphanedBindingProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));

        var orphanedOption = Assert.Single(viewModel.SkillBindingProfiles.Where(option => option.ProfileId == "design-system"));
        Assert.True(orphanedOption.IsSelected);
        Assert.Contains("已失效", orphanedOption.DisplayName, StringComparison.Ordinal);
        Assert.False(viewModel.HasPendingSkillBindingChanges);
    }

    [Fact]
    public async Task Saving_Skill_Bindings_Removes_Current_Category_From_Reloaded_Summary_When_Unchecked()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "unbind-context-project");
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
            new ProjectRecord("UnbindContextProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = false;

        await InvokePrivateAsync(viewModel, "SaveSelectedSkillBindingsAsync");
        await WaitForAsync(() => !Directory.Exists(backendSkillDirectory));
        await WaitForAsync(() => !viewModel.InstalledSkills.Any(item => item.RelativePath == "demo-skill"));

        Assert.DoesNotContain(viewModel.InstalledSkills, item => item.RelativePath == "demo-skill");
        Assert.Null(viewModel.SelectedInstalledSkill);

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));

        Assert.Equal(new[] { WorkspaceProfiles.GlobalId }, viewModel.SelectedInstalledSkill.BindingProfileIds.OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.DoesNotContain(WorkspaceProfiles.BackendDisplayName, viewModel.SelectedInstalledSkill.BindingSummaryDisplay, StringComparison.Ordinal);
        Assert.False(viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected);
    }

    [Fact]
    public async Task Saving_Unchanged_Skill_Bindings_Is_A_NoOp_Even_When_Context_Prefers_Another_Copy_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "no-op-binding-project");
        var globalSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var backendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-skill");
        var frontendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "demo-skill");
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
            new ProjectRecord("NoOpBindingProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains(WorkspaceProfiles.BackendDisplayName, StringComparison.Ordinal));

        Assert.False(viewModel.HasPendingSkillBindingChanges);
        Assert.False(viewModel.SaveSkillBindingsCommand.CanExecute(null));

        await InvokePrivateAsync(viewModel, "SaveSelectedSkillBindingsAsync");

        Assert.Equal("global-version", await File.ReadAllTextAsync(Path.Combine(globalSkillDirectory, "SKILL.md")));
        Assert.Equal("backend-version", await File.ReadAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md")));
        Assert.False(Directory.Exists(frontendSkillDirectory));
    }

    [Fact]
    public async Task Saving_Skill_Group_Bindings_Uses_A_Fully_Populated_Group_Source_Profile()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "group-context-project");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        var globalDispatching = Path.Combine(globalGroupRoot, "dispatching-parallel-agents");
        var backendGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "superpowers");
        var backendBrainstorming = Path.Combine(backendGroupRoot, "brainstorming");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalBrainstorming);
        Directory.CreateDirectory(globalDispatching);
        Directory.CreateDirectory(backendBrainstorming);

        await File.WriteAllTextAsync(Path.Combine(globalGroupRoot, "README.md"), "global-root");
        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "global-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(globalDispatching, "SKILL.md"), "global-dispatching");
        await File.WriteAllTextAsync(Path.Combine(backendBrainstorming, "SKILL.md"), "backend-brainstorming");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("GroupContextProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await InvokePrivateAsync(viewModel, "SaveSelectedSkillGroupBindingsAsync");

        var frontendGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "superpowers");
        await WaitForAsync(() => File.Exists(Path.Combine(frontendGroupRoot, "README.md")));

        Assert.Equal("global-root", await File.ReadAllTextAsync(Path.Combine(frontendGroupRoot, "README.md")));
        Assert.Equal("global-brainstorming", await File.ReadAllTextAsync(Path.Combine(frontendGroupRoot, "brainstorming", "SKILL.md")));
        Assert.Equal("global-dispatching", await File.ReadAllTextAsync(Path.Combine(frontendGroupRoot, "dispatching-parallel-agents", "SKILL.md")));
    }

    [Fact]
    public async Task Skill_Group_Binding_State_Uses_Whole_Group_Semantics_When_Filter_Hides_Siblings()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "group-filter-state-project");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        var globalDispatching = Path.Combine(globalGroupRoot, "dispatching-parallel-agents");
        var backendGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "superpowers");
        var backendBrainstorming = Path.Combine(backendGroupRoot, "brainstorming");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalBrainstorming);
        Directory.CreateDirectory(globalDispatching);
        Directory.CreateDirectory(backendBrainstorming);

        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "global-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(globalDispatching, "SKILL.md"), "global-dispatching");
        await File.WriteAllTextAsync(Path.Combine(backendBrainstorming, "SKILL.md"), "backend-brainstorming");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("GroupFilterStateProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == WorkspaceProfiles.BackendId));
        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));

        await WaitForAsync(() => viewModel.PendingSkillGroupBindingSourceDisplay.Contains(WorkspaceProfiles.GlobalDisplayName, StringComparison.Ordinal));

        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.SelectedSkillGroupBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.SelectedSkillGroupBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.True(viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected);
        Assert.True(viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected);
        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.PendingSkillGroupBindingSourceDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.GlobalDisplayName, viewModel.CurrentSkillGroupBindingImpactDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_Skill_Group_Bindings_Uses_Whole_Group_Source_When_Filter_Hides_Siblings()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "group-filter-save-project");
        var globalGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers");
        var globalBrainstorming = Path.Combine(globalGroupRoot, "brainstorming");
        var globalDispatching = Path.Combine(globalGroupRoot, "dispatching-parallel-agents");
        var backendGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "superpowers");
        var backendBrainstorming = Path.Combine(backendGroupRoot, "brainstorming");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(globalBrainstorming);
        Directory.CreateDirectory(globalDispatching);
        Directory.CreateDirectory(backendBrainstorming);

        await File.WriteAllTextAsync(Path.Combine(globalGroupRoot, "README.md"), "global-root");
        await File.WriteAllTextAsync(Path.Combine(globalBrainstorming, "SKILL.md"), "global-brainstorming");
        await File.WriteAllTextAsync(Path.Combine(globalDispatching, "SKILL.md"), "global-dispatching");
        await File.WriteAllTextAsync(Path.Combine(backendBrainstorming, "SKILL.md"), "backend-brainstorming");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "dispatching-parallel-agents",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/dispatching-parallel-agents",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.BackendId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("GroupFilterSaveProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == WorkspaceProfiles.BackendId));
        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await InvokePrivateAsync(viewModel, "SaveSelectedSkillGroupBindingsAsync");

        var frontendGroupRoot = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "superpowers");
        await WaitForAsync(() => File.Exists(Path.Combine(frontendGroupRoot, "README.md")));

        Assert.Equal("global-root", await File.ReadAllTextAsync(Path.Combine(frontendGroupRoot, "README.md")));
        Assert.Equal("global-brainstorming", await File.ReadAllTextAsync(Path.Combine(frontendGroupRoot, "brainstorming", "SKILL.md")));
        Assert.Equal("global-dispatching", await File.ReadAllTextAsync(Path.Combine(frontendGroupRoot, "dispatching-parallel-agents", "SKILL.md")));
    }

    [Fact]
    public async Task Pending_Skill_Binding_Source_Display_Uses_Service_Resolution_When_Library_Is_Actual_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "library-fallback-project");
        var librarySkillDirectory = Path.Combine(scope.RootPath, "source", "library", "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(librarySkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(librarySkillDirectory, "SKILL.md"), "library-version");

        Directory.CreateDirectory(Path.Combine(scope.RootPath, "source", "registry"));
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "source", "registry", "skills-installs.json"),
            """
            {
              "installs": [
                {
                  "name": "demo-skill",
                  "profile": "backend",
                  "installedRelativePath": "demo-skill",
                  "customizationMode": "Local"
                }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scope.RootPath, "source", "registry", "skills-state.json"),
            """
            {
              "states": [
                {
                  "profile": "backend",
                  "installedRelativePath": "demo-skill",
                  "baselineCapturedAt": "2026-03-21T00:00:00Z",
                  "baselineFiles": []
                }
              ]
            }
            """);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("LibraryFallbackProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedInstalledSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.BackendId,
            RelativePath = "demo-skill",
            BindingProfileIds = new[] { WorkspaceProfiles.BackendId },
            BindingDisplayTags = new[] { WorkspaceProfiles.BackendDisplayName },
            IsRegistered = true
        };
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = false;

        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains(WorkspaceProfiles.BackendDisplayName, StringComparison.Ordinal));

        Assert.Contains("保存后上游来源", viewModel.PendingSkillBindingSourceDisplay, StringComparison.Ordinal);
        Assert.Contains(WorkspaceProfiles.BackendDisplayName, viewModel.PendingSkillBindingSourceDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skill_Browser_Search_Does_Not_Prune_Shared_Sources_And_Does_Not_Clear_Source_Editor_Selection()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "browser-source-selection-project");
        var skillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var alphaSourcePath = Path.Combine(scope.RootPath, "catalog-alpha");
        var betaSourcePath = Path.Combine(scope.RootPath, "catalog-beta");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(skillDirectory);
        Directory.CreateDirectory(alphaSourcePath);
        Directory.CreateDirectory(betaSourcePath);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var firstSourceResult = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "alpha-source",
                Profile = WorkspaceProfiles.GlobalId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = alphaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });
        Assert.True(firstSourceResult.Success, firstSourceResult.Details);

        var secondSourceResult = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "beta-source",
                Profile = WorkspaceProfiles.BackendId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = betaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });
        Assert.True(secondSourceResult.Success, secondSourceResult.Details);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("BrowserSourceSelectionProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "alpha-source"));
        viewModel.SelectedSkillInstallSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "beta-source"));

        Assert.Equal(2, viewModel.SkillSources.Count);
        Assert.Equal("alpha-source", viewModel.SelectedSkillSource?.LocalName);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        viewModel.SkillSearchText = "demo-skill";

        Assert.Equal(2, viewModel.SkillSources.Count);
        Assert.Empty(viewModel.SkillBrowserSources);
        Assert.Null(viewModel.SelectedSkillSource);

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        Assert.Equal("alpha-source", viewModel.SelectedSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SelectedSkillInstallSource?.LocalName);
    }

    [Fact]
    public async Task Browse_Source_Selection_Is_Separated_From_Sources_Editor_Selection_During_Search()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "browser-editor-source-split-project");
        var skillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var alphaSourcePath = Path.Combine(scope.RootPath, "catalog-alpha");
        var betaSourcePath = Path.Combine(scope.RootPath, "catalog-beta");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(skillDirectory);
        Directory.CreateDirectory(alphaSourcePath);
        Directory.CreateDirectory(betaSourcePath);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "alpha-source",
                Profile = WorkspaceProfiles.GlobalId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = alphaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });
        await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "beta-source",
                Profile = WorkspaceProfiles.BackendId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = betaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("BrowserEditorSourceSplitProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "beta-source"));
        Assert.Equal("beta-source", viewModel.SelectedSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SkillSourceLocalName);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        viewModel.SelectedSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));
        Assert.Equal("alpha-source", viewModel.SelectedSkillSource?.LocalName);

        viewModel.SkillSearchText = "demo-skill";

        Assert.Empty(viewModel.SkillBrowserSources);
        Assert.Null(viewModel.SelectedSkillSource);

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        Assert.Equal("beta-source", viewModel.SelectedSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SkillSourceLocalName);
    }

    [Fact]
    public async Task Open_Selected_Source_Management_Syncs_Browse_Selection_Into_Editor_Selection()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "open-source-management-sync-project");
        var alphaSourcePath = Path.Combine(scope.RootPath, "catalog-alpha");
        var betaSourcePath = Path.Combine(scope.RootPath, "catalog-beta");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(alphaSourcePath);
        Directory.CreateDirectory(betaSourcePath);

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "alpha-source",
                Profile = WorkspaceProfiles.GlobalId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = alphaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });
        await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "beta-source",
                Profile = WorkspaceProfiles.BackendId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = betaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("OpenSourceManagementSyncProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "beta-source"));
        Assert.Equal("beta-source", viewModel.SkillSourceLocalName);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        viewModel.SelectedSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));

        await InvokePrivateAsync(viewModel, "OpenSelectedSkillSourceManagementAsync");

        Assert.Equal(SkillsSection.Sources, viewModel.SelectedSkillsSection);
        Assert.Equal("alpha-source", viewModel.SelectedSkillSource?.LocalName);
        Assert.Equal("alpha-source", viewModel.SkillSourceLocalName);
    }

    [Fact]
    public async Task Saving_Skill_Binding_Reload_Does_Not_Sync_Browse_Source_Back_Into_Source_Editor()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "binding-save-source-reload-project");
        var skillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        var alphaSourcePath = Path.Combine(scope.RootPath, "catalog-alpha");
        var betaSourcePath = Path.Combine(scope.RootPath, "catalog-beta");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(skillDirectory);
        Directory.CreateDirectory(alphaSourcePath);
        Directory.CreateDirectory(betaSourcePath);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "alpha-source",
                Profile = WorkspaceProfiles.GlobalId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = alphaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });
        await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "beta-source",
                Profile = WorkspaceProfiles.BackendId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = betaSourcePath,
                CatalogPath = "skills",
                IsEnabled = true
            });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("BindingSaveSourceReloadProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedEditableSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "beta-source"));
        await InvokePrivateAsync(viewModel, "ClearSkillSourceFormAsync");
        Assert.Null(viewModel.SelectedEditableSkillSource);
        Assert.Equal(string.Empty, viewModel.SkillSourceLocalName);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        viewModel.SelectedBrowserSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));

        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        await WaitForAsync(() => viewModel.SaveSkillBindingsCommand.CanExecute(null));
        await InvokePrivateAsync(viewModel, "SaveSelectedSkillBindingsAsync");

        Assert.Equal("alpha-source", viewModel.SelectedBrowserSkillSource?.LocalName);

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        Assert.Null(viewModel.SelectedEditableSkillSource);
        Assert.Equal(string.Empty, viewModel.SkillSourceLocalName);
    }

    [Fact]
    public async Task Group_Binding_Preview_Entering_Loading_Clears_Old_Impact_Display()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "group-preview-loading-project");
        var skillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers", "brainstorming");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var locator = new ProgrammableHubRootLocator(scope.RootPath);
        var service = CreateSkillsService(scope.RootPath, locator);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var backendProject = new ProjectRecord("BackendImpactProject", projectPath, WorkspaceProfiles.BackendId);
        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            locator,
            new RecordingWorkspaceAutomationService(),
            null,
            backendProject);

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "superpowers"));
        viewModel.SelectedSkillsBindingEditorIndex = 1;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        SetPrivateField(
            viewModel,
            "_pendingSkillGroupBindingResolution",
            new BindingResolutionPreview(
                BindingResolutionStatus.Resolved,
                string.Empty,
                BindingSourceKind.Category,
                WorkspaceProfiles.GlobalId,
                BindingSourceKind.Category,
                WorkspaceProfiles.FrontendId,
                new[] { WorkspaceProfiles.BackendId },
                new[] { "superpowers/brainstorming" }));
        SetPrivateField(
            viewModel,
            "_pendingSkillGroupBindingPreviewState",
            Enum.Parse(
                typeof(MainWindowViewModel).GetNestedType("BindingPreviewState", BindingFlags.NonPublic)!,
                "Resolved"));

        Assert.Contains("BackendImpactProject", viewModel.CurrentSkillGroupBindingImpactDisplay, StringComparison.Ordinal);

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                changedProperties.Add(args.PropertyName!);
            }
        };

        locator.BlockNextResolve();
        var queuePreviewMethod = typeof(MainWindowViewModel).GetMethod(
            "QueueSkillGroupBindingResolutionPreviewRefresh",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(queuePreviewMethod);
        queuePreviewMethod!.Invoke(viewModel, null);
        await locator.WaitForBlockedResolveAsync();

        Assert.Contains(nameof(MainWindowViewModel.CurrentSkillGroupBindingImpactDisplay), changedProperties);
        Assert.DoesNotContain("BackendImpactProject", viewModel.CurrentSkillGroupBindingImpactDisplay, StringComparison.Ordinal);
        locator.ReleaseBlockedResolve();
    }

    [Fact]
    public void Failed_Skill_Group_Preview_Does_Not_Keep_Showing_Old_Resolved_Impact()
    {
        var projectPath = "C:/backend-project";
        var viewModel = new MainWindowViewModel();
        viewModel.Projects.Add(new ProjectRecord("BackendProject", projectPath, WorkspaceProfiles.BackendId));
        viewModel.SelectedSkillGroup = new SkillFolderGroupItem(
            "superpowers",
            new[]
            {
                new InstalledSkillRecord
                {
                    Name = "brainstorming",
                    Profile = WorkspaceProfiles.GlobalId,
                    RelativePath = "superpowers/brainstorming",
                    BindingProfileIds = new[] { WorkspaceProfiles.FrontendId },
                    BindingDisplayTags = new[] { WorkspaceProfiles.FrontendDisplayName },
                    IsRegistered = true
                }
            },
            new[] { WorkspaceProfiles.GlobalId },
            new[] { WorkspaceProfiles.FrontendId },
            new[] { WorkspaceProfiles.FrontendDisplayName },
            new[] { "superpowers/brainstorming" });
        viewModel.SelectedSkillsBindingEditorIndex = 1;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        SetPrivateField(
            viewModel,
            "_pendingSkillGroupBindingResolution",
            new BindingResolutionPreview(
                BindingResolutionStatus.Resolved,
                string.Empty,
                BindingSourceKind.Category,
                WorkspaceProfiles.GlobalId,
                BindingSourceKind.Category,
                WorkspaceProfiles.FrontendId,
                new[] { WorkspaceProfiles.BackendId },
                new[] { "superpowers/brainstorming" }));
        SetPrivateField(
            viewModel,
            "_pendingSkillGroupBindingPreviewState",
            Enum.Parse(
                typeof(MainWindowViewModel).GetNestedType("BindingPreviewState", BindingFlags.NonPublic)!,
                "Resolved"));

        Assert.Contains("BackendProject", viewModel.CurrentSkillGroupBindingImpactDisplay, StringComparison.Ordinal);

        var resetMethod = typeof(MainWindowViewModel).GetMethod(
            "ResetSkillGroupBindingResolutionPreview",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(resetMethod);

        resetMethod!.Invoke(viewModel, new[]
        {
            Enum.Parse(
                typeof(MainWindowViewModel).GetNestedType("BindingPreviewState", BindingFlags.NonPublic)!,
                "Failed")
        });

        Assert.DoesNotContain("BackendProject", viewModel.CurrentSkillGroupBindingImpactDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Resetting_Skill_Binding_Preview_Raises_Source_Impact_And_Saveability_Refresh()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SelectedInstalledSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            RelativePath = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName }
        };
        viewModel.SelectedSkillsBindingEditorIndex = 0;

        var changedProperties = new List<string>();
        var canExecuteChangedCount = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                changedProperties.Add(args.PropertyName!);
            }
        };
        viewModel.SaveSkillBindingsCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        var resetMethod = typeof(MainWindowViewModel).GetMethod(
            "ResetSkillBindingResolutionPreview",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(resetMethod);

        resetMethod!.Invoke(viewModel, new[]
        {
            Enum.Parse(
                typeof(MainWindowViewModel).GetNestedType("BindingPreviewState", BindingFlags.NonPublic)!,
                "Failed")
        });

        Assert.Contains(nameof(MainWindowViewModel.PendingSkillBindingSourceDisplay), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.CurrentSkillBindingImpactDisplay), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.SelectedBindingTargetsImpactDisplay), changedProperties);
        Assert.True(canExecuteChangedCount > 0);
    }

    [Fact]
    public async Task Completing_Skill_Binding_Preview_Raises_Source_Impact_And_Saveability_Refresh()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "preview-refresh-project");
        var skillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("PreviewRefreshProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SelectedSkillsBindingEditorIndex = 0;

        var changedProperties = new List<string>();
        var canExecuteChangedCount = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                changedProperties.Add(args.PropertyName!);
            }
        };
        viewModel.SaveSkillBindingsCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        SetPrivateField(viewModel, "_skillBindingResolutionPreviewVersion", 1);
        await InvokePrivateAsync(
            viewModel,
            "RefreshSkillBindingResolutionPreviewAsync",
            1,
            WorkspaceProfiles.GlobalId,
            "demo-skill",
            new[] { WorkspaceProfiles.BackendId });

        Assert.Contains(nameof(MainWindowViewModel.PendingSkillBindingSourceDisplay), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.CurrentSkillBindingImpactDisplay), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.SelectedBindingTargetsImpactDisplay), changedProperties);
        Assert.True(canExecuteChangedCount > 0);
    }

    [Fact]
    public async Task Skill_Source_Editor_Preserves_Orphaned_Profile_Instead_Of_Remapping_To_Global()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "orphaned-source-project");
        Directory.CreateDirectory(projectPath);

        var service = CreateSkillsService(scope.RootPath);
        var saveSourceResult = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "demo-source",
                Profile = "design-system",
                Kind = SkillSourceKind.LocalDirectory,
                Location = projectPath,
                CatalogPath = "skills",
                IsEnabled = true
            });
        Assert.True(saveSourceResult.Success, saveSourceResult.Details);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("OrphanedSourceProject", projectPath, WorkspaceProfiles.BackendId));

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
                },
                new WorkspaceProfileRecord
                {
                    Id = "design-system",
                    DisplayName = "设计系统",
                    IsBuiltin = false,
                    IsDeletable = true,
                    SortOrder = 1
                }
            }
        });

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "demo-source"));
        Assert.Equal("design-system", viewModel.SelectedSkillSourceProfileOption?.Value);

        applyCatalog.Invoke(viewModel, new object[]
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
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.FrontendId,
                    DisplayName = WorkspaceProfiles.FrontendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 1
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.BackendId,
                    DisplayName = WorkspaceProfiles.BackendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 2
                }
            }
        });

        Assert.Equal("design-system", viewModel.SelectedSkillSourceProfileOption?.Value);
        Assert.Contains("已失效", viewModel.SelectedSkillSourceProfileOption?.DisplayName ?? string.Empty, StringComparison.Ordinal);
        Assert.True(viewModel.HasSkillSourceProfileValidationWarning);

        viewModel.SkillSourceRepository = Path.Combine(projectPath, "updated-location");
        await InvokePrivateAsync(viewModel, "SaveSkillSourceAsync");

        var snapshot = await service.LoadAsync();
        var source = Assert.Single(snapshot.Sources.Where(item => item.LocalName == "demo-source"));
        Assert.Equal("design-system", source.Profile);
        Assert.Equal(Path.Combine(projectPath, "updated-location"), source.Location);
    }

    [Fact]
    public async Task Skill_Source_Editor_Removes_Stale_Orphaned_Profile_Options_When_Switching_Back_To_Valid_Profile()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "orphaned-source-cleanup-project");
        Directory.CreateDirectory(projectPath);

        var service = CreateSkillsService(scope.RootPath);
        var saveSourceResult = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = "demo-source",
                Profile = "design-system",
                Kind = SkillSourceKind.LocalDirectory,
                Location = projectPath,
                CatalogPath = "skills",
                IsEnabled = true
            });
        Assert.True(saveSourceResult.Success, saveSourceResult.Details);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("OrphanedSourceCleanupProject", projectPath, WorkspaceProfiles.BackendId));

        var applyCatalog = typeof(MainWindowViewModel)
            .GetMethod("ApplyWorkspaceProfileCatalog", BindingFlags.Instance | BindingFlags.NonPublic);
        var setProfileSelection = typeof(MainWindowViewModel)
            .GetMethod("SetSkillSourceProfileSelection", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(applyCatalog);
        Assert.NotNull(setProfileSelection);

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
                },
                new WorkspaceProfileRecord
                {
                    Id = "design-system",
                    DisplayName = "设计系统",
                    IsBuiltin = false,
                    IsDeletable = true,
                    SortOrder = 1
                }
            }
        });

        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "demo-source"));

        applyCatalog.Invoke(viewModel, new object[]
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
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.BackendId,
                    DisplayName = WorkspaceProfiles.BackendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 1
                }
            }
        });

        setProfileSelection!.Invoke(viewModel, new object[] { WorkspaceProfiles.GlobalId });

        Assert.Equal(WorkspaceProfiles.GlobalId, viewModel.SelectedSkillSourceProfileOption?.Value);
        Assert.DoesNotContain(viewModel.SkillSourceProfileOptions, option => string.Equals(option.Value, "design-system", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ambiguous_Skill_Binding_Preview_Disables_Save_Command()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "ambiguous-preview-project");
        var frontendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.FrontendId, "skills", "demo-skill");
        var backendSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.BackendId, "skills", "demo-skill");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(frontendSkillDirectory);
        Directory.CreateDirectory(backendSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(frontendSkillDirectory, "SKILL.md"), "frontend-version");
        await File.WriteAllTextAsync(Path.Combine(backendSkillDirectory, "SKILL.md"), "backend-version");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("AmbiguousPreviewProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedInstalledSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "demo-skill",
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName },
            IsRegistered = true
        };
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;

        await WaitForAsync(() => viewModel.PendingSkillBindingSourceDisplay.Contains("disagree", StringComparison.OrdinalIgnoreCase));

        Assert.False(viewModel.SaveSkillBindingsCommand.CanExecute(null));
        Assert.Contains("disagree", viewModel.PendingSkillBindingSourceDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SkillsPageContext_Tracks_Profile_Catalog_Instead_Of_Project_List()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "catalog-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("CatalogProject", projectPath, WorkspaceProfiles.BackendId));

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
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.FrontendId,
                    DisplayName = WorkspaceProfiles.FrontendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 1
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.BackendId,
                    DisplayName = WorkspaceProfiles.BackendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 2
                },
                new WorkspaceProfileRecord
                {
                    Id = "design-system",
                    DisplayName = "设计系统",
                    IsBuiltin = false,
                    IsDeletable = true,
                    SortOrder = 3
                }
            }
        });

        Assert.Equal(4, viewModel.SkillsPageContext.TargetOptions.Count);
        Assert.Contains(viewModel.SkillsPageContext.TargetOptions, item => item.ProfileId == "design-system");
        Assert.DoesNotContain(viewModel.SkillsPageContext.TargetOptions, item => item.DisplayName.Contains("CatalogProject", StringComparison.Ordinal));
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

        var backendTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SkillsPageContext.SelectedTarget = backendTarget;
        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));

        await InvokePrivateAsync(viewModel, "LoadWorkspaceProfilesAsync");

        Assert.Equal("__all__", viewModel.SelectedSkillFilterOption?.Value);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SkillsPageContext.SelectedTarget?.ProfileId);
    }

    [Fact]
    public async Task Refreshing_Skill_Groups_Falls_Back_To_Selected_Skill_Group_When_Previous_Group_Disappears()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "group-fallback-project");
        var alphaSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "alpha", "brainstorming-helper");
        var selectedSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers", "brainstorming");
        var removedGroupSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "tools", "helper");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(alphaSkillDirectory);
        Directory.CreateDirectory(selectedSkillDirectory);
        Directory.CreateDirectory(removedGroupSkillDirectory);

        await File.WriteAllTextAsync(Path.Combine(alphaSkillDirectory, "SKILL.md"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(selectedSkillDirectory, "SKILL.md"), "selected");
        await File.WriteAllTextAsync(Path.Combine(removedGroupSkillDirectory, "SKILL.md"), "removed");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming-helper",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "alpha/brainstorming-helper",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "helper",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "tools/helper",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("GroupFallbackProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "superpowers/brainstorming"));
        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "tools"));

        viewModel.SkillSearchText = "brainstorm";

        Assert.Equal("superpowers", viewModel.SelectedSkillGroup?.RelativeRootPath);
    }

    [Fact]
    public async Task Refreshing_Skill_Filters_Keeps_Selected_Skill_And_Group_On_The_Same_Group()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "group-alignment-project");
        var alphaSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "alpha", "helper-alpha");
        var selectedSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "superpowers", "brainstorming");
        var preservedGroupSkillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "tools", "helper");
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(alphaSkillDirectory);
        Directory.CreateDirectory(selectedSkillDirectory);
        Directory.CreateDirectory(preservedGroupSkillDirectory);

        await File.WriteAllTextAsync(Path.Combine(alphaSkillDirectory, "SKILL.md"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(selectedSkillDirectory, "SKILL.md"), "selected");
        await File.WriteAllTextAsync(Path.Combine(preservedGroupSkillDirectory, "SKILL.md"), "preserved");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "helper-alpha",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "alpha/helper-alpha",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "superpowers/brainstorming",
            CustomizationMode = SkillCustomizationMode.Local
        });
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "helper",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "tools/helper",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("GroupAlignmentProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "superpowers/brainstorming"));
        viewModel.SelectedSkillGroup = Assert.Single(viewModel.SkillGroups.Where(item => item.RelativeRootPath == "tools"));

        viewModel.SkillSearchText = "helper";

        Assert.Equal("tools", viewModel.SelectedSkillGroup?.RelativeRootPath);
        Assert.Equal("tools/helper", viewModel.SelectedInstalledSkill?.RelativePath);
    }

    [Fact]
    public async Task Binding_Impact_Display_Differentiates_Projects_With_The_Same_Name()
    {
        using var scope = new TestHubRootScope();
        var backendProjectPath = Path.Combine(scope.RootPath, "backend-app");
        var mirroredProjectPath = Path.Combine(scope.RootPath, "backend-app-copy");
        var skillDirectory = Path.Combine(scope.RootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", "demo-skill");
        Directory.CreateDirectory(backendProjectPath);
        Directory.CreateDirectory(mirroredProjectPath);
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(scope.RootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = "demo-skill",
            CustomizationMode = SkillCustomizationMode.Local
        });

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("SharedName", backendProjectPath, WorkspaceProfiles.BackendId),
            new ProjectRecord("SharedName", mirroredProjectPath, WorkspaceProfiles.BackendId));

        viewModel.SkillsPageContext.SelectedTarget = Assert.Single(viewModel.SkillsPageContext.TargetOptions.Where(item => item.ProfileId == WorkspaceProfiles.BackendId));
        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;

        Assert.Contains(backendProjectPath, viewModel.CurrentSkillBindingImpactDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(mirroredProjectPath, viewModel.CurrentSkillBindingImpactDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(backendProjectPath, viewModel.CurrentSkillsContextImpactDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(mirroredProjectPath, viewModel.CurrentSkillsContextImpactDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Binding_Impact_Display_Uses_Materialized_Target_Profiles_From_Preview_When_Available()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.Projects.Add(new ProjectRecord("FrontendProject", "C:/frontend-project", WorkspaceProfiles.FrontendId));
        viewModel.Projects.Add(new ProjectRecord("BackendProject", "C:/backend-project", WorkspaceProfiles.BackendId));

        viewModel.SelectedInstalledSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "demo-skill",
            BindingProfileIds = new[] { WorkspaceProfiles.FrontendId },
            BindingDisplayTags = new[] { WorkspaceProfiles.FrontendDisplayName },
            IsRegistered = true
        };

        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        SetPrivateField(
            viewModel,
            "_pendingSkillBindingResolution",
            new BindingResolutionPreview(
                BindingResolutionStatus.Resolved,
                string.Empty,
                BindingSourceKind.Category,
                WorkspaceProfiles.GlobalId,
                BindingSourceKind.Category,
                WorkspaceProfiles.FrontendId,
                new[] { WorkspaceProfiles.FrontendId, WorkspaceProfiles.BackendId },
                new[] { "demo-skill" }));
        SetPrivateField(
            viewModel,
            "_pendingSkillBindingPreviewState",
            Enum.Parse(
                typeof(MainWindowViewModel).GetNestedType("BindingPreviewState", BindingFlags.NonPublic)!,
                "Resolved"));

        Assert.Contains("BackendProject", viewModel.CurrentSkillBindingImpactDisplay, StringComparison.Ordinal);
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
    public async Task Selecting_A_Valid_Project_Profile_Clears_Warning_And_Refreshes_Project_Command_State()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "recover-valid-project");
        Directory.CreateDirectory(projectPath);

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("RecoverValidProject", projectPath, WorkspaceProfiles.BackendId));

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
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.FrontendId,
                    DisplayName = WorkspaceProfiles.FrontendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 1
                }
            }
        });

        Assert.True(viewModel.HasProjectProfileValidationWarning);
        Assert.False(viewModel.ApplyProjectProfileCommand.CanExecute(null));
        Assert.False(viewModel.SetCurrentProjectCommand.CanExecute(null));
        Assert.False(viewModel.SwitchToSelectedProjectScopeCommand.CanExecute(null));

        viewModel.SelectedProfileOption = Assert.Single(viewModel.ProfileOptions.Where(option => option.Value == WorkspaceProfiles.FrontendId));

        Assert.False(viewModel.HasProjectProfileValidationWarning);
        Assert.Equal(string.Empty, viewModel.ProjectProfileValidationDisplay);
        Assert.True(viewModel.ApplyProjectProfileCommand.CanExecute(null));
        Assert.True(viewModel.SetCurrentProjectCommand.CanExecute(null));
        Assert.True(viewModel.SwitchToSelectedProjectScopeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selecting_A_Valid_Project_Profile_Allows_Workspace_Actions_To_Execute_With_The_Visible_Profile()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "recover-valid-project-action");
        Directory.CreateDirectory(projectPath);

        var automation = new RecordingWorkspaceAutomationService();
        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            automation,
            new ProjectRecord("RecoverValidProjectAction", projectPath, WorkspaceProfiles.BackendId));

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
                },
                new WorkspaceProfileRecord
                {
                    Id = WorkspaceProfiles.FrontendId,
                    DisplayName = WorkspaceProfiles.FrontendDisplayName,
                    IsBuiltin = true,
                    IsDeletable = true,
                    SortOrder = 1
                }
            }
        });

        viewModel.SelectedProfileOption = Assert.Single(viewModel.ProfileOptions.Where(option => option.Value == WorkspaceProfiles.FrontendId));

        Assert.True(viewModel.ApplyProjectProfileCommand.CanExecute(null));
        viewModel.ApplyProjectProfileCommand.Execute(null);
        await WaitForAsync(() => automation.ApplyProjectProfileCallCount == 1);

        Assert.Equal(projectPath, automation.LastAppliedProjectPath);
        Assert.Equal(WorkspaceProfiles.FrontendId, automation.LastAppliedProjectProfile);
        Assert.DoesNotContain("项目当前引用的分类", viewModel.OperationSummary + viewModel.OperationDetails, StringComparison.Ordinal);
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
        params ProjectRecord[] projects)
    {
        return await CreateWorkspaceViewModelAsync(
            rootPath,
            new FixedHubRootLocator(rootPath),
            workspaceAutomationService,
            null,
            projects);
    }

    private static async Task<MainWindowViewModel> CreateWorkspaceViewModelAsync(
        string rootPath,
        IHubRootLocator locator,
        RecordingWorkspaceAutomationService workspaceAutomationService,
        WorkspaceProfileService? workspaceProfileService,
        params ProjectRecord[] projects)
    {
        Assert.NotEmpty(projects);
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

        foreach (var project in projects)
        {
            await workspaceService.SaveProjectAsync(project);
        }

        await workspaceService.SetCurrentProjectAsync(projects[0]);

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
            new ScriptCenterService(locator, new NoOpScriptExecutionService()),
            workspaceProfileService: workspaceProfileService);

        viewModel.ConfirmationHandler = _ => Task.FromResult(true);
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static SkillsCatalogService CreateSkillsService(string rootPath)
    {
        return new SkillsCatalogService(new FixedHubRootLocator(rootPath), null, new RecordingWorkspaceAutomationService());
    }

    private static SkillsCatalogService CreateSkillsService(string rootPath, IHubRootLocator locator)
    {
        return new SkillsCatalogService(locator, null, new RecordingWorkspaceAutomationService());
    }

    private static WorkspaceProfileService CreateWorkspaceProfileService(string rootPath, IHubRootLocator locator)
    {
        return new WorkspaceProfileService(
            locator,
            _ => new JsonWorkspaceProfileCatalogStore(rootPath),
            _ => new JsonProjectRegistry(rootPath),
            _ => new JsonHubSettingsStore(rootPath),
            _ => new JsonMcpProfileStore(rootPath));
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

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private sealed class ProgrammableHubRootLocator : IHubRootLocator
    {
        private readonly HubRootResolution _validResolution;
        private readonly object _gateLock = new();
        private TaskCompletionSource<bool>? _nextResolveGate;
        private TaskCompletionSource<bool>? _blockedResolveNotification;
        private HubRootResolution? _nextResolutionOverride;
        private string? _preferredRoot;

        public ProgrammableHubRootLocator(string rootPath)
        {
            _preferredRoot = rootPath;
            _validResolution = new HubRootResolution(rootPath, true, "tests", Array.Empty<string>());
        }

        public Task<HubRootResolution> EvaluateAsync(string candidatePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new HubRootResolution(candidatePath, Directory.Exists(candidatePath), "tests", Array.Empty<string>()));

        public async Task<HubRootResolution> ResolveAsync(CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<bool>? gate = null;
            TaskCompletionSource<bool>? notification = null;
            HubRootResolution? nextResolutionOverride = null;

            lock (_gateLock)
            {
                nextResolutionOverride = _nextResolutionOverride;
                _nextResolutionOverride = null;
                if (nextResolutionOverride is null)
                {
                    gate = _nextResolveGate;
                    notification = _blockedResolveNotification;
                    _nextResolveGate = null;
                    _blockedResolveNotification = null;
                }
            }

            if (nextResolutionOverride is not null)
            {
                return nextResolutionOverride;
            }

            if (gate is not null)
            {
                notification?.TrySetResult(true);
                await gate.Task;
            }

            return _validResolution;
        }

        public void SetPreferredRoot(string? rootPath)
        {
            _preferredRoot = rootPath;
        }

        public string? GetPreferredRoot()
        {
            return _preferredRoot;
        }

        public void BlockNextResolve()
        {
            lock (_gateLock)
            {
                _nextResolveGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _blockedResolveNotification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public Task WaitForBlockedResolveAsync()
        {
            lock (_gateLock)
            {
                return _blockedResolveNotification?.Task ?? Task.CompletedTask;
            }
        }

        public void ReleaseBlockedResolve()
        {
            lock (_gateLock)
            {
                _nextResolveGate?.TrySetResult(true);
            }
        }

        public void FailNextResolve(params string[] errors)
        {
            lock (_gateLock)
            {
                _nextResolutionOverride = new HubRootResolution(
                    _validResolution.RootPath,
                    false,
                    "tests",
                    errors.Length == 0 ? new[] { "Simulated hub root failure." } : errors);
            }
        }
    }
}


