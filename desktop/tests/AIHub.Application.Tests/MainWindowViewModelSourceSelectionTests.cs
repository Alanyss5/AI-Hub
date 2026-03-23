using System.Reflection;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Application.Services;
using AIHub.Contracts;
using AIHub.Desktop.Services;
using AIHub.Desktop.ViewModels;
using AIHub.Infrastructure;

namespace AIHub.Application.Tests;

public sealed class MainWindowViewModelSourceSelectionTests
{
    [Fact]
    public async Task Reloading_Workspace_Profile_Dependent_Data_Does_Not_Backfill_Editor_From_Browse_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "profile-reload-project");
        Directory.CreateDirectory(projectPath);

        await CreateLocalSourceAsync(scope.RootPath, "alpha-source", "catalog-alpha");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ProfileReloadProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedBrowserSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));
        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        await InvokePrivateAsync(viewModel, "ClearSkillSourceFormAsync");
        Assert.Null(viewModel.SelectedEditableSkillSource);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        await InvokePrivateAsync(viewModel, "ReloadWorkspaceProfileDependentDataAsync", null);

        Assert.Equal("alpha-source", viewModel.SelectedBrowserSkillSource?.LocalName);
        Assert.Null(viewModel.SelectedEditableSkillSource);
        Assert.Equal(string.Empty, viewModel.SkillSourceLocalName);
    }

    [Fact]
    public async Task Capturing_Skill_Baseline_Does_Not_Backfill_Editor_From_Browse_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "baseline-source-reload-project");
        Directory.CreateDirectory(projectPath);

        await CreateInstalledSkillAsync(scope.RootPath, "demo-skill");
        await CreateLocalSourceAsync(scope.RootPath, "alpha-source", "catalog-alpha");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("BaselineSourceReloadProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedInstalledSkill = Assert.Single(viewModel.InstalledSkills.Where(item => item.RelativePath == "demo-skill"));
        viewModel.SelectedBrowserSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));
        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        await InvokePrivateAsync(viewModel, "ClearSkillSourceFormAsync");
        Assert.Null(viewModel.SelectedEditableSkillSource);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        await InvokePrivateAsync(viewModel, "CaptureSkillBaselineAsync");

        Assert.Equal("alpha-source", viewModel.SelectedBrowserSkillSource?.LocalName);
        Assert.Null(viewModel.SelectedEditableSkillSource);
        Assert.Equal(string.Empty, viewModel.SkillSourceLocalName);
    }

    [Fact]
    public async Task Scanning_Source_Preserves_Browse_Selection_While_Reloading_Editor_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "scan-source-selection-project");
        Directory.CreateDirectory(projectPath);

        await CreateLocalSourceAsync(scope.RootPath, "alpha-source", "catalog-alpha");
        await CreateLocalSourceAsync(scope.RootPath, "beta-source", "catalog-beta");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ScanSourceSelectionProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedBrowserSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));
        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedEditableSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "beta-source"));

        await InvokePrivateAsync(viewModel, "ScanSelectedSkillSourceAsync");

        Assert.Equal("alpha-source", viewModel.SelectedBrowserSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SelectedEditableSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SkillSourceLocalName);
    }

    [Fact]
    public async Task Running_Selected_Scheduled_Update_Preserves_Browse_Selection_While_Reloading_Editor_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "scheduled-update-selection-project");
        Directory.CreateDirectory(projectPath);

        await CreateLocalSourceAsync(scope.RootPath, "alpha-source", "catalog-alpha");
        await CreateLocalSourceAsync(scope.RootPath, "beta-source", "catalog-beta");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("ScheduledUpdateSelectionProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedBrowserSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));
        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedEditableSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "beta-source"));

        await InvokePrivateAsync(viewModel, "RunSelectedSkillScheduledUpdateAsync");

        Assert.Equal("alpha-source", viewModel.SelectedBrowserSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SelectedEditableSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SkillSourceLocalName);
    }

    [Fact]
    public async Task Checking_Source_Versions_Preserves_Browse_Selection_While_Reloading_Editor_Source()
    {
        using var scope = new TestHubRootScope();
        var projectPath = Path.Combine(scope.RootPath, "version-check-selection-project");
        Directory.CreateDirectory(projectPath);

        await CreateLocalSourceAsync(scope.RootPath, "alpha-source", "catalog-alpha");
        await CreateLocalSourceAsync(scope.RootPath, "beta-source", "catalog-beta");

        var viewModel = await CreateWorkspaceViewModelAsync(
            scope.RootPath,
            new RecordingWorkspaceAutomationService(),
            new ProjectRecord("VersionCheckSelectionProject", projectPath, WorkspaceProfiles.BackendId));

        viewModel.SelectedSkillFilterOption = Assert.Single(viewModel.SkillFilterOptions.Where(item => item.Value == "__all__"));
        viewModel.SelectedBrowserSkillSource = Assert.Single(viewModel.SkillBrowserSources.Where(item => item.LocalName == "alpha-source"));
        viewModel.SelectedSkillsSection = SkillsSection.Sources;
        viewModel.SelectedEditableSkillSource = Assert.Single(viewModel.SkillSources.Where(item => item.LocalName == "beta-source"));

        await InvokePrivateAsync(viewModel, "CheckSkillSourceVersionsAsync");

        Assert.Equal("alpha-source", viewModel.SelectedBrowserSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SelectedEditableSkillSource?.LocalName);
        Assert.Equal("beta-source", viewModel.SkillSourceLocalName);
    }

    private static async Task CreateInstalledSkillAsync(string rootPath, string relativePath)
    {
        var skillDirectory = Path.Combine(rootPath, "source", "profiles", WorkspaceProfiles.GlobalId, "skills", relativePath);
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(rootPath);
        await service.SaveInstallAsync(new SkillInstallRecord
        {
            Name = Path.GetFileName(relativePath),
            Profile = WorkspaceProfiles.GlobalId,
            InstalledRelativePath = relativePath,
            CustomizationMode = SkillCustomizationMode.Local
        });
    }

    private static async Task CreateLocalSourceAsync(string rootPath, string localName, string catalogDirectoryName)
    {
        var catalogRoot = Path.Combine(rootPath, catalogDirectoryName);
        var skillDirectory = Path.Combine(catalogRoot, "skills", "demo-skill");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "demo");

        var service = CreateSkillsService(rootPath);
        var result = await service.SaveSourceAsync(
            null,
            null,
            new SkillSourceRecord
            {
                LocalName = localName,
                Profile = WorkspaceProfiles.GlobalId,
                Kind = SkillSourceKind.LocalDirectory,
                Location = catalogRoot,
                CatalogPath = "skills",
                IsEnabled = true
            });
        Assert.True(result.Success, result.Details);
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

    private static async Task InvokePrivateAsync(object target, string methodName, params object?[]? arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        var suppliedArguments = arguments ?? Array.Empty<object?>();
        object?[] invocationArguments;

        if (suppliedArguments.Length == parameters.Length)
        {
            invocationArguments = suppliedArguments;
        }
        else
        {
            invocationArguments = new object?[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                if (index < suppliedArguments.Length)
                {
                    invocationArguments[index] = suppliedArguments[index];
                }
                else if (parameters[index].HasDefaultValue)
                {
                    invocationArguments[index] = Type.Missing;
                }
                else
                {
                    invocationArguments[index] = parameters[index].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[index].ParameterType)
                        : null;
                }
            }
        }

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(target, invocationArguments));
        await task;
    }
}
