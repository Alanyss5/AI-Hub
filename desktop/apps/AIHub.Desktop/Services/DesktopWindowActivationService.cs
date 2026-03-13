using Avalonia.Controls;

namespace AIHub.Desktop.Services;

public static class DesktopWindowActivationService
{
    public static void RestoreMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}