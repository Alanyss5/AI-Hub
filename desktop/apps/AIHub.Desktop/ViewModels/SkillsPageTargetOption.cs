using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public enum SkillsPageTargetKind
{
    Global,
    Category
}

public sealed class SkillsPageTargetOption
{
    public SkillsPageTargetOption(SkillsPageTargetKind kind, string profileId, string displayName)
    {
        Kind = kind;
        ProfileId = WorkspaceProfiles.NormalizeId(profileId);
        DisplayName = displayName;
    }

    public SkillsPageTargetKind Kind { get; }

    public string ProfileId { get; }

    public string DisplayName { get; }

    public bool IsGlobal => Kind == SkillsPageTargetKind.Global;

    public override string ToString() => DisplayName;
}
