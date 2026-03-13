using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillMergeDecisionOption
{
    public SkillMergeDecisionOption(SkillMergeDecisionMode value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public SkillMergeDecisionMode Value { get; }

    public string DisplayName { get; }
}
