namespace AIHub.Contracts;

public sealed record WorkspaceOnboardingPreview(
    WorkspaceScope Scope,
    ProfileKind Profile,
    string? ProjectPath,
    bool IsFirstRun,
    bool RequiresDecision,
    IReadOnlyList<WorkspaceOnboardingCandidate> Candidates,
    string Summary);
