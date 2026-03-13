using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillVersionTrackingOption
{
    public SkillVersionTrackingOption(SkillVersionTrackingMode value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public SkillVersionTrackingMode Value { get; }

    public string DisplayName { get; }
}
