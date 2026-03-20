namespace AIHub.Contracts;

public sealed record SkillMergePreview(
    string Profile,
    string RelativePath,
    string SourceDisplayName,
    string SourceReference,
    IReadOnlyList<SkillMergeFileEntry> Files)
{
    public bool HasChanges => Files.Count > 0;
}
