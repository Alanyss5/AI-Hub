using AIHub.Desktop.Services;
using AIHub.Platform.Windows;
using Avalonia.Threading;

namespace AIHub.Desktop;

public partial class App
{
    private CancellationTokenSource? _singleInstanceActivationCts;
    private Task? _singleInstanceActivationTask;

    private void StartSingleInstanceActivationListener(MainWindow mainWindow)
    {
        var coordinator = Program.SingleInstanceCoordinator;
        if (coordinator is null || !coordinator.IsPrimaryInstance)
        {
            return;
        }

        _singleInstanceActivationCts = new CancellationTokenSource();
        var cancellationToken = _singleInstanceActivationCts.Token;

        _singleInstanceActivationTask = Task.Run(async () =>
        {
            try
            {
                await coordinator.WaitForActivationAsync(() =>
                {
                    Dispatcher.UIThread.Post(() => DesktopWindowActivationService.RestoreMainWindow(mainWindow));
                    return Task.CompletedTask;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }, cancellationToken);
    }

    private void StopSingleInstanceActivationListener()
    {
        _singleInstanceActivationCts?.Cancel();
        _singleInstanceActivationCts?.Dispose();
        _singleInstanceActivationCts = null;
        _singleInstanceActivationTask = null;
    }
}