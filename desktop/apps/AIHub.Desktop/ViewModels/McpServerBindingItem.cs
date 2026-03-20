namespace AIHub.Desktop.ViewModels;

public sealed class McpServerBindingItem
{
    public McpServerBindingItem(string name, string rawJson, IReadOnlyList<string> profileIds, IReadOnlyList<string> profileDisplayNames)
    {
        Name = name;
        RawJson = rawJson;
        ProfileIds = profileIds;
        ProfileDisplayNames = profileDisplayNames;
    }

    public string Name { get; }

    public string RawJson { get; }

    public IReadOnlyList<string> ProfileIds { get; }

    public IReadOnlyList<string> ProfileDisplayNames { get; }

    public string ProfileSummary => ProfileDisplayNames.Count == 0
        ? "未绑定分类"
        : string.Join("、", ProfileDisplayNames);
}
