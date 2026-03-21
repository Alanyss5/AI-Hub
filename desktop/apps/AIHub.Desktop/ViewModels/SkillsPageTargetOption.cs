using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public enum SkillsPageTargetKind
{
    Global,
    Project
}

public sealed class SkillsPageTargetOption
{
    public SkillsPageTargetOption(SkillsPageTargetKind kind, string displayName, ProjectRecord? project = null)
    {
        Kind = kind;
        DisplayName = displayName;
        Project = project;
    }

    public SkillsPageTargetKind Kind { get; }

    public string DisplayName { get; }

    public ProjectRecord? Project { get; }

    public string ProfileId => Project?.Profile ?? WorkspaceProfiles.GlobalId;

    public override string ToString() => DisplayName;
}
