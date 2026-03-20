using AIHub.Application.Models;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillFolderGroupItem
{
    public SkillFolderGroupItem(
        string relativeRootPath,
        IReadOnlyList<InstalledSkillRecord> skills,
        IReadOnlyList<string> profileIds,
        IReadOnlyList<string> profileDisplayNames,
        IReadOnlyList<string> containedSkillPaths)
    {
        RelativeRootPath = relativeRootPath;
        Skills = skills;
        ProfileIds = profileIds;
        ProfileDisplayNames = profileDisplayNames;
        ContainedSkillPaths = containedSkillPaths;
    }

    public string RelativeRootPath { get; }

    public IReadOnlyList<InstalledSkillRecord> Skills { get; }

    public IReadOnlyList<string> ProfileIds { get; }

    public IReadOnlyList<string> ProfileDisplayNames { get; }

    public IReadOnlyList<string> ContainedSkillPaths { get; }

    public string DisplayName => RelativeRootPath;

    public string SkillCountDisplay => $"{ContainedSkillPaths.Count} 个 Skills";

    public string ProfileSummary => ProfileDisplayNames.Count == 0
        ? "未绑定分类"
        : string.Join("、", ProfileDisplayNames);
}
