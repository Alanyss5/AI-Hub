using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IMcpRuntimeStore
{
    Task<IReadOnlyList<McpRuntimeRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAllAsync(IReadOnlyList<McpRuntimeRecord> records, CancellationToken cancellationToken = default);
}
