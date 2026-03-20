using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IMcpProfileStore
{
    Task<IReadOnlyList<McpProfileRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveManifestAsync(string profile, string rawJson, CancellationToken cancellationToken = default);
}
