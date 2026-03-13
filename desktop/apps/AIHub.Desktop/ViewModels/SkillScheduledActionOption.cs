using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillScheduledActionOption
{
    public SkillScheduledActionOption(SkillScheduledUpdateAction value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public SkillScheduledUpdateAction Value { get; }

    public string DisplayName { get; }
}
