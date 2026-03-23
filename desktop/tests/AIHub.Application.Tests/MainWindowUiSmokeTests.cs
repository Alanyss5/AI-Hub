using AIHub.Application.Models;
using AIHub.Contracts;
using AIHub.Desktop;
using AIHub.Desktop.Services;
using AIHub.Desktop.Text;
using AIHub.Desktop.ViewModels;
using AIHub.Desktop.Views.Tabs;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using System.Reflection;

namespace AIHub.Application.Tests;

public sealed class MainWindowUiSmokeTests
{
    [AvaloniaFact]
    public async Task MainWindow_Loads_Default_ViewModel_And_TabHeaders()
    {
        var text = DesktopTextCatalog.Default;
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow
        {
            DataContext = viewModel
        };

        window.Show();

        var overviewTab = window.FindControl<TabItem>("OverviewTab");
        var projectsTab = window.FindControl<TabItem>("ProjectsTab");
        var workspaceTab = window.FindControl<TabItem>("WorkspaceTab");
        var skillsTab = window.FindControl<TabItem>("SkillsTab");
        var scriptsTab = window.FindControl<TabItem>("ScriptsTab");
        var mcpTab = window.FindControl<TabItem>("McpTab");
        var settingsTab = window.FindControl<TabItem>("SettingsTab");
        var refreshAllButton = window.FindControl<Button>("RefreshAllButton");

        Assert.NotNull(overviewTab);
        Assert.NotNull(projectsTab);
        Assert.NotNull(workspaceTab);
        Assert.NotNull(skillsTab);
        Assert.NotNull(scriptsTab);
        Assert.NotNull(mcpTab);
        Assert.NotNull(settingsTab);
        Assert.NotNull(refreshAllButton);

        Assert.Equal(text.Shell.OverviewTabHeader, overviewTab!.Header);
        Assert.Equal(text.Projects.TabHeader, projectsTab!.Header);
        Assert.Equal(text.Shell.WorkspaceTabHeader, workspaceTab!.Header);
        Assert.Equal(text.Skills.TabHeader, skillsTab!.Header);
        Assert.Equal(text.Scripts.TabHeader, scriptsTab!.Header);
        Assert.Equal(text.Mcp.TabHeader, mcpTab!.Header);
        Assert.Equal(text.Settings.TabHeader, settingsTab!.Header);
        Assert.Equal(text.Shell.RefreshAllButton, refreshAllButton!.Content);

        Assert.IsType<ProjectsTabView>(projectsTab.Content);
        Assert.IsType<WorkspaceTabView>(workspaceTab.Content);
        Assert.IsType<SkillsTabView>(skillsTab.Content);
        Assert.IsType<McpTabView>(mcpTab.Content);

        projectsTab.IsSelected = true;
        await WaitForAsync(() => window.GetVisualDescendants().OfType<ProjectsTabView>().Any(view => ReferenceEquals(view.DataContext, viewModel.ProjectsPage)));

        workspaceTab.IsSelected = true;
        await WaitForAsync(() => window.GetVisualDescendants().OfType<WorkspaceTabView>().Any(view => ReferenceEquals(view.DataContext, viewModel.WorkspacePage)));

        skillsTab.IsSelected = true;
        await WaitForAsync(() => window.GetVisualDescendants().OfType<SkillsTabView>().Any(view => ReferenceEquals(view.DataContext, viewModel.SkillsPage)));

        mcpTab.IsSelected = true;
        await WaitForAsync(() => window.GetVisualDescendants().OfType<McpTabView>().Any(view => ReferenceEquals(view.DataContext, viewModel.McpPage)));

        var mainWindowXaml = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\MainWindow.axaml");
        Assert.Contains("<tabs:ProjectsTabView DataContext=\"{Binding ProjectsPage}\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<tabs:WorkspaceTabView DataContext=\"{Binding WorkspacePage}\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<tabs:SkillsTabView DataContext=\"{Binding SkillsPage}\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<tabs:McpTabView DataContext=\"{Binding McpPage}\" />", mainWindowXaml, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TabViews_Expose_Stable_Named_Buttons()
    {
        var viewModel = new MainWindowViewModel();

        AssertNamedButton(CreateView<ProjectsTabView>(viewModel.ProjectsPage), "ProjectsSaveButton");
        AssertNamedButton(CreateView<ProjectsTabView>(viewModel.ProjectsPage), "ProjectsDeleteButton");
        AssertNamedButton(CreateView<ProjectsTabView>(viewModel.ProjectsPage), "ProjectsClearFormButton");
        AssertNamedButton(CreateView<WorkspaceTabView>(viewModel.WorkspacePage), "WorkspaceApplyProjectButton");
        AssertNamedButton(CreateView<WorkspaceTabView>(viewModel.WorkspacePage), "WorkspaceRescanProjectButton");
        AssertNamedButton(CreateView<WorkspaceTabView>(viewModel.WorkspacePage), "WorkspaceApplyGlobalButton");
        AssertNamedButton(CreateView<WorkspaceTabView>(viewModel.WorkspacePage), "WorkspaceRescanGlobalButton");
        AssertNamedControl<ComboBox>(CreateView<SkillsTabView>(viewModel.SkillsPage), "SkillsContextTargetComboBox");
        AssertNamedControl<Border>(CreateView<SkillsTabView>(viewModel.SkillsPage), "SkillsCurrentSkillPanel");
        AssertNamedControl<Border>(CreateView<SkillsTabView>(viewModel.SkillsPage), "SkillsCurrentGroupPanel");
        AssertNamedControl<Border>(CreateView<SkillsTabView>(viewModel.SkillsPage), "SkillsCurrentSourcePanel");
        AssertNamedButton(CreateView<SkillsTabView>(viewModel.SkillsPage), "SkillsOpenSelectedBindingButton");
        AssertNamedButton(CreateView<SkillsTabView>(viewModel.SkillsPage), "SkillsOpenSelectedGroupBindingButton");
        AssertNamedButton(CreateView<SkillsTabView>(viewModel.SkillsPage), "SkillsOpenSelectedSourceManagementButton");
        AssertNamedButton(CreateView<McpTabView>(viewModel.McpPage), "McpSwitchProjectScopeButton");
        AssertNamedButton(CreateView<McpTabView>(viewModel.McpPage), "McpSwitchGlobalScopeButton");
        AssertNamedControl<ComboBox>(CreateView<McpTabView>(viewModel.McpPage), "McpContextProjectComboBox");
        AssertNamedButton(CreateView<SettingsTabView>(viewModel), "SettingsSaveButton");
    }

    [AvaloniaFact]
    public async Task Skills_Browse_Navigation_Buttons_Follow_Selection_And_Switch_Sections()
    {
        var viewModel = new MainWindowViewModel();
        var host = CreateHost(CreateView<SkillsTabView>(viewModel.SkillsPage));
        host.Show();

        var skillsView = (SkillsTabView)host.Content!;
        var skillButton = skillsView.FindControl<Button>("SkillsOpenSelectedBindingButton");
        var groupButton = skillsView.FindControl<Button>("SkillsOpenSelectedGroupBindingButton");
        var sourceButton = skillsView.FindControl<Button>("SkillsOpenSelectedSourceManagementButton");
        var bindingListTabs = skillsView.FindControl<TabControl>("SkillsBindingListTabs");
        var bindingEditorTabs = skillsView.FindControl<TabControl>("SkillsBindingEditorTabs");

        Assert.NotNull(skillButton);
        Assert.NotNull(groupButton);
        Assert.NotNull(sourceButton);
        Assert.NotNull(bindingListTabs);
        Assert.NotNull(bindingEditorTabs);
        Assert.Same(viewModel.OpenSelectedSkillBindingsCommand, skillButton!.Command);
        Assert.Same(viewModel.OpenSelectedSkillGroupBindingsCommand, groupButton!.Command);
        Assert.Same(viewModel.OpenSelectedSkillSourceManagementCommand, sourceButton!.Command);
        Assert.False(viewModel.OpenSelectedSkillBindingsCommand.CanExecute(null));
        Assert.False(viewModel.OpenSelectedSkillGroupBindingsCommand.CanExecute(null));
        Assert.False(viewModel.OpenSelectedSkillSourceManagementCommand.CanExecute(null));
        Assert.Equal(0, bindingListTabs!.SelectedIndex);
        Assert.Equal(0, bindingEditorTabs!.SelectedIndex);

        viewModel.SelectedInstalledSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "demo-skill"
        };
        viewModel.SelectedSkillGroup = new SkillFolderGroupItem(
            "superpowers",
            Array.Empty<InstalledSkillRecord>(),
            new[] { WorkspaceProfiles.GlobalId },
            new[] { WorkspaceProfiles.GlobalId },
            new[] { WorkspaceProfiles.GlobalDisplayName },
            new[] { "superpowers/brainstorming" });
        viewModel.SelectedSkillSource = new SkillSourceRecord
        {
            LocalName = "demo-source",
            Profile = WorkspaceProfiles.GlobalId,
            Location = "https://example.invalid/repo.git"
        };

        await WaitForAsync(() =>
            viewModel.OpenSelectedSkillBindingsCommand.CanExecute(null)
            && viewModel.OpenSelectedSkillGroupBindingsCommand.CanExecute(null)
            && viewModel.OpenSelectedSkillSourceManagementCommand.CanExecute(null));

        viewModel.SkillsPageContext.SelectedTarget = viewModel.SkillsPageContext.TargetOptions.First(item => item.ProfileId == WorkspaceProfiles.BackendId);

        skillButton.Command!.Execute(null);
        await WaitForAsync(() =>
            viewModel.SelectedSkillsSection == SkillsSection.Bindings
            && viewModel.SelectedSkillsBindingListIndex == 0
            && viewModel.SelectedSkillsBindingEditorIndex == 0
            && bindingListTabs.SelectedIndex == 0
            && bindingEditorTabs.SelectedIndex == 0);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SkillsPageContext.SelectedTarget?.ProfileId);

        bindingListTabs.SelectedIndex = 1;
        await WaitForAsync(() => viewModel.SelectedSkillsBindingListIndex == 1);
        Assert.Equal(0, bindingEditorTabs.SelectedIndex);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        groupButton.Command!.Execute(null);
        await WaitForAsync(() =>
            viewModel.SelectedSkillsSection == SkillsSection.Bindings
            && viewModel.SelectedSkillsBindingListIndex == 1
            && viewModel.SelectedSkillsBindingEditorIndex == 1
            && bindingListTabs.SelectedIndex == 1
            && bindingEditorTabs.SelectedIndex == 1);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SkillsPageContext.SelectedTarget?.ProfileId);

        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        sourceButton.Command!.Execute(null);
        Assert.Equal(SkillsSection.Sources, viewModel.SelectedSkillsSection);
        Assert.Equal(WorkspaceProfiles.BackendId, viewModel.SkillsPageContext.SelectedTarget?.ProfileId);
    }

    [AvaloniaFact]
    public async Task Skills_Category_Selector_And_Mcp_Project_Scope_Track_Their_Own_Context()
    {
        var viewModel = new MainWindowViewModel();
        var skillsView = CreateView<SkillsTabView>(viewModel.SkillsPage);
        var mcpView = CreateView<McpTabView>(viewModel.McpPage);
        var skillsCombo = skillsView.FindControl<ComboBox>("SkillsContextTargetComboBox");
        var mcpButton = mcpView.FindControl<Button>("McpSwitchProjectScopeButton");
        var mcpCombo = mcpView.FindControl<ComboBox>("McpContextProjectComboBox");

        Assert.NotNull(skillsCombo);
        Assert.NotNull(mcpButton);
        Assert.NotNull(mcpCombo);
        Assert.Same(viewModel.SkillsPage, skillsView.DataContext);
        Assert.Same(viewModel.McpPage, mcpView.DataContext);
        Assert.Same(viewModel.SwitchToSelectedProjectScopeCommand, mcpButton!.Command);
        Assert.False(mcpButton.IsEnabled);
        Assert.Same(viewModel.SkillsPageContext.TargetOptions, skillsCombo!.ItemsSource);
        Assert.Same(viewModel.Projects, mcpCombo!.ItemsSource);
        Assert.NotNull(skillsCombo.SelectedItem);
        Assert.Null(mcpCombo.SelectedItem);
        Assert.Contains(viewModel.SkillsPageContext.TargetOptions, item => item.ProfileId == WorkspaceProfiles.GlobalId);
        Assert.Contains(viewModel.SkillsPageContext.TargetOptions, item => item.ProfileId == WorkspaceProfiles.BackendId);

        var projectPath = Path.Combine(Path.GetTempPath(), $"aihub-ui-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectPath);
        try
        {
            var selectedProject = new ProjectRecord("SmokeProject", projectPath, WorkspaceProfiles.BackendId);
            viewModel.Projects.Add(selectedProject);
            viewModel.SelectedProject = selectedProject;

            await WaitForAsync(() => mcpButton.IsEnabled && viewModel.SkillsPageContext.TargetOptions.Count >= 2);

            Assert.True(mcpButton.IsEnabled);
            Assert.Same(selectedProject, mcpCombo.SelectedItem);
            Assert.DoesNotContain(viewModel.SkillsPageContext.TargetOptions, item => item.DisplayName.Contains("SmokeProject", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(projectPath, recursive: true);
        }
    }

    [Fact]
    public void ProjectsTabView_No_Longer_Exposes_Workspace_Action_Buttons()
    {
        var content = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\ProjectsTabView.axaml");
        Assert.DoesNotContain("ProjectsRescanGlobalOnboardingButton", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectsRescanProjectOnboardingButton", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyProjectProfileCommand", content, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyGlobalLinksCommand", content, StringComparison.Ordinal);
        Assert.Contains("OperationSummary", content, StringComparison.Ordinal);
        Assert.Contains("OperationDetails", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_Bars_Keep_Category_Selector_Visible_In_Skills_And_Project_Selector_In_Mcp()
    {
        var skills = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\SkillsTabView.axaml");
        var mcp = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\McpTabView.axaml");
        var workspace = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\WorkspaceTabView.axaml");

        Assert.Contains("SkillsContextTargetComboBox", skills, StringComparison.Ordinal);
        Assert.DoesNotContain("SkillsSwitchProjectScopeButton", skills, StringComparison.Ordinal);
        Assert.DoesNotContain("SkillsSwitchGlobalScopeButton", skills, StringComparison.Ordinal);
        Assert.Contains("McpContextProjectComboBox", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("IsVisible=\"{Binding Context.IsProjectScope}\"", mcp, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding HasSelectedProject}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"McpSaveManifestButton\"", mcp, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"McpSaveServerBindingsButton\"", mcp, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"McpResumeSuspendedButton\"", mcp, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_Pages_Use_Readable_Chinese_Copy()
    {
        var workspace = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\WorkspaceTabView.axaml");
        var projects = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\ProjectsTabView.axaml");
        var mcp = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\McpTabView.axaml");
        var settings = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\SettingsTabView.axaml");
        var scripts = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\ScriptsTabView.axaml");
        var overview = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\OverviewTabView.axaml");

        Assert.DoesNotContain("WorkspaceSetCurrentProjectButton", workspace, StringComparison.Ordinal);
        Assert.Contains(ProjectDiagnosticsHeader(), workspace, StringComparison.Ordinal);
        Assert.Contains(OperationLogHeader(), workspace, StringComparison.Ordinal);

        Assert.Contains(ProjectsListTitle(), projects, StringComparison.Ordinal);
        Assert.Contains(ProjectsFormTitle(), projects, StringComparison.Ordinal);

        Assert.Contains(McpCurrentScopeTitle(), mcp, StringComparison.Ordinal);
        Assert.Contains(McpOverviewHeader(), mcp, StringComparison.Ordinal);
        Assert.Contains(McpControlCenterTitle(), mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("Current Scope", mcp, StringComparison.Ordinal);

        Assert.Contains(SettingsProfileCatalogTitle(), settings, StringComparison.Ordinal);
        Assert.Contains(SettingsProfileEditorTitle(), settings, StringComparison.Ordinal);

        Assert.Contains(ScriptsExpertModeDescription(), scripts, StringComparison.Ordinal);
        Assert.Contains(OverviewReadinessTitle(), overview, StringComparison.Ordinal);
        Assert.Contains(OverviewRemainingGatesTitle(), overview, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Default_ViewModel_Uses_Chinese_First_Empty_State_Text()
    {
        var text = DesktopTextCatalog.Default;
        var viewModel = new MainWindowViewModel();

        Assert.Equal(text.State.WaitingForWorkspaceData, viewModel.OperationSummary);
        Assert.Equal(text.State.SelectScript, viewModel.ScriptDescriptionDisplay);
        Assert.Equal(text.State.ScriptUsagePlaceholder, viewModel.ScriptExecutionHint);
        Assert.Equal(text.State.McpConfigNotLoaded, viewModel.McpServerSummary);
    }

    [Fact]
    public void Skills_Runtime_Copy_Is_Readable_Chinese()
    {
        var viewModel = new MainWindowViewModel();

        Assert.Equal("全局", WorkspaceProfiles.GlobalDisplayName);
        Assert.Equal("前端", WorkspaceProfiles.FrontendDisplayName);
        Assert.Equal("后端", WorkspaceProfiles.BackendDisplayName);
        Assert.StartsWith("当前分类", viewModel.SkillsPageContext.CurrentContextDisplay, StringComparison.Ordinal);
        Assert.Contains("全局", viewModel.SkillsPageContext.CurrentContextDisplay, StringComparison.Ordinal);
        Assert.Contains("未绑定", viewModel.PendingSkillBindingSummaryDisplay, StringComparison.Ordinal);
        Assert.Contains("保存后将显示为未绑定", viewModel.PendingSkillBindingSaveTargetDisplay, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void DesktopWindowActivationService_Restores_Minimized_And_Hidden_Window()
    {
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel()
        };

        window.Show();
        window.WindowState = WindowState.Minimized;

        DesktopWindowActivationService.RestoreMainWindow(window);

        Assert.Equal(WindowState.Normal, window.WindowState);

        window.Hide();
        Assert.False(window.IsVisible);

        DesktopWindowActivationService.RestoreMainWindow(window);

        Assert.True(window.IsVisible);
    }

    [Fact]
    public void Overview_And_Scripts_TabView_No_Longer_Use_Legacy_Mojibake_Copy()
    {
        var overview = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\OverviewTabView.axaml");
        var scripts = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\ScriptsTabView.axaml");

        Assert.Contains(OverviewReadinessTitle(), overview, StringComparison.Ordinal);
        Assert.Contains(OverviewRemainingGatesTitle(), overview, StringComparison.Ordinal);
        Assert.Contains(ScriptsExpertModeDescription(), scripts, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Skills_Browse_Group_Cards_Show_Total_And_Filtered_Membership_Runtime()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SelectedSkillsSectionIndex = (int)SkillsSection.Browse;
        var selectedSkill = new InstalledSkillRecord
        {
            Name = "brainstorming",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "superpowers/brainstorming",
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName },
            IsRegistered = true
        };
        var siblingSkill = new InstalledSkillRecord
        {
            Name = "mindmap",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "superpowers/mindmap",
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName },
            IsRegistered = true
        };
        SeedSkillCatalogSnapshot(
            viewModel,
            new[] { selectedSkill, siblingSkill },
            Array.Empty<SkillSourceRecord>());
        viewModel.SkillSearchText = "brainstorming";

        var host = CreateHost(CreateView<SkillsTabView>(viewModel.SkillsPage));
        host.Show();
        viewModel.SelectedSkillsSection = SkillsSection.Browse;
        var browseTabs = ((SkillsTabView)host.Content!).FindControl<TabControl>("SkillsBrowseCatalogTabs");
        var groupListBox = ((SkillsTabView)host.Content!).FindControl<ListBox>("SkillsBrowseGroupList");
        Assert.NotNull(browseTabs);
        Assert.NotNull(groupListBox);
        browseTabs!.SelectedIndex = 0;

        await WaitForAsync(() =>
            browseTabs.SelectedIndex == 0
            && viewModel.SkillGroups.Count == 1
            && groupListBox!.ItemCount == 1);

        Assert.Single(viewModel.SkillGroups);
        Assert.True(viewModel.SkillGroups.Single().Skills.Count == 2);
        Assert.True(viewModel.SkillGroups.Single().ContainedSkillPaths.Count == 1);
        Assert.Equal("整组成员：2", viewModel.SkillGroups.Single().SkillCountDisplay);
        Assert.Equal("当前筛选可见成员：1", viewModel.SkillGroups.Single().VisibleSkillCountDisplay);
        groupListBox!.SelectedItem = viewModel.SkillGroups.Single();

        var detailsText = GetVisibleTextBlocks(host);
        Assert.Contains("整组成员：2", detailsText);
        Assert.Contains("当前筛选可见成员：1", detailsText);
        Assert.DoesNotContain("2 个 Skills", detailsText);
        Assert.Contains(detailsText, text => text.Contains("当前筛选仅影响浏览，保存按整组执行", StringComparison.Ordinal));
    }
    [AvaloniaFact]
    public async Task Skills_Browse_Source_Tab_Runtime_Keeps_Browse_Selection_Separate_From_Sources_Editor()
    {
        var viewModel = new MainWindowViewModel();
        var alphaSource = new SkillSourceRecord
        {
            LocalName = "alpha-source",
            Profile = WorkspaceProfiles.GlobalId,
            Kind = SkillSourceKind.LocalDirectory,
            Location = @"C:\catalog-alpha",
            CatalogPath = "skills",
            IsEnabled = true
        };
        var betaSource = new SkillSourceRecord
        {
            LocalName = "beta-source",
            Profile = WorkspaceProfiles.BackendId,
            Kind = SkillSourceKind.LocalDirectory,
            Location = @"C:\catalog-beta",
            CatalogPath = "skills",
            IsEnabled = true
        };
        SeedSkillCatalogSnapshot(
            viewModel,
            new[]
            {
                new InstalledSkillRecord
                {
                    Name = "demo-skill",
                    Profile = WorkspaceProfiles.GlobalId,
                    RelativePath = "demo-skill",
                    BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
                    BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName },
                    IsRegistered = true
                }
            },
            new[] { alphaSource, betaSource });
        viewModel.SelectedSkillFilterOption = viewModel.SkillFilterOptions.First(option => option.Value == "__all__");
        viewModel.SkillSearchText = " ";
        InvokePrivateInstanceMethod(viewModel, "ApplySkillBrowserFilters", null, null);

        var host = CreateHost(CreateView<SkillsTabView>(viewModel.SkillsPage));
        host.Show();
        var skillsView = (SkillsTabView)host.Content!;
        var skillsSectionTabs = skillsView.GetVisualDescendants()
            .OfType<TabControl>()
            .FirstOrDefault(tabControl => tabControl.Classes.Contains("side-nav"));
        var browseTabs = skillsView.FindControl<TabControl>("SkillsBrowseCatalogTabs");
        var browseSourceList = skillsView.FindControl<ListBox>("SkillsBrowseSourcesList");
        var sourcesEditorList = skillsView.FindControl<ListBox>("SkillsSourcesEditorList");

        Assert.NotNull(skillsSectionTabs);
        Assert.NotNull(browseTabs);
        Assert.NotNull(browseSourceList);
        Assert.NotNull(sourcesEditorList);
        Assert.True(ReferenceEquals(browseSourceList!.ItemsSource, viewModel.SkillBrowserSources));
        Assert.True(ReferenceEquals(sourcesEditorList!.ItemsSource, viewModel.SkillSources));

        skillsSectionTabs!.SelectedIndex = (int)SkillsSection.Sources;
        viewModel.SelectedEditableSkillSource = betaSource;
        await WaitForAsync(() =>
            viewModel.SelectedSkillsSection == SkillsSection.Sources
            && ReferenceEquals(viewModel.SelectedEditableSkillSource, betaSource)
            && viewModel.SkillSourceLocalName == "beta-source");

        skillsSectionTabs.SelectedIndex = (int)SkillsSection.Browse;
        browseTabs!.SelectedIndex = 2;
        await WaitForAsync(() =>
            viewModel.SelectedSkillsSection == SkillsSection.Browse
            && skillsSectionTabs.SelectedIndex == (int)SkillsSection.Browse);

        browseSourceList.SelectedItem = alphaSource;
        await WaitForAsync(() =>
            ReferenceEquals(viewModel.SelectedBrowserSkillSource, alphaSource)
            && skillsView.FindControl<Button>("SkillsOpenSelectedSourceManagementButton")!.Command!.CanExecute(null));

        var browseVisibleText = GetVisibleTextBlocks(skillsView.FindControl<Border>("SkillsCurrentSourcePanel")!);
        Assert.Contains(alphaSource.SourceDisplayName, browseVisibleText);

        skillsSectionTabs.SelectedIndex = (int)SkillsSection.Sources;
        await WaitForAsync(() =>
            viewModel.SelectedSkillsSection == SkillsSection.Sources
            && ReferenceEquals(viewModel.SelectedEditableSkillSource, betaSource)
            && viewModel.SkillSourceLocalName == "beta-source"
            && sourcesEditorList.ItemCount == 2);

        Assert.Equal(betaSource, sourcesEditorList.SelectedItem);
        Assert.Equal("beta-source", viewModel.SkillSourceLocalName);
    }

    [AvaloniaFact]
    public async Task Skills_Binding_Page_Runtime_Tab_Selection_Tracks_Binding_Editor_State()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedSkillsSection = SkillsSection.Bindings
        };
        viewModel.Projects.Add(new ProjectRecord("FrontendProject", @"C:\frontend-project", WorkspaceProfiles.FrontendId));
        viewModel.Projects.Add(new ProjectRecord("BackendProject", @"C:\backend-project", WorkspaceProfiles.BackendId));

        var selectedSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "demo-skill",
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName },
            IsRegistered = true
        };
        viewModel.InstalledSkills.Add(selectedSkill);
        viewModel.SelectedInstalledSkill = selectedSkill;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;

        var selectedGroup = new SkillFolderGroupItem(
            "superpowers",
            Array.Empty<InstalledSkillRecord>(),
            new[] { WorkspaceProfiles.GlobalId },
            new[] { WorkspaceProfiles.GlobalId },
            new[] { WorkspaceProfiles.GlobalDisplayName },
            new[] { "superpowers/brainstorming" });
        viewModel.SkillGroups.Add(selectedGroup);
        viewModel.SelectedSkillGroup = selectedGroup;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;

        var host = CreateHost(CreateView<SkillsTabView>(viewModel.SkillsPage));
        host.Show();

        var skillsView = (SkillsTabView)host.Content!;
        var bindingListTabs = skillsView.FindControl<TabControl>("SkillsBindingListTabs");
        var bindingEditorTabs = skillsView.FindControl<TabControl>("SkillsBindingEditorTabs");

        Assert.NotNull(bindingListTabs);
        Assert.NotNull(bindingEditorTabs);
        Assert.Equal(0, bindingListTabs!.SelectedIndex);
        Assert.Equal(0, bindingEditorTabs.SelectedIndex);
        Assert.Equal(0, viewModel.SelectedSkillsBindingListIndex);
        Assert.Equal(0, viewModel.SelectedSkillsBindingEditorIndex);

        bindingListTabs.SelectedIndex = 1;
        await WaitForAsync(() => viewModel.SelectedSkillsBindingListIndex == 1);
        Assert.Equal(1, bindingListTabs.SelectedIndex);
        Assert.Equal(0, bindingEditorTabs.SelectedIndex);
        Assert.Equal(0, viewModel.SelectedSkillsBindingEditorIndex);

        bindingEditorTabs.SelectedIndex = 1;
        await WaitForAsync(() => viewModel.SelectedSkillsBindingEditorIndex == 1);
        Assert.Equal(1, bindingListTabs.SelectedIndex);

        viewModel.SelectedSkillsBindingListIndex = 0;
        await WaitForAsync(() => bindingListTabs.SelectedIndex == 0);
        Assert.Equal(1, bindingEditorTabs.SelectedIndex);

        viewModel.SelectedSkillsBindingEditorIndex = 0;
        await WaitForAsync(() => bindingEditorTabs.SelectedIndex == 0);
    }

    [AvaloniaFact]
    public async Task Skills_Binding_Page_Runtime_Shows_Context_Impact_And_Both_Editor_Contracts()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedSkillsSection = SkillsSection.Bindings
        };
        viewModel.SelectedSkillsSectionIndex = (int)SkillsSection.Bindings;
        viewModel.Projects.Add(new ProjectRecord("FrontendProject", @"C:\frontend-project", WorkspaceProfiles.FrontendId));
        viewModel.Projects.Add(new ProjectRecord("BackendProject", @"C:\backend-project", WorkspaceProfiles.BackendId));

        var selectedSkill = new InstalledSkillRecord
        {
            Name = "demo-skill",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "superpowers/brainstorming",
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName },
            IsRegistered = true
        };
        var siblingSkill = new InstalledSkillRecord
        {
            Name = "demo-skill-alt",
            Profile = WorkspaceProfiles.GlobalId,
            RelativePath = "superpowers/mindmap",
            BindingProfileIds = new[] { WorkspaceProfiles.GlobalId },
            BindingDisplayTags = new[] { WorkspaceProfiles.GlobalDisplayName },
            IsRegistered = true
        };
        SeedSkillCatalogSnapshot(
            viewModel,
            new[] { selectedSkill, siblingSkill },
            Array.Empty<SkillSourceRecord>());
        viewModel.SkillSearchText = "brainstorming";
        viewModel.SelectedInstalledSkill = selectedSkill;
        viewModel.SelectedSkillGroup = viewModel.SkillGroups.Single();
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.FrontendId).IsSelected = true;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.GlobalId).IsSelected = false;
        viewModel.SkillGroupBindingProfiles.Single(option => option.ProfileId == WorkspaceProfiles.BackendId).IsSelected = true;

        var host = CreateHost(CreateView<SkillsTabView>(viewModel.SkillsPage));
        host.Show();
        viewModel.SelectedSkillsSection = SkillsSection.Bindings;

        await WaitForAsync(() => GetVisibleTextBlocks(host).Contains("当前分类影响"));

        var bindingListTabs = ((SkillsTabView)host.Content!).FindControl<TabControl>("SkillsBindingListTabs");
        var bindingEditorTabs = ((SkillsTabView)host.Content!).FindControl<TabControl>("SkillsBindingEditorTabs");
        Assert.NotNull(bindingListTabs);
        Assert.NotNull(bindingEditorTabs);
        Assert.Equal(new[] { "按 Skill", "按分组" }, bindingListTabs!.Items.OfType<TabItem>().Select(item => item.Header?.ToString()).ToArray());
        Assert.Equal(new[] { "Skill 绑定", "分组绑定" }, bindingEditorTabs!.Items.OfType<TabItem>().Select(item => item.Header?.ToString()).ToArray());

        var visibleText = GetVisibleTextBlocks(host);
        Assert.Contains("当前分类影响", visibleText);
        Assert.Contains("本次草稿影响", visibleText);
        Assert.Contains("已保存绑定", visibleText);
        Assert.Contains("当前草稿", visibleText);
        Assert.Contains("保存后上游来源", visibleText);
        Assert.Contains("保存此 Skill 绑定", visibleText);

        var skillImpact = viewModel.CurrentSkillBindingImpactDisplay;
        var groupImpact = viewModel.CurrentSkillGroupBindingImpactDisplay;
        Assert.NotEqual(skillImpact, groupImpact);
        Assert.Contains(skillImpact, visibleText);
        Assert.Contains(viewModel.SelectedBindingTargetsImpactDisplay, visibleText);

        bindingListTabs.SelectedIndex = 1;
        bindingEditorTabs.SelectedIndex = 1;
        await WaitForAsync(() =>
            viewModel.SelectedSkillsBindingListIndex == 1
            && bindingListTabs.SelectedIndex == 1
            && viewModel.SelectedSkillsBindingEditorIndex == 1
            && bindingEditorTabs.SelectedIndex == 1);

        visibleText = GetVisibleTextBlocks(host);
        Assert.Equal(1, bindingListTabs.SelectedIndex);
        Assert.Equal(1, bindingEditorTabs.SelectedIndex);
        Assert.True(viewModel.SkillGroups.Single().Skills.Count == 2);
        Assert.True(viewModel.SkillGroups.Single().ContainedSkillPaths.Count == 1);
        Assert.Contains(groupImpact, visibleText);
        Assert.Contains(viewModel.SelectedBindingTargetsImpactDisplay, visibleText);

        var skillsXaml = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\SkillsTabView.axaml");
        Assert.Contains("当前筛选仅影响浏览，保存按整组执行。", skillsXaml, StringComparison.Ordinal);
        Assert.Contains("保存后上游来源", skillsXaml, StringComparison.Ordinal);
    }
    [AvaloniaFact]
    public async Task Skills_Source_Editor_Runtime_Shows_Profile_Warning_And_Scan_Only_Copy()
    {
        var viewModel = new MainWindowViewModel
        {
            SelectedSkillsSection = SkillsSection.Sources
        };
        viewModel.SelectedSkillsSectionIndex = (int)SkillsSection.Sources;

        var source = new SkillSourceRecord
        {
            LocalName = "demo-source",
            Profile = "design-system",
            Kind = SkillSourceKind.GitRepository,
            Location = @"C:\demo-source",
            CatalogPath = "skills",
            IsEnabled = true
        };
        SeedSkillCatalogSnapshot(
            viewModel,
            Array.Empty<InstalledSkillRecord>(),
            new[] { source });
        viewModel.SkillSearchText = "demo-source";
        viewModel.SelectedEditableSkillSource = source;

        var host = CreateHost(CreateView<SkillsTabView>(viewModel.SkillsPage));
        host.Show();
        viewModel.SelectedSkillsSection = SkillsSection.Sources;

        await WaitForAsync(() => GetVisibleTextBlocks(host).Any(text =>
            text.Contains("当前来源绑定的分类“design-system”已不在分类目录中", StringComparison.Ordinal)));

        var visibleText = GetVisibleTextBlocks(host);
        Assert.Contains(visibleText, text => text.Contains(
            "扫描只会探测可用引用、可发现 Skill 与分组，并回填来源元数据；不会安装、绑定或发布。",
            StringComparison.Ordinal));
        Assert.Contains(visibleText, text => text.Contains(
            "当前来源绑定的分类“design-system”已不在分类目录中；保存其他字段时会保留原分类，只有显式改选后才会迁移。",
            StringComparison.Ordinal));
        Assert.True(viewModel.HasSkillSourceProfileValidationWarning);
        Assert.Contains("design-system", viewModel.SkillSourceProfileValidationDisplay, StringComparison.Ordinal);
    }
    private static string ProjectDiagnosticsHeader() => "\u9879\u76EE\u8BCA\u65AD";

    private static string OperationLogHeader() => "\u6267\u884C\u65E5\u5FD7";

    private static string ProjectsListTitle() => "\u9879\u76EE\u5217\u8868";

    private static string ProjectsFormTitle() => "\u9879\u76EE\u8D44\u6599";

    private static string McpCurrentScopeTitle() => "\u5F53\u524D\u8303\u56F4";

    private static string McpOverviewHeader() => "\u6982\u89C8";

    private static string McpControlCenterTitle() => "MCP \u63A7\u5236\u4E2D\u5FC3";

    private static string SettingsProfileCatalogTitle() => "Profile \u76EE\u5F55";

    private static string SettingsProfileEditorTitle() => "Profile \u7F16\u8F91\u5668";

    private static string OverviewReadinessTitle() => "\u5DF2\u6536\u53E3\u80FD\u529B";

    private static string OverviewRemainingGatesTitle() => "\u5269\u4F59\u6B63\u5F0F\u4F7F\u7528\u95E8\u69DB";

    private static string ScriptsExpertModeDescription() =>
        "\u4E13\u5BB6\u6A21\u5F0F\uFF1A\u8FD9\u91CC\u4EC5\u4FDD\u7559 Hook \u6A21\u677F\u4E0E\u5916\u90E8\u8BCA\u65AD\u811A\u672C\uFF1B" +
        "\u5168\u5C40\u521D\u59CB\u5316\u3001\u9879\u76EE Profile \u5E94\u7528\u548C MCP \u914D\u7F6E\u751F\u6210\u90FD\u5DF2\u8FC1\u5165\u7A0B\u5E8F\u5185\u90E8\u3002";

    private static Window CreateHost(Control content)
    {
        return new Window
        {
            Content = content
        };
    }

    private static T CreateView<T>(object viewModel)
        where T : Control, new()
    {
        var view = new T
        {
            DataContext = viewModel
        };

        return view;
    }

    private static void AssertNamedButton(Control view, string name)
    {
        var control = view.FindControl<Button>(name);
        Assert.NotNull(control);
        Assert.Equal(name, control.Name);
    }

    private static void AssertNamedControl<TControl>(Control view, string name)
        where TControl : Control
    {
        var control = view.FindControl<TControl>(name);
        Assert.NotNull(control);
        Assert.Equal(name, control.Name);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("Timed out waiting for the UI binding to update.");
            }

            await Task.Delay(20);
        }
    }

    private static void SeedSkillCatalogSnapshot(
        MainWindowViewModel viewModel,
        IReadOnlyList<InstalledSkillRecord> installedSkills,
        IReadOnlyList<SkillSourceRecord> sources)
    {
        var cacheSkillSnapshot = typeof(MainWindowViewModel).GetMethod(
            "CacheSkillSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(cacheSkillSnapshot);

        cacheSkillSnapshot!.Invoke(
            viewModel,
            new object[]
            {
                new SkillCatalogSnapshot(
                    new HubRootResolution(null, true, "smoke", Array.Empty<string>()),
                    installedSkills,
                    sources)
            });
    }

    private static object? InvokePrivateInstanceMethod(object target, string methodName, params object?[]? arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(target, arguments);
    }

    private static IReadOnlyList<string> GetVisibleTextBlocks(Control root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(textBlock => textBlock.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(textBlock.Text))
            .Select(textBlock => textBlock.Text!)
            .ToList();

    private static IReadOnlyList<string> GetVisibleTextBoxValues(Control root) =>
        root.GetVisualDescendants()
            .OfType<TextBox>()
            .Where(textBox => textBox.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(textBox.Text))
            .Select(textBox => textBox.Text!)
            .ToList();

    private static ListBox? FindListBoxBoundTo(Control root, object itemsSource)
    {
        return root.GetVisualDescendants()
            .OfType<ListBox>()
            .FirstOrDefault(listBox => ReferenceEquals(listBox.ItemsSource, itemsSource));
    }

}
