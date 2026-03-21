using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

internal static class SourceLayoutMigrationService
{
    public static readonly ISourcePathLayout DefaultLayout = new DefaultSourcePathLayout();

    public static void EnsureMigrated(
        string hubRoot,
        string personalRoot,
        IEnumerable<string> profiles,
        ISourcePathLayout? pathLayout = null)
    {
        var layout = pathLayout ?? DefaultLayout;
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var normalizedPersonalRoot = Path.GetFullPath(personalRoot);
        var normalizedProfiles = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        EnsureSourceStructure(layout.GetCompanySourceRoot(normalizedHubRoot), normalizedProfiles, layout);
        EnsureSourceStructure(layout.GetPersonalSourceRoot(normalizedPersonalRoot), normalizedProfiles, layout);

        MigrateLegacyRoot(normalizedHubRoot, layout.GetCompanySourceRoot(normalizedHubRoot), normalizedProfiles, layout);
        MigrateLegacyRoot(normalizedPersonalRoot, layout.GetPersonalSourceRoot(normalizedPersonalRoot), normalizedProfiles, layout);
    }

    private static void EnsureSourceStructure(string sourceRoot, IReadOnlyList<string> profiles, ISourcePathLayout layout)
    {
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(layout.GetSkillsLibraryRoot(sourceRoot));
        Directory.CreateDirectory(layout.GetMcpDraftsRoot(sourceRoot));
        Directory.CreateDirectory(layout.GetRegistryRoot(sourceRoot));

        foreach (var profile in profiles)
        {
            Directory.CreateDirectory(layout.GetProfileSkillsRoot(sourceRoot, profile));
            Directory.CreateDirectory(layout.GetProfileCommandsRoot(sourceRoot, profile));
            Directory.CreateDirectory(layout.GetProfileAgentsRoot(sourceRoot, profile));
            Directory.CreateDirectory(Path.GetDirectoryName(layout.GetProfileSettingsPath(sourceRoot, profile))!);
            Directory.CreateDirectory(Path.GetDirectoryName(layout.GetProfileManifestPath(sourceRoot, profile))!);
        }
    }

    private static void MigrateLegacyRoot(string legacyRoot, string sourceRoot, IReadOnlyList<string> profiles, ISourcePathLayout layout)
    {
        foreach (var profile in profiles)
        {
            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(legacyRoot, "skills", profile),
                layout.GetProfileSkillsRoot(sourceRoot, profile));
            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(legacyRoot, "claude", "commands", profile),
                layout.GetProfileCommandsRoot(sourceRoot, profile));
            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(legacyRoot, "agents", profile),
                layout.GetProfileAgentsRoot(sourceRoot, profile));
            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(legacyRoot, "claude", "agents", profile),
                layout.GetProfileAgentsRoot(sourceRoot, profile));
            CopyFileIfTargetMissing(
                Path.Combine(legacyRoot, "claude", "settings", profile + ".settings.json"),
                layout.GetProfileSettingsPath(sourceRoot, profile));
            CopyFileIfTargetMissing(
                Path.Combine(legacyRoot, "mcp", "manifest", profile + ".json"),
                layout.GetProfileManifestPath(sourceRoot, profile));
        }

        CopyFileIfTargetMissing(
            Path.Combine(legacyRoot, "skills", "sources.json"),
            layout.GetSkillSourcesPath(sourceRoot));
        CopyFileIfTargetMissing(
            Path.Combine(legacyRoot, "config", "skills-installs.json"),
            layout.GetSkillInstallsPath(sourceRoot));
        CopyFileIfTargetMissing(
            Path.Combine(legacyRoot, "config", "skills-state.json"),
            layout.GetSkillStatesPath(sourceRoot));
        CopyFileIfTargetMissing(
            Path.Combine(legacyRoot, "config", "profile-catalog.json"),
            layout.GetProfileCatalogPath(sourceRoot));
    }

    private static void CopyFileIfTargetMissing(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(targetPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: false);
    }

    private static void CopyDirectoryContentsIfTargetMissing(string sourceRoot, string targetRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        if (Directory.Exists(targetRoot)
            && (Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories).Any()
                || Directory.EnumerateDirectories(targetRoot, "*", SearchOption.AllDirectories).Any()))
        {
            return;
        }

        CopyDirectoryContents(sourceRoot, targetRoot);
    }

    private static void CopyDirectoryContents(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, filePath);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }
}
