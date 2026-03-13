using AIHub.Application.Abstractions;
using Avalonia.Controls.ApplicationLifetimes;

namespace AIHub.Desktop;

public partial class App
{
    private bool _globalExceptionHandlersRegistered;

    private void RegisterGlobalExceptionHandlers(IDiagnosticLogService diagnosticLogService, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_globalExceptionHandlersRegistered)
        {
            return;
        }

        _globalExceptionHandlersRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                diagnosticLogService.RecordUnhandledException("AppDomain.CurrentDomain", exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            diagnosticLogService.RecordUnhandledException("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        desktop.Exit += (_, _) => diagnosticLogService.RecordInfo("lifecycle", "桌面应用生命周期结束。");
    }
}