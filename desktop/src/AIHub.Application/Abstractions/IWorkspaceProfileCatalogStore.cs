using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IWorkspaceProfileCatalogStore
{
    Task<IReadOnlyList<WorkspaceProfileRecord>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<WorkspaceProfileRecord> profiles, CancellationToken cancellationToken = default);
}
