namespace AIHub.Contracts;

public sealed record WorkspaceOnboardingPreview(
    WorkspaceScope Scope,
    string Profile,
    string? ProjectPath,
    bool IsFirstRun,
    bool RequiresDecision,
    IReadOnlyList<WorkspaceOnboardingCandidate> Candidates,
    string Summary);
