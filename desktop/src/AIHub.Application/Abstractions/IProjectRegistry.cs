using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IProjectRegistry
{
    Task<IReadOnlyList<ProjectRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAllAsync(IReadOnlyList<ProjectRecord> projects, CancellationToken cancellationToken = default);
}
