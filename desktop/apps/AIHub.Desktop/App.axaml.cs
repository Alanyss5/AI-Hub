using AIHub.Application.Abstractions;
using AIHub.Application.Services;
using AIHub.Desktop.Services;
using AIHub.Desktop.ViewModels;
using AIHub.Infrastructure;
using AIHub.Platform.Windows;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AIHub.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var diagnosticsService = Program.DiagnosticLogService;
                RegisterGlobalExceptionHandlers(diagnosticsService, desktop);

                var rootLocator = new HubRootLocator();
                IPlatformCapabilitiesService platformCapabilitiesService = new WindowsPlatformCapabilitiesService();
                IPlatformLinkService platformLinkService = new WindowsPlatformLinkService();
                var dashboardService = new HubDashboardService(
                    platformCapabilitiesService,
                    usesInternalWorkspaceAutomation: true,
                    usesInternalMcpAutomation: true,
                    skillsVersionChannelReady: true);
                var workspaceAutomationService = new NativeWorkspaceAutomationService(platformLinkService, platformCapabilitiesService, diagnosticsService);
                var mcpAutomationService = new NativeMcpAutomationService();
                var mcpClientConfigService = new McpClientConfigService();
                var notificationService = new WindowsNotificationService(diagnosticsService);
                var scriptExecutionService = new PowerShellScriptExecutionService(diagnosticsService);

                Func<string?, JsonProjectRegistry> projectRegistryFactory = root => new JsonProjectRegistry(root, diagnosticsService);
                Func<string?, JsonHubSettingsStore> hubSettingsStoreFactory = root => new JsonHubSettingsStore(root, diagnosticsService);
                Func<string?, JsonWorkspaceProfileCatalogStore> profileCatalogStoreFactory = root => new JsonWorkspaceProfileCatalogStore(root);
                Func<string?, JsonMcpProfileStore> mcpProfileStoreFactory = root => new JsonMcpProfileStore(root, diagnosticsService);
                Func<string?, JsonMcpRuntimeStore> mcpRuntimeStoreFactory = root => new JsonMcpRuntimeStore(root, diagnosticsService);

                var workspaceControlService = new WorkspaceControlService(
                    rootLocator,
                    projectRegistryFactory,
                    hubSettingsStoreFactory,
                    profileCatalogStoreFactory,
                    workspaceAutomationService,
                    dashboardService);

                var workspaceProfileService = new WorkspaceProfileService(
                    rootLocator,
                    profileCatalogStoreFactory,
                    projectRegistryFactory,
                    hubSettingsStoreFactory,
                    mcpProfileStoreFactory);

                var mcpProcessController = new LocalMcpProcessController(() =>
                {
                    var resolution = rootLocator.ResolveAsync().GetAwaiter().GetResult();
                    return resolution.RootPath;
                }, diagnosticsService);

                var mcpControlService = new McpControlService(
                    rootLocator,
                    mcpProfileStoreFactory,
                    mcpRuntimeStoreFactory,
                    mcpProcessController,
                    mcpAutomationService,
                    hubSettingsStoreFactory,
                    mcpClientConfigService);

                var skillsCatalogService = new SkillsCatalogService(rootLocator, hubSettingsStoreFactory);
                var scriptCenterService = new ScriptCenterService(rootLocator, scriptExecutionService);

                var mainWindow = new MainWindow();
                var fileDialogService = new AvaloniaFileDialogService(mainWindow);
                var viewModel = new MainWindowViewModel(
                    workspaceControlService,
                    mcpControlService,
                    skillsCatalogService,
                    scriptCenterService,
                    fileDialogService,
                    workspaceProfileService);
                viewModel.NotificationService = notificationService;
                viewModel.DiagnosticLogService = diagnosticsService;

                mainWindow.DataContext = viewModel;

                desktop.MainWindow = mainWindow;
                InitializeDesktopShell(desktop, mainWindow, viewModel);
                StartSingleInstanceActivationListener(mainWindow);
                _ = viewModel.InitializeAsync();
            }
        }
        catch (Exception exception)
        {
            Program.DiagnosticLogService.RecordStartupFailure("App.OnFrameworkInitializationCompleted", exception);
            throw;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
