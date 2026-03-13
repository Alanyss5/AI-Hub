namespace AIHub.Desktop.ViewModels;

public sealed class SkillScheduleIntervalOption
{
    public SkillScheduleIntervalOption(int? value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public int? Value { get; }

    public string DisplayName { get; }
}
