using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Application.Services;

internal static class RuntimeRefreshCoordinator
{
    public static async Task RefreshAsync(
        string hubRoot,
        IEnumerable<string> affectedProfiles,
        Func<string?, IProjectRegistry>? projectRegistryFactory,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory,
        IWorkspaceAutomationService? workspaceAutomationService,
        CancellationToken cancellationToken = default)
    {
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var profiles = affectedProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (profiles.Length == 0)
        {
            return;
        }

        if (workspaceAutomationService is null)
        {
            return;
        }

        await workspaceAutomationService.ApplyGlobalLinksAsync(normalizedHubRoot, cancellationToken: cancellationToken);

        if (projectRegistryFactory is null || hubSettingsStoreFactory is null)
        {
            return;
        }

        var settings = await hubSettingsStoreFactory(normalizedHubRoot).LoadAsync(cancellationToken);
        var onboardedProjectPaths = settings.OnboardedProjectPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (onboardedProjectPaths.Count == 0)
        {
            return;
        }

        var projects = await projectRegistryFactory(normalizedHubRoot).GetAllAsync(cancellationToken);
        foreach (var project in projects
                     .Where(project => profiles.Contains(WorkspaceProfiles.NormalizeId(project.Profile), StringComparer.OrdinalIgnoreCase))
                     .Where(project => onboardedProjectPaths.Contains(Path.GetFullPath(project.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                     .Where(project => Directory.Exists(project.Path)))
        {
            await workspaceAutomationService.ApplyProjectProfileAsync(
                normalizedHubRoot,
                project.Path,
                project.Profile,
                cancellationToken: cancellationToken);
        }
    }
}
