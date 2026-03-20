using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class McpProfileListItem
{
    public McpProfileListItem(string profile, IReadOnlyList<string> serverNames, string? displayName = null)
    {
        Profile = WorkspaceProfiles.NormalizeId(profile);
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? WorkspaceProfiles.ToDisplayName(Profile)
            : displayName.Trim();
        ServerNames = serverNames;
    }

    public string Profile { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> ServerNames { get; }

    public string ServerCountDisplay => ServerNames.Count == 0 ? "\u672a\u914d\u7f6e\u670d\u52a1\u5668" : $"{ServerNames.Count} \u4e2a\u670d\u52a1\u5668";

    public string ServerNamesSummary => ServerNames.Count == 0 ? "\u7a7a\u914d\u7f6e" : string.Join("\u3001", ServerNames);
}
