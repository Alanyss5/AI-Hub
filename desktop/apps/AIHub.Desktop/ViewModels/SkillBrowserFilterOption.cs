namespace AIHub.Desktop.ViewModels;

public sealed class SkillBrowserFilterOption
{
    public SkillBrowserFilterOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }

    public string DisplayName { get; }
}
