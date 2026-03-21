using AIHub.Contracts;
namespace AIHub.Desktop.ViewModels;
public sealed class McpPageContextViewModel : ObservableObject
{
    private readonly MainWindowViewModel _vm;
    public McpPageContextViewModel(MainWindowViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                RaisePropertyChanged(e.PropertyName);
            }
            RaisePropertyChanged(nameof(CurrentScopeDisplay));
            RaisePropertyChanged(nameof(IsProjectScope));
            RaisePropertyChanged(nameof(IsGlobalScope));
            RaisePropertyChanged(nameof(HasSelectedProject));
        };
    }
    public System.Collections.ObjectModel.ObservableCollection<ProjectRecord> Projects => _vm.Projects;
    public ProjectRecord? SelectedProject
    {
        get => _vm.SelectedProject;
        set => _vm.SelectedProject = value;
    }
    public AsyncDelegateCommand SwitchToGlobalScopeCommand => _vm.SwitchToGlobalScopeCommand;
    public AsyncDelegateCommand SwitchToSelectedProjectScopeCommand => _vm.SwitchToSelectedProjectScopeCommand;
    public WorkspaceScope CurrentWorkspaceScope => _vm.CurrentWorkspaceScope;
    public string CurrentScopeDisplay => _vm.ActiveScopeDisplay;
    public bool IsProjectScope => CurrentWorkspaceScope == WorkspaceScope.Project;
    public bool IsGlobalScope => CurrentWorkspaceScope == WorkspaceScope.Global;
    public bool HasSelectedProject => SelectedProject is not null;
}

