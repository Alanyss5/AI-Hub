namespace AIHub.Contracts;

public enum SkillMergeFileStatus
{
    SourceOnly = 0,
    LocalOnly = 1,
    SourceChanged = 2,
    SourceDeleted = 3,
    Conflict = 4
}
