namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private SkillsSection _selectedSkillsSection = SkillsSection.Browse;
    private SkillsBindingListMode _selectedSkillsBindingListMode = SkillsBindingListMode.Skill;
    private SkillsBindingEditor _selectedSkillsBindingEditor = SkillsBindingEditor.Skill;
    private McpSection _selectedMcpSection = McpSection.Overview;

    public SkillsSection SelectedSkillsSection
    {
        get => _selectedSkillsSection;
        set
        {
            if (SetProperty(ref _selectedSkillsSection, value))
            {
                RaisePropertyChanged(nameof(SelectedSkillsSectionIndex));
            }
        }
    }

    public int SelectedSkillsSectionIndex
    {
        get => (int)SelectedSkillsSection;
        set
        {
            if (value < (int)SkillsSection.Browse || value > (int)SkillsSection.Maintenance)
            {
                value = (int)SkillsSection.Browse;
            }

            SelectedSkillsSection = (SkillsSection)value;
        }
    }

    public int SelectedSkillsBindingEditorIndex
    {
        get => (int)_selectedSkillsBindingEditor;
        set
        {
            if (value < (int)SkillsBindingEditor.Skill || value > (int)SkillsBindingEditor.SkillGroup)
            {
                value = (int)SkillsBindingEditor.Skill;
            }

            if (SetProperty(ref _selectedSkillsBindingEditor, (SkillsBindingEditor)value))
            {
                RaisePropertyChanged(nameof(SelectedBindingTargetsImpactDisplay));
            }
        }
    }

    public int SelectedSkillsBindingListIndex
    {
        get => (int)_selectedSkillsBindingListMode;
        set
        {
            if (value < (int)SkillsBindingListMode.Skill || value > (int)SkillsBindingListMode.SkillGroup)
            {
                value = (int)SkillsBindingListMode.Skill;
            }

            SetProperty(ref _selectedSkillsBindingListMode, (SkillsBindingListMode)value);
        }
    }

    public McpSection SelectedMcpSection
    {
        get => _selectedMcpSection;
        set
        {
            if (SetProperty(ref _selectedMcpSection, value))
            {
                RaisePropertyChanged(nameof(SelectedMcpSectionIndex));
            }
        }
    }

    public int SelectedMcpSectionIndex
    {
        get => (int)SelectedMcpSection;
        set
        {
            if (value < (int)McpSection.Overview || value > (int)McpSection.ManagedProcesses)
            {
                value = (int)McpSection.Overview;
            }

            SelectedMcpSection = (McpSection)value;
        }
    }

    private enum SkillsBindingEditor
    {
        Skill,
        SkillGroup
    }

    private enum SkillsBindingListMode
    {
        Skill,
        SkillGroup
    }
}
