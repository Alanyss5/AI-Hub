namespace AIHub.Application.Models;

public sealed record DiagnosticSnapshot
{
    public string DiagnosticsRoot { get; init; } = string.Empty;

    public DateTimeOffset? LastStartupFailureAt { get; init; }

    public string LatestStartupFailureSummary { get; init; } = string.Empty;

    public string LatestStartupFailureDetails { get; init; } = string.Empty;

    public DateTimeOffset? LastUnhandledExceptionAt { get; init; }

    public string LatestUnhandledExceptionSummary { get; init; } = string.Empty;

    public string LatestUnhandledExceptionDetails { get; init; } = string.Empty;

    public DateTimeOffset? LastExportedAt { get; init; }

    public string LastExportPath { get; init; } = string.Empty;
}