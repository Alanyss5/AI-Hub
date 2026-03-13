using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IMcpProcessController
{
    Task<McpRuntimeRecord> RefreshAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default);

    Task<McpProcessCommandResult> StartAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default);

    Task<McpProcessCommandResult> StopAsync(McpRuntimeRecord record, CancellationToken cancellationToken = default);
}
