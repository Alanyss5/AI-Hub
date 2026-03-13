namespace AIHub.Contracts;

public sealed record McpValidationIssueRecord(
    McpValidationSeverity Severity,
    string Summary,
    string? Details = null,
    string? FilePath = null,
    string? ServerName = null);
