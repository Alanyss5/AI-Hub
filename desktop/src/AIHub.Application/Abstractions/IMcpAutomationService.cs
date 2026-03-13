using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IMcpAutomationService
{
    Task<OperationResult> GenerateConfigsAsync(string hubRoot, CancellationToken cancellationToken = default);
}
