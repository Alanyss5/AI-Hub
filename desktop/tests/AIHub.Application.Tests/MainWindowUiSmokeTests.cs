using AIHub.Contracts;
using AIHub.Desktop;
using AIHub.Desktop.Services;
using AIHub.Desktop.Text;
using AIHub.Desktop.ViewModels;
using AIHub.Desktop.Views.Tabs;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

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
        AssertNamedButton(CreateView<McpTabView>(viewModel.McpPage), "McpSwitchProjectScopeButton");
        AssertNamedButton(CreateView<McpTabView>(viewModel.McpPage), "McpSwitchGlobalScopeButton");
        AssertNamedControl<ComboBox>(CreateView<McpTabView>(viewModel.McpPage), "McpContextProjectComboBox");
        AssertNamedButton(CreateView<SettingsTabView>(viewModel), "SettingsSaveButton");
    }

    [AvaloniaFact]
    public async Task Skills_Target_Selector_And_Mcp_Project_Scope_Track_Their_Own_Context()
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
            Assert.Contains(viewModel.SkillsPageContext.TargetOptions, item => ReferenceEquals(item.Project, selectedProject));
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
    public void Context_Bars_Keep_Project_Selector_Visible_In_Skills_And_Mcp()
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
        var skills = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\SkillsTabView.axaml");
        var mcp = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\McpTabView.axaml");
        var settings = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\SettingsTabView.axaml");
        var scripts = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\ScriptsTabView.axaml");
        var overview = File.ReadAllText(@"C:\AI-Hub\desktop\apps\AIHub.Desktop\Views\Tabs\OverviewTabView.axaml");

        Assert.DoesNotContain("WorkspaceSetCurrentProjectButton", workspace, StringComparison.Ordinal);
        Assert.Contains(ProjectDiagnosticsHeader(), workspace, StringComparison.Ordinal);
        Assert.Contains(OperationLogHeader(), workspace, StringComparison.Ordinal);

        Assert.Contains(ProjectsListTitle(), projects, StringComparison.Ordinal);
        Assert.Contains(ProjectsFormTitle(), projects, StringComparison.Ordinal);

        Assert.Contains(SkillsCurrentContextTitle(), skills, StringComparison.Ordinal);
        Assert.Contains(SkillsBrowseTabHeader(), skills, StringComparison.Ordinal);
        Assert.Contains(InstalledSkillsTitle(), skills, StringComparison.Ordinal);
        Assert.DoesNotContain("Current Context", skills, StringComparison.Ordinal);

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
        Assert.Equal("当前查看全局 Skills", viewModel.SkillsPageContext.CurrentContextDisplay);
        Assert.Equal("保存后将显示为未绑定", viewModel.PendingSkillBindingSaveTargetDisplay);
        Assert.Equal("未绑定", viewModel.PendingSkillBindingSummaryDisplay);
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

    private static string ProjectDiagnosticsHeader() => "\u9879\u76EE\u8BCA\u65AD";

    private static string OperationLogHeader() => "\u6267\u884C\u65E5\u5FD7";

    private static string ProjectsListTitle() => "\u9879\u76EE\u5217\u8868";

    private static string ProjectsFormTitle() => "\u9879\u76EE\u8D44\u6599";

    private static string SkillsCurrentContextTitle() => "\u5F53\u524D\u64CD\u4F5C\u5BF9\u8C61";

    private static string SkillsBrowseTabHeader() => "\u6D4F\u89C8";

    private static string InstalledSkillsTitle() => "\u5DF2\u5B89\u88C5 Skills";

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
}
