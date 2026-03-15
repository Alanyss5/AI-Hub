namespace AIHub.Contracts;

public sealed record WorkspaceOnboardingCandidate(
    string Id,
    WorkspaceOnboardingResourceKind ResourceKind,
    string DisplayName,
    string SourcePath,
    string SourceDetails,
    string CompanyDestinationPath,
    string PrivateDestinationPath,
    bool CompanyDestinationExists,
    bool PrivateDestinationExists,
    WorkspaceImportTargetKind SuggestedTarget = WorkspaceImportTargetKind.AIHub);
