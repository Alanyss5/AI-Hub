using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class McpProfileListItem
{
    public McpProfileListItem(ProfileKind profile, IReadOnlyList<string> serverNames)
    {
        Profile = profile;
        DisplayName = profile.ToDisplayName();
        ServerNames = serverNames;
    }

    public ProfileKind Profile { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> ServerNames { get; }

    public string ServerCountDisplay => ServerNames.Count == 0 ? "未配置服务器" : $"{ServerNames.Count} 个服务器";

    public string ServerNamesSummary => ServerNames.Count == 0 ? "空配置" : string.Join("、", ServerNames);
}
