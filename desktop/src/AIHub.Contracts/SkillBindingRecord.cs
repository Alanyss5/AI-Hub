namespace AIHub.Contracts;

public sealed record SkillBindingRecord
{
    public string RelativePath { get; init; } = string.Empty;

    public string SourceProfile { get; init; } = WorkspaceProfiles.GlobalId;

    public IReadOnlyList<string> PublishedProfiles { get; init; } = Array.Empty<string>();
}
