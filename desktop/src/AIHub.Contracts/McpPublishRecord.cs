namespace AIHub.Contracts;

public sealed record McpPublishRecord
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> PublishedProfiles { get; init; } = Array.Empty<string>();
}
