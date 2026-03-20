namespace AIHub.Contracts;

public sealed record McpProfileRecord(
    string Profile,
    string ProfileDisplayName,
    string ManifestPath,
    string RawJson,
    IReadOnlyList<string> ServerNames,
    IReadOnlyList<McpGeneratedClientConfig> GeneratedClients);
