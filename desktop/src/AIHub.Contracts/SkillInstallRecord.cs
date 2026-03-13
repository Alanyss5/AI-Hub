namespace AIHub.Contracts;

public sealed record SkillInstallRecord
{
    public string Name { get; init; } = string.Empty;

    public ProfileKind Profile { get; init; } = ProfileKind.Global;

    public string InstalledRelativePath { get; init; } = string.Empty;

    public string? SourceLocalName { get; init; }

    public ProfileKind? SourceProfile { get; init; }

    public string? SourceSkillPath { get; init; }

    public SkillCustomizationMode CustomizationMode { get; init; } = SkillCustomizationMode.Local;
}