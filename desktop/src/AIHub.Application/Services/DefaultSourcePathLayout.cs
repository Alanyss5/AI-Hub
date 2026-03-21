using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Application.Services;

internal sealed class DefaultSourcePathLayout : ISourcePathLayout
{
    public string GetCompanySourceRoot(string hubRoot)
    {
        return Path.Combine(Path.GetFullPath(hubRoot), "source");
    }

    public string GetPersonalSourceRoot(string personalRoot)
    {
        return Path.Combine(Path.GetFullPath(personalRoot), "source");
    }

    public string GetProfileRoot(string sourceRoot, string profile)
    {
        return Path.Combine(Path.GetFullPath(sourceRoot), "profiles", WorkspaceProfiles.NormalizeId(profile));
    }

    public string GetProfileSkillsRoot(string sourceRoot, string profile)
    {
        return Path.Combine(GetProfileRoot(sourceRoot, profile), "skills");
    }

    public string GetProfileCommandsRoot(string sourceRoot, string profile)
    {
        return Path.Combine(GetProfileRoot(sourceRoot, profile), "claude", "commands");
    }

    public string GetProfileAgentsRoot(string sourceRoot, string profile)
    {
        return Path.Combine(GetProfileRoot(sourceRoot, profile), "claude", "agents");
    }

    public string GetProfileSettingsPath(string sourceRoot, string profile)
    {
        return Path.Combine(GetProfileRoot(sourceRoot, profile), "claude", "settings.json");
    }

    public string GetProfileManifestPath(string sourceRoot, string profile)
    {
        return Path.Combine(GetProfileRoot(sourceRoot, profile), "mcp", "manifest.json");
    }

    public string GetSkillsLibraryRoot(string sourceRoot)
    {
        return Path.Combine(Path.GetFullPath(sourceRoot), "library", "skills");
    }

    public string GetSkillLibraryDirectory(string sourceRoot, string relativePath)
    {
        return Path.Combine(
            GetSkillsLibraryRoot(sourceRoot),
            NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));
    }

    public string GetMcpDraftsRoot(string sourceRoot)
    {
        return Path.Combine(Path.GetFullPath(sourceRoot), "library", "mcp-drafts");
    }

    public string GetMcpDraftPath(string sourceRoot, string draftId)
    {
        return Path.Combine(GetMcpDraftsRoot(sourceRoot), NormalizeDraftId(draftId) + ".json");
    }

    public string GetRegistryRoot(string sourceRoot)
    {
        return Path.Combine(Path.GetFullPath(sourceRoot), "registry");
    }

    public string GetSkillSourcesPath(string sourceRoot)
    {
        return Path.Combine(GetRegistryRoot(sourceRoot), "skills-sources.json");
    }

    public string GetSkillInstallsPath(string sourceRoot)
    {
        return Path.Combine(GetRegistryRoot(sourceRoot), "skills-installs.json");
    }

    public string GetSkillStatesPath(string sourceRoot)
    {
        return Path.Combine(GetRegistryRoot(sourceRoot), "skills-state.json");
    }

    public string GetProfileCatalogPath(string sourceRoot)
    {
        return Path.Combine(GetRegistryRoot(sourceRoot), "profiles.json");
    }

    private static string NormalizeRelativePath(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        return rawValue
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/')
            .TrimEnd('/');
    }

    private static string NormalizeDraftId(string? rawValue)
    {
        var normalized = NormalizeRelativePath(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "draft";
        }

        return normalized.Replace('/', '-');
    }
}
