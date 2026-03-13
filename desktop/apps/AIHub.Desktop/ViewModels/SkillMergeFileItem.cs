using AIHub.Contracts;
using AIHub.Desktop.Text;

namespace AIHub.Desktop.ViewModels;

public sealed class SkillMergeFileItem : ObservableObject
{
    private static readonly DesktopTextCatalog Text = DesktopTextCatalog.Default;
    private SkillMergeDecisionOption? _selectedDecisionOption;

    public SkillMergeFileItem(SkillMergeFileEntry entry, IReadOnlyList<SkillMergeDecisionOption> options)
    {
        Entry = entry;
        DecisionOptions = options;
        _selectedDecisionOption = options.FirstOrDefault(option => option.Value == entry.SuggestedDecision) ?? options.FirstOrDefault();
    }

    public SkillMergeFileEntry Entry { get; }

    public IReadOnlyList<SkillMergeDecisionOption> DecisionOptions { get; }

    public string RelativePath => Entry.RelativePath;

    public string StatusDisplay => Entry.Status switch
    {
        SkillMergeFileStatus.SourceOnly => Text.Skills.MergeStatusSourceOnly,
        SkillMergeFileStatus.LocalOnly => Text.Skills.MergeStatusLocalOnly,
        SkillMergeFileStatus.SourceChanged => Text.Skills.MergeStatusSourceChanged,
        SkillMergeFileStatus.SourceDeleted => Text.Skills.MergeStatusSourceDeleted,
        SkillMergeFileStatus.Conflict => Text.Skills.MergeStatusConflict,
        _ => Text.Skills.MergeStatusPending
    };

    public string Summary => Entry.Summary;

    public SkillMergeDecisionOption? SelectedDecisionOption
    {
        get => _selectedDecisionOption;
        set => SetProperty(ref _selectedDecisionOption, value);
    }

    public SkillMergeDecision BuildDecision()
    {
        return new SkillMergeDecision(RelativePath, SelectedDecisionOption?.Value ?? Entry.SuggestedDecision);
    }
}