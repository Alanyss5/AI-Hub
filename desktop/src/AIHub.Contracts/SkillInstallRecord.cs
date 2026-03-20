namespace AIHub.Contracts;

public sealed record SkillInstallRecord
{
    public string Name { get; init; } = string.Empty;

    public string Profile { get; init; } = WorkspaceProfiles.GlobalId;

    public string InstalledRelativePath { get; init; } = string.Empty;

    public string? SourceLocalName { get; init; }

    public string? SourceProfile { get; init; }

    public string? SourceSkillPath { get; init; }

    public SkillCustomizationMode CustomizationMode { get; init; } = SkillCustomizationMode.Local;
}
