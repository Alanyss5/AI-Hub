using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(AIHub.Application.Tests.AvaloniaHeadlessTestApp))]

namespace AIHub.Application.Tests;

public static class AvaloniaHeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<AIHub.Desktop.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
