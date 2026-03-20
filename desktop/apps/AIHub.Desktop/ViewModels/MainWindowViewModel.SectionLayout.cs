namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private SkillsSection _selectedSkillsSection = SkillsSection.Browse;
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
}
