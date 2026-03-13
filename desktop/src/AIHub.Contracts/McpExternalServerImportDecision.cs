namespace AIHub.Contracts;

public sealed record McpExternalServerImportDecision(
    string Name,
    McpClientKind SourceClient);
