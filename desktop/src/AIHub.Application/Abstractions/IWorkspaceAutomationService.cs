using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IWorkspaceAutomationService
{
    Task<OperationResult> ApplyGlobalLinksAsync(string hubRoot, CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyProjectProfileAsync(string hubRoot, string projectPath, ProfileKind profile, CancellationToken cancellationToken = default);
}
