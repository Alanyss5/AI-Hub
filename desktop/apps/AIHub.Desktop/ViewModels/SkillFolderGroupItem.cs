using AIHub.Application.Models;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillFolderGroupItem
{
    public SkillFolderGroupItem(
        string relativeRootPath,
        IReadOnlyList<InstalledSkillRecord> skills,
        IReadOnlyList<string> sourceProfileIds,
        IReadOnlyList<string> bindingProfileIds,
        IReadOnlyList<string> bindingDisplayTags,
        IReadOnlyList<string> containedSkillPaths)
    {
        RelativeRootPath = relativeRootPath;
        Skills = skills;
        SourceProfileIds = sourceProfileIds;
        BindingProfileIds = bindingProfileIds;
        BindingDisplayTags = bindingDisplayTags;
        ContainedSkillPaths = containedSkillPaths;
    }

    public string RelativeRootPath { get; }

    public IReadOnlyList<InstalledSkillRecord> Skills { get; }

    public IReadOnlyList<string> SourceProfileIds { get; }

    public IReadOnlyList<string> BindingProfileIds { get; }

    public IReadOnlyList<string> BindingDisplayTags { get; }

    public IReadOnlyList<string> ContainedSkillPaths { get; }

    public string DisplayName => RelativeRootPath;

    public string SkillCountDisplay => $"{Skills.Count} 个 Skills";

    public string VisibleSkillCountDisplay => $"当前筛选可见成员：{ContainedSkillPaths.Count}";

    public string ProfileSummary => BindingDisplayTags.Count == 0
        ? "未绑定"
        : string.Join(" / ", BindingDisplayTags);
}
