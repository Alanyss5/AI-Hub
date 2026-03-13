namespace AIHub.Contracts;

public sealed record SkillMergeFileEntry(
    string RelativePath,
    SkillMergeFileStatus Status,
    SkillMergeDecisionMode SuggestedDecision,
    string Summary,
    bool ExistsInBaseline,
    bool ExistsInSource,
    bool ExistsInInstalled);
