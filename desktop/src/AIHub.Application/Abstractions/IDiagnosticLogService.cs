using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IDiagnosticLogService
{
    void RecordInfo(string category, string message, string? details = null);

    void RecordWarning(string category, string message, string? details = null);

    void RecordError(string category, string message, Exception? exception = null, string? details = null);

    void RecordStartupFailure(string stage, Exception exception);

    void RecordUnhandledException(string stage, Exception exception);

    Task<DiagnosticSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ExportBundleAsync(string destinationPath, string? hubRoot = null, CancellationToken cancellationToken = default);
}