namespace AIHub.Desktop.ViewModels;

public sealed class SkillsViewModel : ObservableObject
{
    public SkillsViewModel(MainWindowViewModel vm, SkillsPageContextViewModel context)
    {
        Vm = vm;
        Context = context;
    }

    public MainWindowViewModel Vm { get; }

    public SkillsPageContextViewModel Context { get; }
}
