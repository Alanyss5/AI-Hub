using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class PowerShellScriptExecutionService : IScriptExecutionService
{
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public PowerShellScriptExecutionService(IDiagnosticLogService? diagnosticLogService = null)
    {
        _diagnosticLogService = diagnosticLogService;
    }

    public Task<OperationResult> RunAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken = default)
    {
        return PowerShellScriptRunner.RunAsync(
            scriptPath,
            arguments,
            successMessage,
            failureMessage,
            cancellationToken,
            _diagnosticLogService);
    }
}