using System.ComponentModel;
using System.Threading;
using AIHub.Desktop.Services;
using AIHub.Desktop.Text;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AIHub.Desktop;

public partial class App
{
    private static readonly DesktopTextCatalog Text = DesktopTextCatalog.Default;
    private readonly SemaphoreSlim _maintenanceGate = new(1, 1);
    private TrayIcon? _trayIcon;
    private DispatcherTimer? _maintenanceTimer;
    private bool _exitRequested;
    private ViewModels.MainWindowViewModel? _trayViewModel;
    private NativeMenuItem? _runtimeSummaryItem;
    private NativeMenuItem? _alertSummaryItem;

    private void InitializeDesktopShell(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        ViewModels.MainWindowViewModel viewModel)
    {
        mainWindow.Closing += (_, args) =>
        {
            if (_exitRequested)
            {
                return;
            }

            args.Cancel = true;
            mainWindow.Hide();
        };

        CreateTrayIcon(desktop, mainWindow, viewModel);
        StartMaintenanceTimer(viewModel);
        desktop.Exit += (_, _) => DisposeDesktopShell();
    }

    private void CreateTrayIcon(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        ViewModels.MainWindowViewModel viewModel)
    {
        try
        {
            var iconStream = AssetLoader.Open(new Uri("avares://AIHub.Desktop/Assets/aihub-tray.ico"));
            var menu = new NativeMenu();

            _runtimeSummaryItem = new NativeMenuItem(viewModel.TrayRuntimeSummaryDisplay)
            {
                IsEnabled = false
            };
            _alertSummaryItem = new NativeMenuItem(viewModel.TrayAlertSummaryDisplay)
            {
                IsEnabled = false
            };
            menu.Add(_runtimeSummaryItem);
            menu.Add(_alertSummaryItem);
            menu.Add(new NativeMenuItemSeparator());

            AddMenuItem(menu, Text.Shell.TrayShowWindowMenu, () =>
            {
                DesktopWindowActivationService.RestoreMainWindow(mainWindow);
                return Task.CompletedTask;
            });
            AddMenuItem(menu, Text.Shell.TrayRefreshStatusMenu, viewModel.RefreshFromTrayAsync);
            AddMenuItem(menu, Text.Shell.TrayStartAllMcpMenu, viewModel.StartAllManagedProcessesFromTrayAsync);
            AddMenuItem(menu, Text.Shell.TrayStopAllMcpMenu, viewModel.StopAllManagedProcessesFromTrayAsync);
            AddMenuItem(menu, Text.Shell.TrayMaintainMenu, viewModel.MaintainManagedProcessesFromTrayAsync);
            AddMenuItem(menu, "恢复被监督暂停的 MCP", viewModel.ResumeSuspendedManagedProcessesFromTrayAsync);
            menu.Add(new NativeMenuItemSeparator());
            AddMenuItem(menu, Text.Shell.TrayExitMenu, () =>
            {
                _exitRequested = true;
                _trayIcon!.IsVisible = false;
                desktop.Shutdown();
                return Task.CompletedTask;
            });

            _trayViewModel = viewModel;
            _trayViewModel.PropertyChanged += OnTrayViewModelPropertyChanged;

            _trayIcon = new TrayIcon
            {
                ToolTipText = viewModel.TrayToolTipText,
                Icon = new WindowIcon(iconStream),
                Menu = menu,
                IsVisible = true
            };

            UpdateTrayStatus(viewModel);
        }
        catch
        {
            _trayIcon = null;
        }
    }

    private void StartMaintenanceTimer(ViewModels.MainWindowViewModel viewModel)
    {
        _maintenanceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };

        _maintenanceTimer.Tick += async (_, _) => await RunMaintenanceCycleAsync(viewModel);
        _maintenanceTimer.Start();
    }

    private async Task RunMaintenanceCycleAsync(ViewModels.MainWindowViewModel viewModel)
    {
        if (!await _maintenanceGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            await viewModel.RunBackgroundMaintenanceCycleAsync();
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    private void OnTrayViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName is nameof(ViewModels.MainWindowViewModel.TrayToolTipText)
            or nameof(ViewModels.MainWindowViewModel.TrayRuntimeSummaryDisplay)
            or nameof(ViewModels.MainWindowViewModel.TrayAlertSummaryDisplay)
            or nameof(ViewModels.MainWindowViewModel.McpRuntimeSummaryDisplay))
        {
            Dispatcher.UIThread.Post(() => UpdateTrayStatus(viewModel));
        }
    }

    private void UpdateTrayStatus(ViewModels.MainWindowViewModel viewModel)
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.ToolTipText = viewModel.TrayToolTipText;

        if (_runtimeSummaryItem is not null)
        {
            _runtimeSummaryItem.Header = viewModel.TrayRuntimeSummaryDisplay;
        }

        if (_alertSummaryItem is not null)
        {
            _alertSummaryItem.Header = viewModel.TrayAlertSummaryDisplay;
        }
    }

    private static void AddMenuItem(NativeMenu menu, string title, Func<Task> action)
    {
        var item = new NativeMenuItem(title);
        item.Click += async (_, _) => await action();
        menu.Add(item);
    }

    private void DisposeDesktopShell()
    {
        StopSingleInstanceActivationListener();

        _maintenanceTimer?.Stop();
        _maintenanceTimer = null;

        if (_trayViewModel is not null)
        {
            _trayViewModel.PropertyChanged -= OnTrayViewModelPropertyChanged;
            _trayViewModel = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _runtimeSummaryItem = null;
        _alertSummaryItem = null;
    }
}
