using AIHub.Desktop;
using AIHub.Desktop.Services;
using AIHub.Desktop.Text;
using AIHub.Desktop.ViewModels;
using AIHub.Desktop.Views.Tabs;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AIHub.Application.Tests;

public sealed class MainWindowUiSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_Loads_Default_ViewModel_And_TabHeaders()
    {
        var text = DesktopTextCatalog.Default;
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel()
        };

        window.Show();

        var overviewTab = window.FindControl<TabItem>("OverviewTab");
        var projectsTab = window.FindControl<TabItem>("ProjectsTab");
        var skillsTab = window.FindControl<TabItem>("SkillsTab");
        var scriptsTab = window.FindControl<TabItem>("ScriptsTab");
        var mcpTab = window.FindControl<TabItem>("McpTab");
        var settingsTab = window.FindControl<TabItem>("SettingsTab");
        var refreshAllButton = window.FindControl<Button>("RefreshAllButton");

        Assert.NotNull(overviewTab);
        Assert.NotNull(projectsTab);
        Assert.NotNull(skillsTab);
        Assert.NotNull(scriptsTab);
        Assert.NotNull(mcpTab);
        Assert.NotNull(settingsTab);
        Assert.NotNull(refreshAllButton);

        Assert.Equal(text.Shell.OverviewTabHeader, overviewTab.Header);
        Assert.Equal(text.Projects.TabHeader, projectsTab.Header);
        Assert.Equal(text.Skills.TabHeader, skillsTab.Header);
        Assert.Equal(text.Scripts.TabHeader, scriptsTab.Header);
        Assert.Equal(text.Mcp.TabHeader, mcpTab.Header);
        Assert.Equal(text.Settings.TabHeader, settingsTab.Header);
        Assert.Equal(text.Shell.RefreshAllButton, refreshAllButton.Content);
    }

    [AvaloniaFact]
    public void TabViews_Expose_Stable_Named_Buttons()
    {
        var viewModel = new MainWindowViewModel();

        AssertNamedButton(CreateView<ProjectsTabView>(viewModel), "ProjectsSaveButton");
        AssertNamedButton(CreateView<SkillsTabView>(viewModel), "SkillsSaveSourceButton");
        AssertNamedButton(CreateView<SkillsTabView>(viewModel), "SkillsCheckSourceVersionsButton");
        AssertNamedButton(CreateView<SkillsTabView>(viewModel), "SkillsUpgradeSourceVersionsButton");
        AssertNamedButton(CreateView<McpTabView>(viewModel), "McpSaveManifestButton");
        AssertNamedButton(CreateView<McpTabView>(viewModel), "McpResumeSuspendedButton");
        AssertNamedButton(CreateView<SettingsTabView>(viewModel), "SettingsSaveButton");
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
    public void OverviewTabView_No_Longer_Uses_Legacy_Next_Steps_Copy()
    {
        var content = File.ReadAllText("C:\\AI-Hub\\desktop\\apps\\AIHub.Desktop\\Views\\Tabs\\OverviewTabView.axaml");
        Assert.DoesNotContain("后续重点", content, StringComparison.Ordinal);
        Assert.Contains("已收口能力", content, StringComparison.Ordinal);
        Assert.Contains("剩余正式使用门槛", content, StringComparison.Ordinal);
    }

    private static T CreateView<T>(MainWindowViewModel viewModel)
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
        var button = view.FindControl<Button>(name);
        Assert.NotNull(button);
        Assert.Equal(name, button.Name);
    }
}
