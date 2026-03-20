using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IMcpClientConfigService
{
    Task<McpValidationSnapshot> InspectAsync(
        string hubRoot,
        WorkspaceScope scope,
        string profile,
        string? projectPath,
        IReadOnlyDictionary<string, McpServerDefinitionRecord> managedServers,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SyncAsync(
        string hubRoot,
        WorkspaceScope scope,
        string profile,
        string? projectPath,
        IReadOnlyDictionary<string, McpServerDefinitionRecord> managedServers,
        CancellationToken cancellationToken = default);
}
