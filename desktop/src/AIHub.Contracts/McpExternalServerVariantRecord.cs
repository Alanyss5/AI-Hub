namespace AIHub.Contracts;

public sealed record McpExternalServerVariantRecord(
    McpClientKind Client,
    string FilePath,
    McpServerDefinitionRecord Definition);
