namespace AIHub.Application.Models;

public sealed record ScriptDefinitionRecord(
    string RelativePath,
    string DisplayName,
    string Category,
    string Description,
    bool UsesHubRoot,
    bool UsesProjectPath,
    bool UsesProfile,
    bool UsesUserHome,
    bool SupportsRawArguments)
{
    public string CommandHint
    {
        get
        {
            var flags = new List<string>();

            if (UsesHubRoot)
            {
                flags.Add("HubRoot");
            }

            if (UsesProjectPath)
            {
                flags.Add("ProjectPath");
            }

            if (UsesProfile)
            {
                flags.Add("Profile");
            }

            if (UsesUserHome)
            {
                flags.Add("UserHome");
            }

            if (SupportsRawArguments)
            {
                flags.Add("自定义参数");
            }

            return flags.Count == 0 ? "无需额外参数" : string.Join(" / ", flags);
        }
    }
}
