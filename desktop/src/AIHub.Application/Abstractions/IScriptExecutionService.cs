using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IScriptExecutionService
{
    Task<OperationResult> RunAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken = default);
}
