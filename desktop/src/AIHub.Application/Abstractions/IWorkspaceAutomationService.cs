using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IWorkspaceAutomationService
{
    Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(string hubRoot, CancellationToken cancellationToken = default);

    Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
        string hubRoot,
        string projectPath,
        string profile,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyGlobalLinksAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyProjectProfileAsync(
        string hubRoot,
        string projectPath,
        string profile,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default);
}
