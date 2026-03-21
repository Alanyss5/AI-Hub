using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private HubSettingsRecord _currentWorkspaceSettings = new();
    private string _globalOnboardingStatusDisplay = DefaultText.State.OnboardingPending;
    private string _projectOnboardingStatusDisplay = DefaultText.State.OnboardingProjectNotSelected;

    public AsyncDelegateCommand RescanGlobalOnboardingCommand { get; private set; } = null!;

    public AsyncDelegateCommand RescanProjectOnboardingCommand { get; private set; } = null!;

    public Func<WorkspaceOnboardingDialogRequest, Task<WorkspaceOnboardingDialogResult?>>? WorkspaceOnboardingDialogHandler { get; set; }

    public string GlobalOnboardingStatusDisplay
    {
        get => _globalOnboardingStatusDisplay;
        private set => SetProperty(ref _globalOnboardingStatusDisplay, value);
    }

    public string ProjectOnboardingStatusDisplay
    {
        get => _projectOnboardingStatusDisplay;
        private set => SetProperty(ref _projectOnboardingStatusDisplay, value);
    }

    private void InitializeOnboardingState()
    {
        RescanGlobalOnboardingCommand = new AsyncDelegateCommand(() => ExecuteGlobalOnboardingFlowAsync(forceRescan: true), CanUseWorkspace);
        RescanProjectOnboardingCommand = new AsyncDelegateCommand(ExecuteSelectedProjectOnboardingRescanAsync, CanUseSelectedWorkspaceProject);
    }

    private void RaiseOnboardingCommandStates()
    {
        RescanGlobalOnboardingCommand.RaiseCanExecuteChanged();
        RescanProjectOnboardingCommand.RaiseCanExecuteChanged();
    }

    private async Task ExecuteSelectedProjectOnboardingRescanAsync()
    {
        if (!TryResolveSelectedProjectForWorkspaceAction(out var project, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await ExecuteProjectOnboardingFlowAsync(project, forceRescan: true);
    }

    private async Task ExecuteGlobalOnboardingFlowAsync(bool forceRescan)
    {
        await RunBusyAsync(async () =>
        {
            if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ScriptExecution))
            {
                return;
            }

            var previewResult = await _workspaceControlService!.PreviewGlobalOnboardingAsync(forceRescan);
            if (!previewResult.Success || previewResult.Preview is null)
            {
                SetOperation(false, previewResult.Message, previewResult.Details ?? string.Empty);
                return;
            }

            IReadOnlyList<WorkspaceImportDecisionRecord>? decisions = null;
            if (previewResult.Preview.Candidates.Count > 0 && (forceRescan || previewResult.Preview.RequiresDecision))
            {
                var dialogResult = await ShowWorkspaceOnboardingDialogAsync(
                    new WorkspaceOnboardingDialogRequest(
                        Text.Dialogs.GlobalOnboardingDialogTitle,
                        Text.State.GlobalOnboardingDialogMessage(forceRescan),
                        previewResult.Preview));
                if (dialogResult is null)
                {
                    SetOperation(false, Text.State.OnboardingCancelled, previewResult.Preview.Summary);
                    return;
                }

                decisions = dialogResult.Decisions;
            }
            else if (forceRescan)
            {
                await ShowRescanEmptyNoticeAsync(WorkspaceScope.Global, HubRootDisplay, null, previewResult.Preview.Summary, Text.State.NoGlobalReimportableResources);
                return;
            }

            var result = await _workspaceControlService.ApplyGlobalLinksAsync(decisions);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(SelectedProject?.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task ExecuteProjectOnboardingFlowAsync(ProjectRecord project, bool forceRescan)
    {
        await RunBusyAsync(async () =>
        {
            if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ScriptExecution))
            {
                return;
            }

            var previewResult = await _workspaceControlService!.PreviewProjectOnboardingAsync(project, forceRescan);
            if (!previewResult.Success || previewResult.Preview is null)
            {
                SetOperation(false, previewResult.Message, previewResult.Details ?? string.Empty);
                return;
            }

            IReadOnlyList<WorkspaceImportDecisionRecord>? decisions = null;
            if (previewResult.Preview.Candidates.Count > 0 && (forceRescan || previewResult.Preview.RequiresDecision))
            {
                var dialogResult = await ShowWorkspaceOnboardingDialogAsync(
                    new WorkspaceOnboardingDialogRequest(
                        Text.Dialogs.ProjectOnboardingDialogTitle,
                        Text.State.ProjectOnboardingDialogMessage(forceRescan),
                        previewResult.Preview));
                if (dialogResult is null)
                {
                    SetOperation(false, Text.State.OnboardingCancelled, previewResult.Preview.Summary);
                    return;
                }

                decisions = dialogResult.Decisions;
            }
            else if (forceRescan)
            {
                await ShowRescanEmptyNoticeAsync(WorkspaceScope.Project, project.Path, project.Profile, previewResult.Preview.Summary, Text.State.NoProjectReimportableResources);
                return;
            }

            var result = await _workspaceControlService.ApplyProjectProfileAsync(project, decisions);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await ReloadAllAsync(project.Path, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
            }
        });
    }

    private async Task ShowRescanEmptyNoticeAsync(
        WorkspaceScope scope,
        string checkedPath,
        string? profileId,
        string summary,
        string message)
    {
        var details = new List<string>
        {
            Text.State.DetailScopeLabel + Text.State.RescanScope(scope),
            Text.State.CheckedPathLabel + checkedPath
        };

        if (scope == WorkspaceScope.Project && !string.IsNullOrWhiteSpace(profileId))
        {
            details.Add(Text.State.CurrentProfileLabel + WorkspaceProfiles.ToDisplayName(profileId));
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            details.Add(summary);
        }

        SetOperation(true, message, string.Join(Environment.NewLine, details));
        await ShowNoticeAsync(new NoticeDialogRequest(
            Text.Dialogs.RescanResultTitle,
            message,
            string.Join(Environment.NewLine, details),
            Text.Dialogs.NoticeConfirmText));
    }

    private async Task<WorkspaceOnboardingDialogResult?> ShowWorkspaceOnboardingDialogAsync(WorkspaceOnboardingDialogRequest request)
    {
        var handler = WorkspaceOnboardingDialogHandler;
        if (handler is null)
        {
            SetOperation(false, Text.State.OnboardingDialogNotReady, request.Preview.Summary);
            return null;
        }

        return await handler(request);
    }

    private void UpdateOnboardingStatusDisplay(HubSettingsRecord settings, ProjectRecord? project)
    {
        GlobalOnboardingStatusDisplay = settings.GlobalOnboardingCompleted
            ? Text.State.OnboardingCompleted(settings.GlobalOnboardingCompletedAt)
            : Text.State.OnboardingPending;

        if (project is null)
        {
            ProjectOnboardingStatusDisplay = Text.State.OnboardingProjectNotSelected;
            return;
        }

        ProjectOnboardingStatusDisplay = settings.OnboardedProjectPaths.Any(path => PathsMatch(path, project.Path))
            ? Text.State.OnboardingCompleted(null)
            : Text.State.OnboardingPending;
    }

    private static bool PathsMatch(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record WorkspaceOnboardingDialogRequest(
    string Title,
    string Message,
    WorkspaceOnboardingPreview Preview);

public sealed record WorkspaceOnboardingDialogResult(
    IReadOnlyList<WorkspaceImportDecisionRecord> Decisions);
