using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IMcpEffectiveConfigReader
{
    Task<IReadOnlyDictionary<string, McpServerDefinitionRecord>> GetEffectiveServersAsync(
        string hubRoot,
        string profile,
        CancellationToken cancellationToken = default);
}
