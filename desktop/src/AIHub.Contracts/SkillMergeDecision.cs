namespace AIHub.Contracts;

public sealed record SkillMergeDecision(
    string RelativePath,
    SkillMergeDecisionMode Decision);
