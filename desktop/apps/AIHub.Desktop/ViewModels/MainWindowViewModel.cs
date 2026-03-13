
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using AIHub.Application.Models;
using AIHub.Application.Services;
using AIHub.Contracts;
using AIHub.Desktop.Services;
using AIHub.Desktop.Text;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private static readonly DesktopTextCatalog DefaultText = DesktopTextCatalog.Default;
    private readonly WorkspaceControlService? _workspaceControlService;
    private readonly McpControlService? _mcpControlService;
    private readonly SkillsCatalogService? _skillsCatalogService;
    private readonly ScriptCenterService? _scriptCenterService;
    private IReadOnlyList<McpProfileRecord> _mcpProfileCache = Array.Empty<McpProfileRecord>();
    private ProjectRecord? _selectedProject;
    private ProfileOption? _selectedProfileOption;
    private McpProfileListItem? _selectedMcpProfile;
    private McpManagedProcessItem? _selectedManagedProcess;
    private SkillSourceRecord? _selectedSkillSource;
    private ProfileOption? _selectedSkillSourceProfileOption;
    private ScriptDefinitionRecord? _selectedScript;
    private ProfileOption? _selectedScriptProfileOption;
    private string _appTitle = DefaultText.Shell.AppTitle;
    private string _hubRootDisplay = DefaultText.State.HubRootNotResolved;
    private string _hubStatus = DefaultText.State.WaitingForWorkspace;
    private string _appVersionDisplay = DefaultText.Shell.VersionDisplay(DesktopBuildInfo.Version);
    private string _projectCountDisplay = DefaultText.State.ProjectCount(0);
    private string _projectName = string.Empty;
    private string _projectPath = string.Empty;
    private bool _isPinned;
    private string _activeScopeDisplay = DefaultText.State.ScopeGlobal;
    private string _defaultProfileDisplay = DefaultText.State.ScopeGlobal;
    private string _lastOpenedProjectDisplay = DefaultText.State.NotSet;
    private string _operationSummary = DefaultText.State.WaitingForWorkspaceData;
    private string _operationDetails = DefaultText.State.OperationDetailsPlaceholder;
    private bool _hasValidationErrors;
    private bool _isBusy;
    private string _hubRootInput = string.Empty;
    private bool _autoStartManagedMcpOnLoad = true;
    private string _mcpRuntimeSummaryDisplay = DefaultText.State.McpRuntimeSummary(0, 0);
    private string _mcpManagedProcessDetails = DefaultText.State.NoManagedMcpDefinitions;
    private string _mcpManifestPathDisplay = DefaultText.State.NotSelected;
    private string _mcpServerSummary = DefaultText.State.McpConfigNotLoaded;
    private string _mcpManifestEditor = "{\r\n  \"mcpServers\": {}\r\n}";
    private string _mcpClaudePathDisplay = DefaultText.State.NotGenerated;
    private string _mcpCodexPathDisplay = DefaultText.State.NotGenerated;
    private string _mcpAntigravityPathDisplay = DefaultText.State.NotGenerated;
    private string _mcpClaudePreview = DefaultText.State.ConfigNotGenerated;
    private string _mcpCodexPreview = DefaultText.State.ConfigNotGenerated;
    private string _mcpAntigravityPreview = DefaultText.State.ConfigNotGenerated;
    private string _managedProcessName = string.Empty;
    private string _managedProcessCommand = string.Empty;
    private string _managedProcessArgumentsText = string.Empty;
    private string _managedProcessWorkingDirectory = string.Empty;
    private bool _managedProcessEnabled = true;
    private bool _managedProcessAutoStart;
    private string _managedProcessHealthCheckUrl = string.Empty;
    private string _managedProcessHealthTimeoutText = "5";
    private string _managedProcessStatusDisplay = DefaultText.State.NoManagedProcessSelected;
    private string _managedProcessHealthStatusDisplay = DefaultText.State.NotChecked;
    private string _managedProcessLastMessageDisplay = DefaultText.State.SaveDefinitionBeforeOperate;
    private string _managedProcessOutputLogPath = DefaultText.State.NotGenerated;
    private string _managedProcessErrorLogPath = DefaultText.State.NotGenerated;
    private string _managedProcessOutputPreview = DefaultText.State.NoLogs;
    private string _managedProcessErrorPreview = DefaultText.State.NoLogs;
    private string _skillsSummaryDisplay = DefaultText.State.InstalledSkillsSummary(0, 0);
    private string _skillSourcesSummaryDisplay = DefaultText.State.SkillSourcesSummary(0);
    private string _skillSourceLocalName = string.Empty;
    private string _skillSourceRepository = string.Empty;
    private string _skillSourcePath = string.Empty;
    private string _skillSourceReference = "main";
    private bool _skillSourceEnabled = true;
    private bool _skillSourceAutoUpdate = true;
    private string _scriptsSummaryDisplay = DefaultText.State.ScriptsSummary(0);
    private string _scriptDescriptionDisplay = DefaultText.State.SelectScript;
    private string _scriptProjectPath = string.Empty;
    private string _scriptUserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _scriptArgumentsText = string.Empty;
    private string _scriptExecutionHint = DefaultText.State.ScriptUsagePlaceholder;

    public MainWindowViewModel(IFileDialogService? fileDialogService = null)
    {
        _fileDialogService = fileDialogService;
        Projects = new ObservableCollection<ProjectRecord>();
        ProfileOptions = new ObservableCollection<ProfileOption>(CreateProfileOptions());
        SkillSourceProfileOptions = new ObservableCollection<ProfileOption>(CreateProfileOptions());
        SkillModeOptions = new ObservableCollection<SkillModeOption>(CreateSkillModeOptions());
        SkillSourceKindOptions = new ObservableCollection<SkillSourceKindOption>(CreateSkillSourceKindOptions());
        ScriptProfileOptions = new ObservableCollection<ProfileOption>(CreateProfileOptions());
        EnabledClients = new ObservableCollection<string>();
        Modules = new ObservableCollection<string>();
        NextSteps = new ObservableCollection<string>();
        ValidationErrors = new ObservableCollection<string>();
        McpProfiles = new ObservableCollection<McpProfileListItem>();
        ManagedProcesses = new ObservableCollection<McpManagedProcessItem>();
        InstalledSkills = new ObservableCollection<InstalledSkillRecord>();
        SkillSources = new ObservableCollection<SkillSourceRecord>();
        Scripts = new ObservableCollection<ScriptDefinitionRecord>();

        _selectedProfileOption = ProfileOptions.FirstOrDefault(option => option.Value == ProfileKind.Global);
        _selectedSkillSourceProfileOption = SkillSourceProfileOptions.FirstOrDefault(option => option.Value == ProfileKind.Global);
        _selectedSkillModeOption = SkillModeOptions.FirstOrDefault(option => option.Value == SkillCustomizationMode.Local);
        _selectedSkillSourceKindOption = SkillSourceKindOptions.FirstOrDefault(option => option.Value == SkillSourceKind.GitRepository);
        _selectedScriptProfileOption = ScriptProfileOptions.FirstOrDefault(option => option.Value == ProfileKind.Global);

        RefreshCommand = new AsyncDelegateCommand(RefreshAsync, CanRefresh);
        SaveProjectCommand = new AsyncDelegateCommand(SaveProjectAsync, CanUseWorkspace);
        DeleteSelectedProjectCommand = new AsyncDelegateCommand(DeleteSelectedProjectAsync, CanUseSelectedProject);
        ApplyGlobalLinksCommand = new AsyncDelegateCommand(ApplyGlobalLinksAsync, CanUseWorkspace);
        ApplyProjectProfileCommand = new AsyncDelegateCommand(ApplyProjectProfileAsync, CanUseWorkspace);
        SetCurrentProjectCommand = new AsyncDelegateCommand(SetCurrentProjectAsync, CanUseWorkspace);
        ClearFormCommand = new AsyncDelegateCommand(ClearFormAsync, () => !IsBusy);
        SaveMcpManifestCommand = new AsyncDelegateCommand(SaveMcpManifestAsync, CanUseMcp);
        GenerateMcpConfigsCommand = new AsyncDelegateCommand(GenerateMcpConfigsAsync, CanUseMcp);
        SaveManagedProcessCommand = new AsyncDelegateCommand(SaveManagedProcessAsync, CanUseMcp);
        DeleteManagedProcessCommand = new AsyncDelegateCommand(DeleteManagedProcessAsync, CanUseSelectedManagedProcess);
        StartManagedProcessCommand = new AsyncDelegateCommand(StartManagedProcessAsync, CanUseSelectedManagedProcess);
        StopManagedProcessCommand = new AsyncDelegateCommand(StopManagedProcessAsync, CanUseSelectedManagedProcess);
        RestartManagedProcessCommand = new AsyncDelegateCommand(RestartManagedProcessAsync, CanUseSelectedManagedProcess);
        RunManagedProcessHealthCheckCommand = new AsyncDelegateCommand(RunManagedProcessHealthCheckAsync, CanUseSelectedManagedProcess);
        ClearManagedProcessFormCommand = new AsyncDelegateCommand(ClearManagedProcessFormAsync, () => !IsBusy);
        SaveSkillSourceCommand = new AsyncDelegateCommand(SaveSkillSourceAsync, CanUseSkills);
        DeleteSkillSourceCommand = new AsyncDelegateCommand(DeleteSelectedSkillSourceAsync, CanUseSelectedSkillSource);
        ClearSkillSourceFormCommand = new AsyncDelegateCommand(ClearSkillSourceFormAsync, () => !IsBusy);
        SaveSkillInstallCommand = new AsyncDelegateCommand(SaveSkillInstallAsync, CanUseSelectedInstalledSkill);
        DeleteSkillInstallCommand = new AsyncDelegateCommand(DeleteSkillInstallAsync, CanDeleteSelectedSkillInstall);
        CaptureSkillBaselineCommand = new AsyncDelegateCommand(CaptureSkillBaselineAsync, CanCaptureSelectedSkillBaseline);
        PreviewSkillDiffCommand = new AsyncDelegateCommand(PreviewSkillDiffAsync, CanCheckSelectedSkillUpdate);
        ScanSkillSourceCommand = new AsyncDelegateCommand(ScanSelectedSkillSourceAsync, CanScanSelectedSkillSource);
        CheckSkillUpdateCommand = new AsyncDelegateCommand(CheckSelectedSkillUpdateAsync, CanCheckSelectedSkillUpdate);
        SyncSkillCommand = new AsyncDelegateCommand(SyncSelectedSkillAsync, CanSyncSelectedSkill);
        ForceSyncSkillCommand = new AsyncDelegateCommand(ForceSyncSelectedSkillAsync, CanSyncSelectedSkill);
        RollbackSkillCommand = new AsyncDelegateCommand(RollbackSelectedSkillAsync, CanRollbackSelectedSkill);
        ExecuteScriptCommand = new AsyncDelegateCommand(ExecuteSelectedScriptAsync, CanUseSelectedScript);
        SaveHubRootCommand = new AsyncDelegateCommand(SaveHubRootAsync, CanUseWorkspace);
        SaveAutomationSettingsCommand = new AsyncDelegateCommand(SaveAutomationSettingsAsync, CanUseWorkspace);
        SwitchToGlobalScopeCommand = new AsyncDelegateCommand(SwitchToGlobalScopeAsync, CanUseWorkspace);
        SwitchToSelectedProjectScopeCommand = new AsyncDelegateCommand(SwitchToSelectedProjectScopeAsync, CanUseWorkspace);
        InitializeFileDialogCommands();
        InitializeMaintenanceState();
        InitializeAdvancedCommands();
        InitializeDiagnosticsState();
    }

    public MainWindowViewModel(
        WorkspaceControlService workspaceControlService,
        McpControlService mcpControlService,
        SkillsCatalogService skillsCatalogService,
        ScriptCenterService scriptCenterService,
        IFileDialogService? fileDialogService = null)
        : this(fileDialogService)
    {
        _workspaceControlService = workspaceControlService;
        _mcpControlService = mcpControlService;
        _skillsCatalogService = skillsCatalogService;
        _scriptCenterService = scriptCenterService;
    }

    public ObservableCollection<ProjectRecord> Projects { get; }

    public DesktopTextCatalog Text { get; } = DesktopTextCatalog.Default;

    public ObservableCollection<ProfileOption> ProfileOptions { get; }

    public ObservableCollection<ProfileOption> SkillSourceProfileOptions { get; }

    public ObservableCollection<ProfileOption> ScriptProfileOptions { get; }

    public ObservableCollection<string> EnabledClients { get; }

    public ObservableCollection<string> Modules { get; }

    public ObservableCollection<string> NextSteps { get; }

    public ObservableCollection<string> ValidationErrors { get; }

    public ObservableCollection<McpProfileListItem> McpProfiles { get; }

    public ObservableCollection<McpManagedProcessItem> ManagedProcesses { get; }

    public ObservableCollection<InstalledSkillRecord> InstalledSkills { get; }

    public ObservableCollection<SkillSourceRecord> SkillSources { get; }

    public ObservableCollection<ScriptDefinitionRecord> Scripts { get; }

    public AsyncDelegateCommand RefreshCommand { get; }

    public AsyncDelegateCommand SaveProjectCommand { get; }

    public AsyncDelegateCommand DeleteSelectedProjectCommand { get; }

    public AsyncDelegateCommand ApplyGlobalLinksCommand { get; }

    public AsyncDelegateCommand ApplyProjectProfileCommand { get; }

    public AsyncDelegateCommand SetCurrentProjectCommand { get; }

    public AsyncDelegateCommand ClearFormCommand { get; }

    public AsyncDelegateCommand SaveMcpManifestCommand { get; }

    public AsyncDelegateCommand GenerateMcpConfigsCommand { get; }

    public AsyncDelegateCommand SaveManagedProcessCommand { get; }

    public AsyncDelegateCommand DeleteManagedProcessCommand { get; }

    public AsyncDelegateCommand StartManagedProcessCommand { get; }

    public AsyncDelegateCommand StopManagedProcessCommand { get; }

    public AsyncDelegateCommand RestartManagedProcessCommand { get; }

    public AsyncDelegateCommand RunManagedProcessHealthCheckCommand { get; }

    public AsyncDelegateCommand ClearManagedProcessFormCommand { get; }

    public AsyncDelegateCommand SaveSkillSourceCommand { get; }

    public AsyncDelegateCommand DeleteSkillSourceCommand { get; }

    public AsyncDelegateCommand ClearSkillSourceFormCommand { get; }

    public AsyncDelegateCommand ExecuteScriptCommand { get; }

    public AsyncDelegateCommand SaveHubRootCommand { get; }

    public AsyncDelegateCommand SaveAutomationSettingsCommand { get; }

    public AsyncDelegateCommand SwitchToGlobalScopeCommand { get; }

    public AsyncDelegateCommand SwitchToSelectedProjectScopeCommand { get; }

    public string AppTitle
    {
        get => _appTitle;
        private set => SetProperty(ref _appTitle, value);
    }

    public string HubRootDisplay
    {
        get => _hubRootDisplay;
        private set => SetProperty(ref _hubRootDisplay, value);
    }

    public string HubStatus
    {
        get => _hubStatus;
        private set => SetProperty(ref _hubStatus, value);
    }

    public string ProjectCountDisplay
    {
        get => _projectCountDisplay;
        private set => SetProperty(ref _projectCountDisplay, value);
    }

    public string HubRootInput
    {
        get => _hubRootInput;
        set => SetProperty(ref _hubRootInput, value);
    }

    public bool AutoStartManagedMcpOnLoad
    {
        get => _autoStartManagedMcpOnLoad;
        set => SetProperty(ref _autoStartManagedMcpOnLoad, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set => SetProperty(ref _projectName, value);
    }

    public string ProjectPath
    {
        get => _projectPath;
        set => SetProperty(ref _projectPath, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public string ActiveScopeDisplay
    {
        get => _activeScopeDisplay;
        private set => SetProperty(ref _activeScopeDisplay, value);
    }

    public string DefaultProfileDisplay
    {
        get => _defaultProfileDisplay;
        private set => SetProperty(ref _defaultProfileDisplay, value);
    }

    public string LastOpenedProjectDisplay
    {
        get => _lastOpenedProjectDisplay;
        private set => SetProperty(ref _lastOpenedProjectDisplay, value);
    }

    public string OperationSummary
    {
        get => _operationSummary;
        private set => SetProperty(ref _operationSummary, value);
    }

    public string OperationDetails
    {
        get => _operationDetails;
        private set => SetProperty(ref _operationDetails, value);
    }

    public bool HasValidationErrors
    {
        get => _hasValidationErrors;
        private set => SetProperty(ref _hasValidationErrors, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string McpRuntimeSummaryDisplay
    {
        get => _mcpRuntimeSummaryDisplay;
        private set => SetProperty(ref _mcpRuntimeSummaryDisplay, value);
    }

    public string McpManagedProcessDetails
    {
        get => _mcpManagedProcessDetails;
        private set => SetProperty(ref _mcpManagedProcessDetails, value);
    }

    public string McpManifestPathDisplay
    {
        get => _mcpManifestPathDisplay;
        private set => SetProperty(ref _mcpManifestPathDisplay, value);
    }

    public string McpServerSummary
    {
        get => _mcpServerSummary;
        private set => SetProperty(ref _mcpServerSummary, value);
    }

    public string McpManifestEditor
    {
        get => _mcpManifestEditor;
        set => SetProperty(ref _mcpManifestEditor, value);
    }

    public string McpClaudePathDisplay
    {
        get => _mcpClaudePathDisplay;
        private set => SetProperty(ref _mcpClaudePathDisplay, value);
    }

    public string McpCodexPathDisplay
    {
        get => _mcpCodexPathDisplay;
        private set => SetProperty(ref _mcpCodexPathDisplay, value);
    }

    public string McpAntigravityPathDisplay
    {
        get => _mcpAntigravityPathDisplay;
        private set => SetProperty(ref _mcpAntigravityPathDisplay, value);
    }

    public string McpClaudePreview
    {
        get => _mcpClaudePreview;
        private set => SetProperty(ref _mcpClaudePreview, value);
    }

    public string McpCodexPreview
    {
        get => _mcpCodexPreview;
        private set => SetProperty(ref _mcpCodexPreview, value);
    }

    public string McpAntigravityPreview
    {
        get => _mcpAntigravityPreview;
        private set => SetProperty(ref _mcpAntigravityPreview, value);
    }

    public string ManagedProcessName
    {
        get => _managedProcessName;
        set => SetProperty(ref _managedProcessName, value);
    }

    public string ManagedProcessCommand
    {
        get => _managedProcessCommand;
        set => SetProperty(ref _managedProcessCommand, value);
    }

    public string ManagedProcessArgumentsText
    {
        get => _managedProcessArgumentsText;
        set => SetProperty(ref _managedProcessArgumentsText, value);
    }

    public string ManagedProcessWorkingDirectory
    {
        get => _managedProcessWorkingDirectory;
        set => SetProperty(ref _managedProcessWorkingDirectory, value);
    }

    public bool ManagedProcessEnabled
    {
        get => _managedProcessEnabled;
        set => SetProperty(ref _managedProcessEnabled, value);
    }

    public bool ManagedProcessAutoStart
    {
        get => _managedProcessAutoStart;
        set => SetProperty(ref _managedProcessAutoStart, value);
    }

    public string ManagedProcessHealthCheckUrl
    {
        get => _managedProcessHealthCheckUrl;
        set => SetProperty(ref _managedProcessHealthCheckUrl, value);
    }

    public string ManagedProcessHealthTimeoutText
    {
        get => _managedProcessHealthTimeoutText;
        set => SetProperty(ref _managedProcessHealthTimeoutText, value);
    }

    public string ManagedProcessStatusDisplay
    {
        get => _managedProcessStatusDisplay;
        private set => SetProperty(ref _managedProcessStatusDisplay, value);
    }

    public string ManagedProcessHealthStatusDisplay
    {
        get => _managedProcessHealthStatusDisplay;
        private set => SetProperty(ref _managedProcessHealthStatusDisplay, value);
    }

    public string ManagedProcessLastMessageDisplay
    {
        get => _managedProcessLastMessageDisplay;
        private set => SetProperty(ref _managedProcessLastMessageDisplay, value);
    }

    public string ManagedProcessOutputLogPath
    {
        get => _managedProcessOutputLogPath;
        private set => SetProperty(ref _managedProcessOutputLogPath, value);
    }

    public string ManagedProcessErrorLogPath
    {
        get => _managedProcessErrorLogPath;
        private set => SetProperty(ref _managedProcessErrorLogPath, value);
    }

    public string ManagedProcessOutputPreview
    {
        get => _managedProcessOutputPreview;
        private set => SetProperty(ref _managedProcessOutputPreview, value);
    }

    public string ManagedProcessErrorPreview
    {
        get => _managedProcessErrorPreview;
        private set => SetProperty(ref _managedProcessErrorPreview, value);
    }

    public string SkillsSummaryDisplay
    {
        get => _skillsSummaryDisplay;
        private set => SetProperty(ref _skillsSummaryDisplay, value);
    }

    public string SkillSourcesSummaryDisplay
    {
        get => _skillSourcesSummaryDisplay;
        private set => SetProperty(ref _skillSourcesSummaryDisplay, value);
    }

    public string SkillSourceLocalName
    {
        get => _skillSourceLocalName;
        set => SetProperty(ref _skillSourceLocalName, value);
    }

    public string SkillSourceRepository
    {
        get => _skillSourceRepository;
        set => SetProperty(ref _skillSourceRepository, value);
    }

    public string SkillSourcePath
    {
        get => _skillSourcePath;
        set => SetProperty(ref _skillSourcePath, value);
    }

    public string SkillSourceReference
    {
        get => _skillSourceReference;
        set => SetProperty(ref _skillSourceReference, value);
    }

    public bool SkillSourceEnabled
    {
        get => _skillSourceEnabled;
        set => SetProperty(ref _skillSourceEnabled, value);
    }

    public bool SkillSourceAutoUpdate
    {
        get => _skillSourceAutoUpdate;
        set
        {
            if (SetProperty(ref _skillSourceAutoUpdate, value))
            {
                OnSkillSchedulePolicyChanged();
            }
        }
    }

    public string ScriptsSummaryDisplay
    {
        get => _scriptsSummaryDisplay;
        private set => SetProperty(ref _scriptsSummaryDisplay, value);
    }

    public string ScriptDescriptionDisplay
    {
        get => _scriptDescriptionDisplay;
        private set => SetProperty(ref _scriptDescriptionDisplay, value);
    }

    public string ScriptProjectPath
    {
        get => _scriptProjectPath;
        set => SetProperty(ref _scriptProjectPath, value);
    }

    public string ScriptUserHome
    {
        get => _scriptUserHome;
        set => SetProperty(ref _scriptUserHome, value);
    }

    public string ScriptArgumentsText
    {
        get => _scriptArgumentsText;
        set => SetProperty(ref _scriptArgumentsText, value);
    }

    public string ScriptExecutionHint
    {
        get => _scriptExecutionHint;
        private set => SetProperty(ref _scriptExecutionHint, value);
    }

    public ProjectRecord? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                if (value is not null)
                {
                    ApplyProjectToForm(value);
                    if (string.IsNullOrWhiteSpace(ScriptProjectPath))
                    {
                        ScriptProjectPath = value.Path;
                    }
                }

                RaiseCommandStates();
            }
        }
    }

    public ProfileOption? SelectedProfileOption
    {
        get => _selectedProfileOption;
        set => SetProperty(ref _selectedProfileOption, value);
    }

    public McpProfileListItem? SelectedMcpProfile
    {
        get => _selectedMcpProfile;
        set
        {
            if (SetProperty(ref _selectedMcpProfile, value))
            {
                ApplySelectedMcpProfile();
                RaiseCommandStates();
            }
        }
    }

    public McpManagedProcessItem? SelectedManagedProcess
    {
        get => _selectedManagedProcess;
        set
        {
            if (SetProperty(ref _selectedManagedProcess, value))
            {
                ApplySelectedManagedProcess();
                RaiseCommandStates();
            }
        }
    }

    public SkillSourceRecord? SelectedSkillSource
    {
        get => _selectedSkillSource;
        set
        {
            if (SetProperty(ref _selectedSkillSource, value))
            {
                ApplySelectedSkillSource();
                RaiseCommandStates();
            }
        }
    }

    public ProfileOption? SelectedSkillSourceProfileOption
    {
        get => _selectedSkillSourceProfileOption;
        set => SetProperty(ref _selectedSkillSourceProfileOption, value);
    }

    public ScriptDefinitionRecord? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (SetProperty(ref _selectedScript, value))
            {
                ApplySelectedScript();
                RaiseCommandStates();
            }
        }
    }

    public ProfileOption? SelectedScriptProfileOption
    {
        get => _selectedScriptProfileOption;
        set => SetProperty(ref _selectedScriptProfileOption, value);
    }

    public async Task InitializeAsync()
    {
        if (_workspaceControlService is null)
        {
            return;
        }

        await RefreshAsync();
    }

    private bool CanRefresh() => !IsBusy && _workspaceControlService is not null;

    private bool CanUseWorkspace() => !IsBusy && _workspaceControlService is not null;

    private bool CanUseSelectedProject() => !IsBusy && _workspaceControlService is not null && SelectedProject is not null;

    private bool CanUseMcp() => !IsBusy && _mcpControlService is not null;

    private bool CanUseSkills() => !IsBusy && _skillsCatalogService is not null;

    private bool CanUseSelectedSkillSource() => !IsBusy && _skillsCatalogService is not null && SelectedSkillSource is not null;

    private bool CanUseSelectedScript() => !IsBusy && _scriptCenterService is not null && SelectedScript is not null;

    private bool CanUseSelectedManagedProcess() => !IsBusy && _mcpControlService is not null && SelectedManagedProcess is not null;

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            await ReloadAllAsync(SelectedProject?.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            SetOperation(true, Text.State.WorkspaceRefreshed, string.Empty);
        });
    }

    private async Task SaveProjectAsync()
    {
        if (!TryBuildDraftProject(out var project, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.SaveProjectAsync(project);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(project.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task DeleteSelectedProjectAsync()
    {
        if (SelectedProject is null)
        {
            SetOperation(false, Text.State.SelectProjectToDelete, string.Empty);
            return;
        }

        var confirmed = await ConfirmAsync(CreateDeleteProjectConfirmation(SelectedProject));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.DeleteProjectAsync(SelectedProject.Path);
            ApplyOperationResult(result);

            if (result.Success)
            {
                SelectedProject = null;
                ClearFormFields(ProfileKind.Global);
                await ReloadAllAsync(null, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task ApplyGlobalLinksAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ScriptExecution))
            {
                return;
            }

            var result = await _workspaceControlService!.ApplyGlobalLinksAsync();
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(SelectedProject?.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task ApplyProjectProfileAsync()
    {
        if (!TryBuildDraftProject(out var project, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ScriptExecution))
            {
                return;
            }

            var result = await _workspaceControlService!.ApplyProjectProfileAsync(project);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(project.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task SetCurrentProjectAsync()
    {
        if (!TryBuildDraftProject(out var project, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var saveResult = await _workspaceControlService!.SaveProjectAsync(project);
            if (!saveResult.Success)
            {
                ApplyOperationResult(saveResult);
                return;
            }

            var result = await _workspaceControlService.SetCurrentProjectAsync(project);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(project.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private Task ClearFormAsync()
    {
        SelectedProject = null;
        ClearFormFields(ProfileKind.Global);
        SetOperation(true, Text.State.ProjectFormCleared, string.Empty);
        return Task.CompletedTask;
    }

    private async Task SaveMcpManifestAsync()
    {
        if (SelectedMcpProfile is null)
        {
            SetOperation(false, Text.State.SelectMcpProfileFirst, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.SaveManifestAsync(SelectedMcpProfile.Profile, McpManifestEditor);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task GenerateMcpConfigsAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ScriptExecution))
            {
                return;
            }

            var result = await _mcpControlService!.GenerateConfigsAsync();
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task SaveManagedProcessAsync()
    {
        if (!TryBuildManagedProcessDraft(out var draft, out var originalName, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.SaveManagedProcessAsync(originalName, draft);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, draft.Name);
            }
        });
    }

    private async Task DeleteManagedProcessAsync()
    {
        if (SelectedManagedProcess is null)
        {
            SetOperation(false, Text.State.SelectManagedProcessToDelete, string.Empty);
            return;
        }

        var confirmed = await ConfirmAsync(CreateDeleteManagedProcessConfirmation(SelectedManagedProcess));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.DeleteManagedProcessAsync(SelectedManagedProcess.Name);
            ApplyOperationResult(result);

            if (result.Success)
            {
                SelectedManagedProcess = null;
                ClearManagedProcessFormFields();
                await LoadMcpAsync(SelectedMcpProfile?.Profile, null);
            }
        });
    }

    private async Task StartManagedProcessAsync()
    {
        if (SelectedManagedProcess is null)
        {
            SetOperation(false, Text.State.SelectManagedProcessToStart, string.Empty);
            return;
        }

        if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ManagedMcpExecution))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.StartManagedProcessAsync(SelectedManagedProcess.Name);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess.Name);
            }
        });
    }

    private async Task StopManagedProcessAsync()
    {
        if (SelectedManagedProcess is null)
        {
            SetOperation(false, Text.State.SelectManagedProcessToStop, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.StopManagedProcessAsync(SelectedManagedProcess.Name);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess.Name);
            }
        });
    }

    private async Task RestartManagedProcessAsync()
    {
        if (SelectedManagedProcess is null)
        {
            SetOperation(false, Text.State.SelectManagedProcessToRestart, string.Empty);
            return;
        }

        if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ManagedMcpExecution))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.RestartManagedProcessAsync(SelectedManagedProcess.Name);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess.Name);
            }
        });
    }

    private async Task RunManagedProcessHealthCheckAsync()
    {
        if (SelectedManagedProcess is null)
        {
            SetOperation(false, Text.State.SelectManagedProcessToHealthCheck, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.RunHealthCheckAsync(SelectedManagedProcess.Name);
            ApplyOperationResult(result);
            await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess.Name);
        });
    }

    private Task ClearManagedProcessFormAsync()
    {
        SelectedManagedProcess = null;
        ClearManagedProcessFormFields();
        SetOperation(true, Text.State.ManagedProcessFormCleared, string.Empty);
        return Task.CompletedTask;
    }

    private async Task SaveSkillSourceAsync()
    {
        if (!TryBuildSkillSourceDraft(out var draft, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.SaveSourceAsync(SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile, draft);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadSkillsAsync(draft.LocalName, draft.Profile);
            }
        });
    }

    private async Task DeleteSelectedSkillSourceAsync()
    {
        if (SelectedSkillSource is null)
        {
            SetOperation(false, Text.State.SelectSkillSourceToDelete, string.Empty);
            return;
        }

        var confirmed = await ConfirmAsync(CreateDeleteSkillSourceConfirmation(SelectedSkillSource));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.DeleteSourceAsync(SelectedSkillSource.LocalName, SelectedSkillSource.Profile);
            ApplyOperationResult(result);

            if (result.Success)
            {
                SelectedSkillSource = null;
                ClearSkillSourceFormFields();
                await LoadSkillsAsync();
            }
        });
    }

    private Task ClearSkillSourceFormAsync()
    {
        SelectedSkillSource = null;
        ClearSkillSourceFormFields();
        SetOperation(true, Text.State.SkillSourceFormCleared, string.Empty);
        return Task.CompletedTask;
    }

    private async Task ExecuteSelectedScriptAsync()
    {
        if (SelectedScript is null)
        {
            SetOperation(false, Text.State.SelectScriptFirst, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _scriptCenterService!.ExecuteAsync(
                SelectedScript.RelativePath,
                ScriptUserHome,
                ScriptProjectPath,
                SelectedScriptProfileOption?.Value ?? ProfileKind.Global,
                ScriptArgumentsText);

            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(SelectedProject?.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task SaveHubRootAsync()
    {
        if (string.IsNullOrWhiteSpace(HubRootInput))
        {
            SetOperation(false, Text.State.EnterHubRootFirst, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.SetHubRootAsync(HubRootInput);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(SelectedProject?.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task SaveAutomationSettingsAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.SaveAutomationSettingsAsync(
                AutoStartManagedMcpOnLoad,
                AutoCheckSkillUpdatesOnLoad,
                AutoSyncSafeSkillsOnLoad);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadWorkspaceAsync(SelectedProject?.Path);
            }
        });
    }

    private async Task SwitchToGlobalScopeAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.SwitchToGlobalScopeAsync();
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadWorkspaceAsync(null);
            }
        });
    }

    private async Task SwitchToSelectedProjectScopeAsync()
    {
        if (!TryResolveProjectForScopeSwitch(out var project, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.SwitchToProjectScopeAsync(project);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadWorkspaceAsync(project.Path);
            }
        });
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        try
        {
            IsBusy = true;
            await operation();
        }
        catch (Exception exception)
        {
            SetOperation(false, Text.State.UnhandledError, exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAllAsync(string? preferredProjectPath = null, ProfileKind? preferredMcpProfile = null, string? preferredProcessName = null)
    {
        await LoadWorkspaceAsync(preferredProjectPath);
        await LoadMcpAsync(preferredMcpProfile, preferredProcessName);
        await LoadSkillsAsync();
        await LoadScriptsAsync();
        await LoadDiagnosticsAsync();
    }

    private async Task LoadWorkspaceAsync(string? preferredProjectPath = null)
    {
        if (_workspaceControlService is null)
        {
            return;
        }

        var snapshot = await _workspaceControlService.LoadAsync();
        ApplyWorkspaceSnapshot(snapshot, preferredProjectPath);
    }

    private async Task LoadMcpAsync(ProfileKind? preferredProfile = null, string? preferredProcessName = null)
    {
        if (_mcpControlService is null)
        {
            return;
        }

        var snapshot = await _mcpControlService.LoadAsync();
        ApplyMcpSnapshot(snapshot, preferredProfile, preferredProcessName);
    }

    private async Task LoadSkillsAsync(string? preferredLocalName = null, ProfileKind? preferredProfile = null)
    {
        if (_skillsCatalogService is null)
        {
            return;
        }

        var snapshot = await _skillsCatalogService.LoadAsync();
        ReplaceCollection(InstalledSkills, snapshot.InstalledSkills);
        ReplaceCollection(SkillSources, snapshot.Sources);

        var registeredSkillCount = snapshot.InstalledSkills.Count(skill => skill.IsRegistered);
        SkillsSummaryDisplay = Text.State.InstalledSkillsSummary(snapshot.InstalledSkills.Count, registeredSkillCount);
        SkillSourcesSummaryDisplay = Text.State.SkillSourcesSummary(snapshot.Sources.Count);

        var selectedInstalledSkill = FindInstalledSkill(snapshot.InstalledSkills, SelectedInstalledSkill?.RelativePath, SelectedInstalledSkill?.Profile)
            ?? snapshot.InstalledSkills.FirstOrDefault();
        SelectedInstalledSkill = selectedInstalledSkill;

        var selectedSource = FindSkillSource(snapshot.Sources, preferredLocalName, preferredProfile)
            ?? FindSkillSource(snapshot.Sources, SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile)
            ?? FindSkillSource(snapshot.Sources, SelectedSkillInstallSource?.LocalName, SelectedSkillInstallSource?.Profile)
            ?? snapshot.Sources.FirstOrDefault();
        SelectedSkillSource = selectedSource;

        if (selectedSource is null)
        {
            ClearSkillSourceFormFields();
        }

        if (SelectedInstalledSkill is null)
        {
            SelectedSkillInstallSource = null;
        }
        else if (SelectedSkillInstallSource is not null)
        {
            SelectedSkillInstallSource = FindSkillSource(snapshot.Sources, SelectedSkillInstallSource.LocalName, SelectedSkillInstallSource.Profile);
        }
    }

    private async Task LoadScriptsAsync(string? preferredRelativePath = null)
    {
        if (_scriptCenterService is null)
        {
            return;
        }

        var snapshot = await _scriptCenterService.LoadAsync();
        ReplaceCollection(Scripts, snapshot.Scripts);
        ScriptsSummaryDisplay = Text.State.ScriptsSummary(snapshot.Scripts.Count);

        var selectedScript = FindScript(snapshot.Scripts, preferredRelativePath)
            ?? FindScript(snapshot.Scripts, SelectedScript?.RelativePath)
            ?? snapshot.Scripts.FirstOrDefault();
        SelectedScript = selectedScript;
    }
    private void ApplyWorkspaceSnapshot(WorkspaceSnapshot snapshot, string? preferredProjectPath)
    {
        AppTitle = Text.Shell.AppTitle;
        HubRootDisplay = string.IsNullOrWhiteSpace(snapshot.Dashboard.HubRoot) ? Text.State.HubRootNotResolved : snapshot.Dashboard.HubRoot;
        HubStatus = snapshot.Dashboard.HubStatus;
        ProjectCountDisplay = Text.State.ProjectCount(snapshot.Dashboard.ProjectCount);
        ActiveScopeDisplay = Text.State.ActiveScope(snapshot.Settings.ActiveScope);
        DefaultProfileDisplay = snapshot.Settings.DefaultProfile.ToDisplayName();
        LastOpenedProjectDisplay = string.IsNullOrWhiteSpace(snapshot.Settings.LastOpenedProject) ? Text.State.NotSet : snapshot.Settings.LastOpenedProject;
        HubRootInput = string.IsNullOrWhiteSpace(snapshot.Resolution.RootPath)
            ? (snapshot.Settings.HubRoot ?? HubRootInput)
            : snapshot.Resolution.RootPath;
        AutoStartManagedMcpOnLoad = snapshot.Settings.AutoStartManagedMcpOnLoad;
        AutoCheckSkillUpdatesOnLoad = snapshot.Settings.AutoCheckSkillUpdatesOnLoad;
        AutoSyncSafeSkillsOnLoad = snapshot.Settings.AutoSyncSafeSkillsOnLoad;
        ApplyCurrentWorkspaceContext(snapshot.Settings.ActiveScope, snapshot.Settings.LastOpenedProject);

        ReplaceCollection(Projects, snapshot.Projects);
        ReplaceCollection(EnabledClients, snapshot.Dashboard.EnabledClients);
        ReplaceCollection(Modules, snapshot.Dashboard.Modules);
        ReplaceCollection(ReadinessItems, snapshot.Dashboard.ReadinessItems);
        ReplaceCollection(RemainingGates, snapshot.Dashboard.RemainingGates);
        ReplaceCollection(ValidationErrors, snapshot.Dashboard.ValidationErrors);
        HasValidationErrors = snapshot.Dashboard.ValidationErrors.Count > 0;

        var projectToSelect = FindProjectByPath(snapshot.Projects, preferredProjectPath)
            ?? FindProjectByPath(snapshot.Projects, SelectedProject?.Path)
            ?? FindProjectByPath(snapshot.Projects, snapshot.Settings.LastOpenedProject);

        if (projectToSelect is not null)
        {
            SelectedProject = projectToSelect;
        }
        else
        {
            SelectedProject = null;
            ClearFormFields(snapshot.Settings.DefaultProfile);
        }
    }

    private void ApplyMcpSnapshot(McpWorkspaceSnapshot snapshot, ProfileKind? preferredProfile, string? preferredProcessName)
    {
        _mcpProfileCache = snapshot.Profiles;
        ApplyRuntimeSummary(snapshot.RuntimeSummary);
        RememberManagedProcessRestartBaselines(snapshot.ManagedProcesses);
        McpManagedProcessDetails = snapshot.ManagedProcesses.Count == 0
            ? Text.State.NoManagedMcpDefinitions
            : string.Join(Environment.NewLine, snapshot.ManagedProcesses.Select(ProcessSummaryLine));

        var processItems = snapshot.ManagedProcesses
            .Select(record => new McpManagedProcessItem(record))
            .ToArray();
        ReplaceCollection(ManagedProcesses, processItems);

        var selectedProcess = FindManagedProcessItem(processItems, preferredProcessName)
            ?? FindManagedProcessItem(processItems, SelectedManagedProcess?.Name)
            ?? processItems.FirstOrDefault();
        SelectedManagedProcess = selectedProcess;

        var profileItems = snapshot.Profiles
            .Select(profile => new McpProfileListItem(profile.Profile, profile.ServerNames))
            .ToArray();
        ReplaceCollection(McpProfiles, profileItems);

        var selectedProfile = FindMcpProfileItem(profileItems, preferredProfile)
            ?? FindMcpProfileItem(profileItems, SelectedMcpProfile?.Profile)
            ?? profileItems.FirstOrDefault();
        SelectedMcpProfile = selectedProfile;

        if (selectedProfile is null)
        {
            ResetMcpPanel();
        }
    }

    private bool TryBuildDraftProject(out ProjectRecord project, out string validationError)
    {
        project = new ProjectRecord(string.Empty, string.Empty, ProfileKind.Global);
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            validationError = Text.State.ProjectNameRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            validationError = Text.State.ProjectPathRequired;
            return false;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(ProjectPath.Trim());
        }
        catch (Exception exception)
        {
            validationError = Text.State.InvalidProjectPath(exception.Message);
            return false;
        }

        if (!Directory.Exists(normalizedPath))
        {
            validationError = Text.State.ProjectDirectoryDoesNotExist(normalizedPath);
            return false;
        }

        var profile = SelectedProfileOption?.Value ?? ProfileKind.Global;
        project = new ProjectRecord(ProjectName.Trim(), normalizedPath, profile, IsPinned);
        return true;
    }

    private bool TryResolveProjectForScopeSwitch(out ProjectRecord project, out string validationError)
    {
        if (SelectedProject is not null)
        {
            project = SelectedProject;
            validationError = string.Empty;
            return true;
        }

        return TryBuildDraftProject(out project, out validationError);
    }

    private bool TryBuildManagedProcessDraft(out McpRuntimeRecord record, out string? originalName, out string validationError)
    {
        record = new McpRuntimeRecord();
        originalName = SelectedManagedProcess?.Name;
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(ManagedProcessName))
        {
            validationError = Text.State.ManagedProcessNameRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(ManagedProcessCommand))
        {
            validationError = Text.State.ManagedProcessCommandRequired;
            return false;
        }

        string[] arguments;
        try
        {
            arguments = SplitArguments(ManagedProcessArgumentsText);
        }
        catch (Exception exception)
        {
            validationError = Text.State.InvalidArgumentList(exception.Message);
            return false;
        }

        string? workingDirectory = null;
        if (!string.IsNullOrWhiteSpace(ManagedProcessWorkingDirectory))
        {
            try
            {
                workingDirectory = Path.GetFullPath(ManagedProcessWorkingDirectory.Trim());
            }
            catch (Exception exception)
            {
                validationError = Text.State.InvalidWorkingDirectory(exception.Message);
                return false;
            }
        }

        var timeoutText = string.IsNullOrWhiteSpace(ManagedProcessHealthTimeoutText) ? "5" : ManagedProcessHealthTimeoutText.Trim();
        if (!int.TryParse(timeoutText, out var timeoutSeconds) || timeoutSeconds <= 0)
        {
            validationError = Text.State.HealthTimeoutMustBePositiveInteger;
            return false;
        }

        var baseRecord = SelectedManagedProcess?.Record ?? new McpRuntimeRecord();
        record = baseRecord with
        {
            Name = ManagedProcessName.Trim(),
            Mode = McpServerMode.ProcessManaged,
            IsEnabled = ManagedProcessEnabled,
            AutoStart = ManagedProcessAutoStart,
            KeepAlive = ManagedProcessKeepAlive,
            Command = ManagedProcessCommand.Trim(),
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = baseRecord.EnvironmentVariables,
            HealthCheckUrl = string.IsNullOrWhiteSpace(ManagedProcessHealthCheckUrl) ? null : ManagedProcessHealthCheckUrl.Trim(),
            HealthCheckTimeoutSeconds = timeoutSeconds,
            LastHealthMessage = baseRecord.LastHealthMessage,
            LastHealthStatus = baseRecord.LastHealthStatus,
            LastCheckedAt = baseRecord.LastCheckedAt,
            StandardOutputLogPath = baseRecord.StandardOutputLogPath,
            StandardErrorLogPath = baseRecord.StandardErrorLogPath,
            LastOutputSnippet = baseRecord.LastOutputSnippet,
            LastErrorSnippet = baseRecord.LastErrorSnippet
        };

        return true;
    }

    private bool TryBuildSkillSourceDraft(out SkillSourceRecord record, out string validationError)
    {
        record = new SkillSourceRecord();
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(SkillSourceLocalName))
        {
            validationError = Text.State.SourceNameRequired;
            return false;
        }

        if (string.IsNullOrWhiteSpace(SkillSourceRepository))
        {
            validationError = Text.State.SourceLocationRequired;
            return false;
        }

        var selectedKind = SelectedSkillSourceKindOption?.Value ?? SkillSourceKind.GitRepository;
        var baseRecord = SelectedSkillSource ?? new SkillSourceRecord();
        int? scheduledInterval = SkillSourceAutoUpdate
            ? SelectedSkillScheduleIntervalOption?.Value ?? 24
            : null;
        var versionTrackingMode = ResolveSelectedSkillVersionTrackingMode(selectedKind);
        var pinnedTag = versionTrackingMode == SkillVersionTrackingMode.PinTag && !string.IsNullOrWhiteSpace(SkillSourcePinnedTag)
            ? SkillSourcePinnedTag.Trim()
            : null;
        record = baseRecord with
        {
            LocalName = SkillSourceLocalName.Trim(),
            Profile = SelectedSkillSourceProfileOption?.Value ?? ProfileKind.Global,
            Kind = selectedKind,
            Location = SkillSourceRepository.Trim(),
            CatalogPath = string.IsNullOrWhiteSpace(SkillSourcePath) ? null : SkillSourcePath.Trim(),
            Reference = selectedKind == SkillSourceKind.LocalDirectory
                ? string.Empty
                : (string.IsNullOrWhiteSpace(SkillSourceReference) ? "main" : SkillSourceReference.Trim()),
            IsEnabled = SkillSourceEnabled,
            AutoUpdate = SkillSourceAutoUpdate && scheduledInterval.HasValue,
            ScheduledUpdateIntervalHours = scheduledInterval,
            ScheduledUpdateAction = SelectedSkillScheduledActionOption?.Value ?? SkillScheduledUpdateAction.CheckOnly,
            VersionTrackingMode = versionTrackingMode,
            PinnedTag = pinnedTag
        };

        return true;
    }

    private void ApplyProjectToForm(ProjectRecord project)
    {
        ProjectName = project.Name;
        ProjectPath = project.Path;
        IsPinned = project.IsPinned;
        SelectedProfileOption = ProfileOptions.FirstOrDefault(option => option.Value == project.Profile)
            ?? ProfileOptions.FirstOrDefault(option => option.Value == ProfileKind.Global);
    }

    private void ClearFormFields(ProfileKind defaultProfile)
    {
        ProjectName = string.Empty;
        ProjectPath = string.Empty;
        IsPinned = false;
        SelectedProfileOption = ProfileOptions.FirstOrDefault(option => option.Value == defaultProfile)
            ?? ProfileOptions.FirstOrDefault(option => option.Value == ProfileKind.Global);
    }

    private void ApplySelectedManagedProcess()
    {
        if (SelectedManagedProcess is null)
        {
            ClearManagedProcessFormFields();
            ManagedProcessStatusDisplay = Text.State.NoManagedProcessSelected;
            ManagedProcessHealthStatusDisplay = Text.State.NotChecked;
            ManagedProcessLastMessageDisplay = Text.State.SaveDefinitionBeforeOperate;
            ManagedProcessOutputLogPath = Text.State.NotGenerated;
            ManagedProcessErrorLogPath = Text.State.NotGenerated;
            ManagedProcessOutputPreview = Text.State.NoLogs;
            ManagedProcessErrorPreview = Text.State.NoLogs;
            return;
        }

        var record = SelectedManagedProcess.Record;
        ManagedProcessName = record.Name;
        ManagedProcessCommand = record.Command;
        ManagedProcessArgumentsText = JoinArguments(record.Arguments);
        ManagedProcessWorkingDirectory = record.WorkingDirectory ?? string.Empty;
        ManagedProcessEnabled = record.IsEnabled;
        ManagedProcessAutoStart = record.AutoStart;
        ManagedProcessKeepAlive = record.KeepAlive;
        ManagedProcessHealthCheckUrl = record.HealthCheckUrl ?? string.Empty;
        ManagedProcessHealthTimeoutText = record.HealthCheckTimeoutSeconds.ToString();
        ManagedProcessStatusDisplay = ProcessSummaryLine(record);
        ManagedProcessHealthStatusDisplay = string.IsNullOrWhiteSpace(record.LastHealthStatus) ? Text.State.NotChecked : record.LastHealthStatus!;
        ManagedProcessLastMessageDisplay = string.IsNullOrWhiteSpace(record.LastHealthMessage) ? Text.State.NoStatusMessage : record.LastHealthMessage!;
        ManagedProcessOutputLogPath = record.StandardOutputLogPath ?? Text.State.NotGenerated;
        ManagedProcessErrorLogPath = record.StandardErrorLogPath ?? Text.State.NotGenerated;
        ManagedProcessOutputPreview = string.IsNullOrWhiteSpace(record.LastOutputSnippet) ? Text.State.NoLogs : record.LastOutputSnippet!;
        ManagedProcessErrorPreview = string.IsNullOrWhiteSpace(record.LastErrorSnippet) ? Text.State.NoLogs : record.LastErrorSnippet!;
    }

    private void ClearManagedProcessFormFields()
    {
        ManagedProcessName = string.Empty;
        ManagedProcessCommand = string.Empty;
        ManagedProcessArgumentsText = string.Empty;
        ManagedProcessWorkingDirectory = string.Empty;
        ManagedProcessEnabled = true;
        ManagedProcessAutoStart = false;
        ManagedProcessKeepAlive = false;
        ManagedProcessHealthCheckUrl = string.Empty;
        ManagedProcessHealthTimeoutText = "5";
    }

    private void ApplySelectedSkillSource()
    {
        if (SelectedSkillSource is null)
        {
            ClearSkillSourceFormFields();
            return;
        }

        SkillSourceLocalName = SelectedSkillSource.LocalName;
        SkillSourceRepository = SelectedSkillSource.Location;
        SkillSourcePath = SelectedSkillSource.CatalogPath ?? string.Empty;
        SkillSourceReference = string.IsNullOrWhiteSpace(SelectedSkillSource.Reference) ? "main" : SelectedSkillSource.Reference;
        SkillSourceEnabled = SelectedSkillSource.IsEnabled;
        SkillSourceAutoUpdate = SelectedSkillSource.AutoUpdate;
        SelectedSkillSourceKindOption = SkillSourceKindOptions.FirstOrDefault(option => option.Value == SelectedSkillSource.Kind)
            ?? SkillSourceKindOptions.FirstOrDefault(option => option.Value == SkillSourceKind.GitRepository);
        SelectedSkillSourceProfileOption = SkillSourceProfileOptions.FirstOrDefault(option => option.Value == SelectedSkillSource.Profile)
            ?? SkillSourceProfileOptions.FirstOrDefault(option => option.Value == ProfileKind.Global);
        ApplySelectedSkillSourceReference(SelectedSkillSource);
        ApplySkillSourceScheduleState(SelectedSkillSource);
        ApplySkillSourceVersionState(SelectedSkillSource);
    }

    private void ClearSkillSourceFormFields()
    {
        SkillSourceLocalName = string.Empty;
        SkillSourceRepository = string.Empty;
        SkillSourcePath = string.Empty;
        SkillSourceReference = "main";
        SkillSourceEnabled = true;
        SkillSourceAutoUpdate = true;
        SelectedSkillSourceKindOption = SkillSourceKindOptions.FirstOrDefault(option => option.Value == SkillSourceKind.GitRepository);
        SelectedSkillSourceProfileOption = SkillSourceProfileOptions.FirstOrDefault(option => option.Value == ProfileKind.Global);
        SelectedSkillSourceReferenceOption = null;
        ApplySkillSourceScheduleState(null);
        ApplySkillSourceVersionState(null);
    }
    private void ApplySelectedScript()
    {
        if (SelectedScript is null)
        {
            ScriptDescriptionDisplay = Text.State.SelectScript;
            ScriptExecutionHint = Text.State.ScriptUsagePlaceholder;
            return;
        }

        ScriptDescriptionDisplay = SelectedScript.Description;
        ScriptExecutionHint = Text.State.HintPrefix + SelectedScript.CommandHint;

        if (SelectedScript.UsesProjectPath && string.IsNullOrWhiteSpace(ScriptProjectPath) && SelectedProject is not null)
        {
            ScriptProjectPath = SelectedProject.Path;
        }

        if (SelectedScript.UsesUserHome && string.IsNullOrWhiteSpace(ScriptUserHome))
        {
            ScriptUserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    private void ApplySelectedMcpProfile()
    {
        if (SelectedMcpProfile is null)
        {
            ResetMcpPanel();
            return;
        }

        var profileRecord = _mcpProfileCache.FirstOrDefault(profile => profile.Profile == SelectedMcpProfile.Profile);
        if (profileRecord is null)
        {
            ResetMcpPanel();
            return;
        }

        McpManifestPathDisplay = profileRecord.ManifestPath;
        McpServerSummary = Text.State.ServersSummary(profileRecord.ServerNames);
        McpManifestEditor = profileRecord.RawJson;

        var claudeConfig = FindGeneratedClient(profileRecord, "Claude");
        var codexConfig = FindGeneratedClient(profileRecord, "Codex");
        var antigravityConfig = FindGeneratedClient(profileRecord, "Antigravity");

        McpClaudePathDisplay = claudeConfig?.FilePath ?? Text.State.NotGenerated;
        McpCodexPathDisplay = codexConfig?.FilePath ?? Text.State.NotGenerated;
        McpAntigravityPathDisplay = antigravityConfig?.FilePath ?? Text.State.NotGenerated;
        McpClaudePreview = claudeConfig?.Content ?? Text.State.ConfigNotGenerated;
        McpCodexPreview = codexConfig?.Content ?? Text.State.ConfigNotGenerated;
        McpAntigravityPreview = antigravityConfig?.Content ?? Text.State.ConfigNotGenerated;
        UpdateMcpValidationSelectionState();
    }

    private void ResetMcpPanel()
    {
        McpManifestPathDisplay = Text.State.NotSelected;
        McpServerSummary = Text.State.McpConfigNotLoaded;
        McpManifestEditor = "{\r\n  \"mcpServers\": {}\r\n}";
        McpClaudePathDisplay = Text.State.NotGenerated;
        McpCodexPathDisplay = Text.State.NotGenerated;
        McpAntigravityPathDisplay = Text.State.NotGenerated;
        McpClaudePreview = Text.State.ConfigNotGenerated;
        McpCodexPreview = Text.State.ConfigNotGenerated;
        McpAntigravityPreview = Text.State.ConfigNotGenerated;
        UpdateMcpValidationSelectionState();
    }

    private void ApplyOperationResult(OperationResult result)
    {
        SetOperation(result.Success, result.Message, result.Details ?? string.Empty);
    }

    private void SetOperation(bool success, string message, string details)
    {
        OperationSummary = Text.State.OperationSummary(success, message);
        OperationDetails = details;
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        SaveProjectCommand.RaiseCanExecuteChanged();
        DeleteSelectedProjectCommand.RaiseCanExecuteChanged();
        ApplyGlobalLinksCommand.RaiseCanExecuteChanged();
        ApplyProjectProfileCommand.RaiseCanExecuteChanged();
        SetCurrentProjectCommand.RaiseCanExecuteChanged();
        ClearFormCommand.RaiseCanExecuteChanged();
        SaveMcpManifestCommand.RaiseCanExecuteChanged();
        GenerateMcpConfigsCommand.RaiseCanExecuteChanged();
        SaveManagedProcessCommand.RaiseCanExecuteChanged();
        DeleteManagedProcessCommand.RaiseCanExecuteChanged();
        StartManagedProcessCommand.RaiseCanExecuteChanged();
        StopManagedProcessCommand.RaiseCanExecuteChanged();
        RestartManagedProcessCommand.RaiseCanExecuteChanged();
        RunManagedProcessHealthCheckCommand.RaiseCanExecuteChanged();
        ClearManagedProcessFormCommand.RaiseCanExecuteChanged();
        SaveSkillSourceCommand.RaiseCanExecuteChanged();
        DeleteSkillSourceCommand.RaiseCanExecuteChanged();
        ClearSkillSourceFormCommand.RaiseCanExecuteChanged();
        SaveSkillInstallCommand.RaiseCanExecuteChanged();
        DeleteSkillInstallCommand.RaiseCanExecuteChanged();
        CaptureSkillBaselineCommand.RaiseCanExecuteChanged();
        PreviewSkillDiffCommand.RaiseCanExecuteChanged();
        ScanSkillSourceCommand.RaiseCanExecuteChanged();
        CheckSkillUpdateCommand.RaiseCanExecuteChanged();
        SyncSkillCommand.RaiseCanExecuteChanged();
        ForceSyncSkillCommand.RaiseCanExecuteChanged();
        RollbackSkillCommand.RaiseCanExecuteChanged();
        ExecuteScriptCommand.RaiseCanExecuteChanged();
        SaveHubRootCommand.RaiseCanExecuteChanged();
        SaveAutomationSettingsCommand.RaiseCanExecuteChanged();
        SwitchToGlobalScopeCommand.RaiseCanExecuteChanged();
        SwitchToSelectedProjectScopeCommand.RaiseCanExecuteChanged();
        RaiseFileDialogCommandStates();
        RaiseMaintenanceCommandStates();
        RaiseAdvancedCommandStates();
        RaiseDiagnosticsCommandStates();
    }

    private static ProjectRecord? FindProjectByPath(IEnumerable<ProjectRecord> projects, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return projects.FirstOrDefault(project => string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private static McpProfileListItem? FindMcpProfileItem(IEnumerable<McpProfileListItem> profiles, ProfileKind? profile)
    {
        if (!profile.HasValue)
        {
            return null;
        }

        return profiles.FirstOrDefault(item => item.Profile == profile.Value);
    }

    private static McpManagedProcessItem? FindManagedProcessItem(IEnumerable<McpManagedProcessItem> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return items.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static SkillSourceRecord? FindSkillSource(IEnumerable<SkillSourceRecord> items, string? localName, ProfileKind? profile)
    {
        if (string.IsNullOrWhiteSpace(localName) || !profile.HasValue)
        {
            return null;
        }

        return items.FirstOrDefault(item =>
            item.Profile == profile.Value &&
            string.Equals(item.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static ScriptDefinitionRecord? FindScript(IEnumerable<ScriptDefinitionRecord> items, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return items.FirstOrDefault(item => string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static McpGeneratedClientConfig? FindGeneratedClient(McpProfileRecord profileRecord, string clientName)
    {
        return profileRecord.GeneratedClients.FirstOrDefault(client =>
            string.Equals(client.ClientName, clientName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ProcessSummaryLine(McpRuntimeRecord record)
    {
        return DesktopTextCatalog.Default.State.ProcessSummary(
            record.Name,
            record.IsEnabled,
            record.IsRunning,
            record.ProcessId,
            record.LastHealthStatus);
    }

    private static ProfileOption[] CreateProfileOptions()
    {
        return
        [
            new ProfileOption(ProfileKind.Global),
            new ProfileOption(ProfileKind.Frontend),
            new ProfileOption(ProfileKind.Backend)
        ];
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string JoinArguments(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return "\"\"";
        }

        return argument.IndexOfAny([' ', '\t', '"']) >= 0
            ? "\"" + argument.Replace("\"", "\\\"") + "\""
            : argument;
    }

    private static string[] SplitArguments(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        foreach (var character in rawValue)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    arguments.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException(DesktopTextCatalog.Default.State.UnclosedQuoteDetected);
        }

        if (builder.Length > 0)
        {
            arguments.Add(builder.ToString());
        }

        return arguments.ToArray();
    }
}



