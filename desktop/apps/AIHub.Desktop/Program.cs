using AIHub.Application.Abstractions;
using AIHub.Infrastructure;
using AIHub.Platform.Windows;
using Avalonia;

namespace AIHub.Desktop;

internal static class Program
{
    private const string SingleInstanceApplicationId = "AIHub.Desktop";

    internal static ISingleInstanceCoordinator? SingleInstanceCoordinator { get; private set; }

    internal static IDiagnosticLogService DiagnosticLogService { get; } = new FileDiagnosticLogService();

    [STAThread]
    public static void Main(string[] args)
    {
        DiagnosticLogService.RecordInfo("startup", "桌面进程启动。", Environment.ProcessPath ?? SingleInstanceApplicationId);

        try
        {
            using var singleInstanceCoordinator = CreateSingleInstanceCoordinator();
            if (singleInstanceCoordinator is not null && !singleInstanceCoordinator.IsPrimaryInstance)
            {
                singleInstanceCoordinator.TrySignalPrimaryInstance();
                DiagnosticLogService.RecordInfo("startup", "检测到第二实例启动，已唤起主实例并退出。", Environment.ProcessPath ?? SingleInstanceApplicationId);
                return;
            }

            SingleInstanceCoordinator = singleInstanceCoordinator;
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                DiagnosticLogService.RecordInfo("startup", "桌面进程已退出。", Environment.ProcessPath ?? SingleInstanceApplicationId);
            }
            finally
            {
                SingleInstanceCoordinator = null;
            }
        }
        catch (Exception exception)
        {
            DiagnosticLogService.RecordStartupFailure("Program.Main", exception);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static ISingleInstanceCoordinator? CreateSingleInstanceCoordinator()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsSingleInstanceCoordinator(SingleInstanceApplicationId)
            : null;
    }
}