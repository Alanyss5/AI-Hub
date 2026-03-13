namespace AIHub.Contracts;

public sealed record McpClientConfigStatusRecord(
    McpClientKind Client,
    string FilePath,
    bool IsSupported,
    bool Exists,
    bool InSync,
    IReadOnlyList<string> ManagedServerNames,
    IReadOnlyList<string> ExternalServerNames,
    string Summary);
