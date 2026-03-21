using AIHub.Contracts;
namespace AIHub.Desktop.ViewModels;
public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly MainWindowViewModel _vm;
    public WorkspaceViewModel(MainWindowViewModel vm)
    {
        _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                RaisePropertyChanged(e.PropertyName);
            }
            RaisePropertyChanged(nameof(ProjectStatusTitle));
            RaisePropertyChanged(nameof(ProjectPrimaryActionLabel));
            RaisePropertyChanged(nameof(CurrentScopeTitle));
            RaisePropertyChanged(nameof(HasSelectedProject));
        };
    }
    public MainWindowViewModel Vm => _vm;
    public string ProjectStatusTitle => _vm.ProjectWorkspaceHealthStatus switch
    {
        WorkspaceProjectHealthStatus.NotOnboarded => _vm.Text.Workspace.ProjectNotOnboardedStatus,
        WorkspaceProjectHealthStatus.Legacy => _vm.Text.Workspace.ProjectLegacyStatus,
        WorkspaceProjectHealthStatus.Incomplete => _vm.Text.Workspace.ProjectIncompleteStatus,
        WorkspaceProjectHealthStatus.Healthy => _vm.Text.Workspace.ProjectHealthyStatus,
        _ => _vm.Text.Workspace.ProjectNoSelectionStatus
    };
    public string ProjectPrimaryActionLabel => _vm.ProjectWorkspaceHealthStatus switch
    {
        WorkspaceProjectHealthStatus.NotOnboarded => _vm.Text.Workspace.ProjectStartButton,
        WorkspaceProjectHealthStatus.Legacy => _vm.Text.Workspace.ProjectUpgradeButton,
        WorkspaceProjectHealthStatus.Incomplete => _vm.Text.Workspace.ProjectRepairButton,
        WorkspaceProjectHealthStatus.Healthy => _vm.Text.Workspace.ProjectReapplyButton,
        _ => _vm.Text.Workspace.ProjectStartButton
    };
    public string CurrentScopeTitle => _vm.ActiveScopeDisplay;
    public bool HasSelectedProject => _vm.SelectedProject is not null;
}
