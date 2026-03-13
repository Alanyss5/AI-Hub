namespace AIHub.Contracts;

public sealed record McpValidationSnapshot(
    WorkspaceScope Scope,
    ProfileKind Profile,
    string? ProjectPath,
    IReadOnlyList<McpClientConfigStatusRecord> ClientStatuses,
    IReadOnlyList<McpValidationIssueRecord> Issues,
    IReadOnlyList<McpExternalServerPreviewRecord> ExternalServers)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == McpValidationSeverity.Error);
}
