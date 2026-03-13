namespace AIHub.Contracts;

public sealed record McpProfileRecord(
    ProfileKind Profile,
    string ManifestPath,
    string RawJson,
    IReadOnlyList<string> ServerNames,
    IReadOnlyList<McpGeneratedClientConfig> GeneratedClients);
