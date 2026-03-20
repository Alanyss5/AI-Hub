namespace AIHub.Contracts;

public static class McpServerNameAliases
{
    public static string ToCanonical(string? serverName)
    {
        var normalized = serverName?.Trim() ?? string.Empty;
        return string.Equals(normalized, "coplay_mcp", StringComparison.OrdinalIgnoreCase)
            ? "coplay-mcp"
            : normalized;
    }

    public static string ToCodexKey(string? serverName)
    {
        var canonical = ToCanonical(serverName);
        return string.Equals(canonical, "coplay-mcp", StringComparison.OrdinalIgnoreCase)
            ? "coplay_mcp"
            : canonical;
    }
}
