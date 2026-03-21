using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ProjectsViewModel? _projectsPage;
    private WorkspaceViewModel? _workspacePage;
    private SkillsPageContextViewModel? _skillsPageContext;
    private SkillsViewModel? _skillsPage;
    private McpPageContextViewModel? _mcpPageContext;
    private McpViewModel? _mcpPage;
    private WorkspaceProjectHealthStatus _projectWorkspaceHealthStatus = WorkspaceProjectHealthStatus.NotSelected;

    public ProjectsViewModel ProjectsPage => _projectsPage ??= new ProjectsViewModel(this);

    public WorkspaceViewModel WorkspacePage => _workspacePage ??= new WorkspaceViewModel(this);

    public SkillsPageContextViewModel SkillsPageContext => _skillsPageContext ??= new SkillsPageContextViewModel(this);

    public SkillsViewModel SkillsPage => _skillsPage ??= new SkillsViewModel(this, SkillsPageContext);

    public McpPageContextViewModel McpPageContext => _mcpPageContext ??= new McpPageContextViewModel(this);

    public McpViewModel McpPage => _mcpPage ??= new McpViewModel(this, McpPageContext);

    public WorkspaceScope CurrentWorkspaceScope => _currentWorkspaceScope;

    public WorkspaceProjectHealthStatus ProjectWorkspaceHealthStatus
    {
        get => _projectWorkspaceHealthStatus;
        private set => SetProperty(ref _projectWorkspaceHealthStatus, value);
    }

    private void SyncSkillFilterToCurrentScope(bool force = false)
    {
        // Skills now owns its page-level context and no longer follows the shell workspace scope.
    }
}
