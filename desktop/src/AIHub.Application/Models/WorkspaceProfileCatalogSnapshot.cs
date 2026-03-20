using AIHub.Contracts;

namespace AIHub.Application.Models;

public sealed record WorkspaceProfileCatalogSnapshot(
    HubRootResolution Resolution,
    IReadOnlyList<WorkspaceProfileDescriptor> Profiles);

public sealed record WorkspaceProfileDescriptor(
    WorkspaceProfileRecord Record,
    int ProjectCount,
    int SkillSourceCount,
    int SkillInstallCount,
    int SkillStateCount,
    int SkillDirectoryCount,
    int McpServerCount,
    int SettingsCount,
    int CommandAssetCount,
    int AgentAssetCount)
{
    public string Id => Record.Id;

    public string DisplayName => Record.DisplayName;

    public bool IsBuiltin => Record.IsBuiltin;

    public bool IsDeletable => Record.IsDeletable;

    public int SortOrder => Record.SortOrder;

    public bool HasReferences => ProjectCount + SkillSourceCount + SkillInstallCount + SkillStateCount + SkillDirectoryCount + McpServerCount + SettingsCount + CommandAssetCount + AgentAssetCount > 0;

    public string UsageSummary =>
        $"\u9879\u76ee {ProjectCount} / \u6765\u6e90 {SkillSourceCount} / \u5b89\u88c5 {SkillInstallCount} / \u72b6\u6001 {SkillStateCount} / \u76ee\u5f55 {SkillDirectoryCount} / MCP {McpServerCount} / \u8bbe\u7f6e {SettingsCount} / commands {CommandAssetCount} / agents {AgentAssetCount}";
}
