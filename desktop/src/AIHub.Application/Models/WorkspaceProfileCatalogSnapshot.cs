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
    int SettingsCount)
{
    public string Id => Record.Id;

    public string DisplayName => Record.DisplayName;

    public bool IsBuiltin => Record.IsBuiltin;

    public bool IsDeletable => Record.IsDeletable;

    public int SortOrder => Record.SortOrder;

    public bool HasReferences => ProjectCount + SkillSourceCount + SkillInstallCount + SkillStateCount + SkillDirectoryCount + McpServerCount + SettingsCount > 0;

    public string UsageSummary =>
        $"项目 {ProjectCount} / 来源 {SkillSourceCount} / 安装 {SkillInstallCount} / 状态 {SkillStateCount} / 目录 {SkillDirectoryCount} / MCP {McpServerCount} / 设置 {SettingsCount}";
}
