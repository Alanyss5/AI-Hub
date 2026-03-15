using System.Reflection;

namespace AIHub.Desktop.Services;

public static class DesktopBuildInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string ExecutablePath { get; } = ResolveExecutablePath();

    public static string BuildLabel { get; } = ResolveBuildLabel(ExecutablePath);

    private static string ResolveVersion()
    {
        var assembly = typeof(DesktopBuildInfo).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion!;
        }

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "0.0.0-local" : assemblyVersion;
    }

    private static string ResolveExecutablePath()
    {
        var candidate = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        var assemblyLocation = typeof(DesktopBuildInfo).Assembly.Location;
        return string.IsNullOrWhiteSpace(assemblyLocation)
            ? Path.GetFullPath(AppContext.BaseDirectory)
            : Path.GetFullPath(assemblyLocation);
    }

    private static string ResolveBuildLabel(string executablePath)
    {
        var normalizedPath = executablePath.Replace('/', '\\');
        if (normalizedPath.Contains("\\.config\\superpowers\\worktrees\\", StringComparison.OrdinalIgnoreCase))
        {
            return "Worktree 构建";
        }

        if (normalizedPath.Contains("\\AI-Hub\\desktop\\apps\\AIHub.Desktop\\bin\\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("C:\\AI-Hub\\", StringComparison.OrdinalIgnoreCase))
        {
            return "主仓库构建";
        }

        return "外部宿主";
    }
}
