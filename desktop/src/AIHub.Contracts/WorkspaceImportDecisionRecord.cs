namespace AIHub.Contracts;

public sealed record WorkspaceImportDecisionRecord(
    string CandidateId,
    WorkspaceImportTargetKind Target);
