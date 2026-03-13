namespace AIHub.Core;

public static class HubLayout
{
    public const string HubMarkerFileName = "hub.json";

    public static IReadOnlyList<string> RequiredDirectories { get; } = new[]
    {
        "claude",
        "docs",
        "mcp",
        "skills"
    };
}
