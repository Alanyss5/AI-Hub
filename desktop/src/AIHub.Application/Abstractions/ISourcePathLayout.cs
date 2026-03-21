namespace AIHub.Application.Abstractions;

public interface ISourcePathLayout
{
    string GetCompanySourceRoot(string hubRoot);

    string GetPersonalSourceRoot(string personalRoot);

    string GetProfileRoot(string sourceRoot, string profile);

    string GetProfileSkillsRoot(string sourceRoot, string profile);

    string GetProfileCommandsRoot(string sourceRoot, string profile);

    string GetProfileAgentsRoot(string sourceRoot, string profile);

    string GetProfileSettingsPath(string sourceRoot, string profile);

    string GetProfileManifestPath(string sourceRoot, string profile);

    string GetSkillsLibraryRoot(string sourceRoot);

    string GetSkillLibraryDirectory(string sourceRoot, string relativePath);

    string GetMcpDraftsRoot(string sourceRoot);

    string GetMcpDraftPath(string sourceRoot, string draftId);

    string GetRegistryRoot(string sourceRoot);

    string GetSkillSourcesPath(string sourceRoot);

    string GetSkillInstallsPath(string sourceRoot);

    string GetSkillStatesPath(string sourceRoot);

    string GetProfileCatalogPath(string sourceRoot);
}
