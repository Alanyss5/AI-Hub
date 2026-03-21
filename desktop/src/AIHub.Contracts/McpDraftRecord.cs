namespace AIHub.Contracts;

public sealed record McpDraftRecord
{
    public string Name { get; init; } = string.Empty;

    public string DraftPath { get; init; } = string.Empty;

    public string RawJson { get; init; } = "{\r\n  \"command\": \"\"\r\n}";
}
