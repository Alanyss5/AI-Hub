using System.Collections.ObjectModel;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;
using AIHub.Desktop.Text;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, AlertState> _maintenanceAlertStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _managedProcessSessionRestartBaselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _managedProcessHealthAlertStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _managedProcessSuspendedStates = new(StringComparer.OrdinalIgnoreCase);
    private WorkspaceScope _currentWorkspaceScope = WorkspaceScope.Global;
    private string? _currentScopeProjectPath;
    private SkillScheduleIntervalOption? _selectedSkillScheduleIntervalOption;
    private SkillScheduledActionOption? _selectedSkillScheduledActionOption;
    private string _skillSourceScheduledNextRunDisplay = DefaultText.State.RuntimeScheduledUpdateDisabled;
    private string _skillMergeSummaryDisplay = DefaultText.State.OverlayPreviewNotSupported;
    private bool _hasSkillMergePreview;
    private McpValidationSnapshot? _lastMcpValidationSnapshot;
    private string _mcpValidationScopeDisplay = DefaultText.State.McpValidationScopeNotLoaded;
    private string _mcpValidationSummaryDisplay = DefaultText.State.McpValidationNotRun;
    private bool _syncImportedExternalServers = true;

    public INotificationService? NotificationService { private get; set; }

    public ObservableCollection<SkillScheduleIntervalOption> SkillScheduleIntervalOptions { get; } =
        new(CreateSkillScheduleIntervalOptions(DefaultText));

    public ObservableCollection<SkillScheduledActionOption> SkillScheduledActionOptions { get; } =
        new(CreateSkillScheduledActionOptions(DefaultText));

    public ObservableCollection<SkillMergeFileItem> SkillMergeFiles { get; } = new();

    public ObservableCollection<McpClientConfigStatusRecord> McpClientStatuses { get; } = new();

    public ObservableCollection<McpValidationIssueRecord> McpValidationIssues { get; } = new();

    public ObservableCollection<McpExternalServerImportItem> McpExternalServerImports { get; } = new();

    public AsyncDelegateCommand RunSelectedSkillScheduledUpdateCommand { get; private set; } = null!;

    public AsyncDelegateCommand PreviewOverlayMergeCommand { get; private set; } = null!;

    public AsyncDelegateCommand ApplyOverlayMergeCommand { get; private set; } = null!;

    public AsyncDelegateCommand ValidateCurrentMcpScopeCommand { get; private set; } = null!;

    public AsyncDelegateCommand SyncCurrentMcpClientsCommand { get; private set; } = null!;

    public AsyncDelegateCommand ImportExternalMcpCommand { get; private set; } = null!;

    public SkillScheduleIntervalOption? SelectedSkillScheduleIntervalOption
    {
        get => _selectedSkillScheduleIntervalOption;
        set
        {
            if (SetProperty(ref _selectedSkillScheduleIntervalOption, value))
            {
                UpdateSkillSourceScheduleDisplay();
            }
        }
    }

    public SkillScheduledActionOption? SelectedSkillScheduledActionOption
    {
        get => _selectedSkillScheduledActionOption;
        set => SetProperty(ref _selectedSkillScheduledActionOption, value);
    }

    public string SkillSourceScheduledNextRunDisplay
    {
        get => _skillSourceScheduledNextRunDisplay;
        private set => SetProperty(ref _skillSourceScheduledNextRunDisplay, value);
    }

    public string SkillMergeSummaryDisplay
    {
        get => _skillMergeSummaryDisplay;
        private set => SetProperty(ref _skillMergeSummaryDisplay, value);
    }

    public bool HasSkillMergePreview
    {
        get => _hasSkillMergePreview;
        private set => SetProperty(ref _hasSkillMergePreview, value);
    }

    public string McpValidationScopeDisplay
    {
        get => _mcpValidationScopeDisplay;
        private set => SetProperty(ref _mcpValidationScopeDisplay, value);
    }

    public string McpValidationSummaryDisplay
    {
        get => _mcpValidationSummaryDisplay;
        private set => SetProperty(ref _mcpValidationSummaryDisplay, value);
    }

    public bool SyncImportedExternalServers
    {
        get => _syncImportedExternalServers;
        set => SetProperty(ref _syncImportedExternalServers, value);
    }

    private void InitializeMaintenanceState()
    {
        InitializeMaintenanceCommands();
        InitializeSkillVersioningState();
        SelectedSkillScheduleIntervalOption = SkillScheduleIntervalOptions.FirstOrDefault(option => option.Value == 24);
        SelectedSkillScheduledActionOption = SkillScheduledActionOptions.FirstOrDefault(option => option.Value == SkillScheduledUpdateAction.CheckOnly);
        UpdateSkillSourceScheduleDisplay();
        ResetSkillMergePreview();
        UpdateMcpValidationSelectionState();
    }

    private void InitializeMaintenanceCommands()
    {
        RunSelectedSkillScheduledUpdateCommand = new AsyncDelegateCommand(RunSelectedSkillScheduledUpdateAsync, CanRunSelectedSkillScheduledUpdate);
        PreviewOverlayMergeCommand = new AsyncDelegateCommand(PreviewOverlayMergeAsync, CanPreviewOverlayMerge);
        ApplyOverlayMergeCommand = new AsyncDelegateCommand(ApplyOverlayMergeAsync, CanApplyOverlayMerge);
        ValidateCurrentMcpScopeCommand = new AsyncDelegateCommand(ValidateCurrentMcpScopeAsync, CanValidateCurrentMcpScope);
        SyncCurrentMcpClientsCommand = new AsyncDelegateCommand(SyncCurrentMcpClientsAsync, CanSyncCurrentMcpClients);
        ImportExternalMcpCommand = new AsyncDelegateCommand(ImportExternalMcpAsync, CanImportExternalMcp);
    }

    private void RaiseMaintenanceCommandStates()
    {
        RunSelectedSkillScheduledUpdateCommand?.RaiseCanExecuteChanged();
        PreviewOverlayMergeCommand?.RaiseCanExecuteChanged();
        ApplyOverlayMergeCommand?.RaiseCanExecuteChanged();
        ValidateCurrentMcpScopeCommand?.RaiseCanExecuteChanged();
        SyncCurrentMcpClientsCommand?.RaiseCanExecuteChanged();
        ImportExternalMcpCommand?.RaiseCanExecuteChanged();
        RaiseSkillVersioningCommandStates();
    }

    private async Task RunSelectedSkillScheduledUpdateAsync()
    {
        if (SelectedSkillSource is null)
        {
            SetOperation(false, Text.State.SelectSkillsSourceFirst, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.RunScheduledUpdateForSourceAsync(
                SelectedSkillSource.LocalName,
                SelectedSkillSource.Profile);
            ApplyScheduledUpdateBatchResult(result, Text.State.SkillScheduledPolicyExecuted);
            await LoadSkillsAsync(SelectedSkillSource.LocalName, SelectedSkillSource.Profile);
            await PublishMaintenanceAlertsAsync(result.Sources.Select(item => item.Alert).Where(item => item is not null).Cast<MaintenanceAlertRecord>());
        });
    }

    private async Task PreviewOverlayMergeAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var preview = await _skillsCatalogService!.PreviewOverlayMergeAsync(installedSkill.Profile, installedSkill.RelativePath);
            if (preview is null || !preview.HasChanges)
            {
                ResetSkillMergePreview();
                SetOperation(true, Text.State.NoSourceDifferences, installedSkill.RelativePath);
                return;
            }

            ApplySkillMergePreview(preview);
            SetOperation(true, Text.State.OverlayMergePreviewGenerated, BuildSkillMergePreviewDetails(preview));
        });
    }

    private async Task ApplyOverlayMergeAsync()
    {
        if (!TryGetSelectedRegisteredSkill(out var installedSkill, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        if (SkillMergeFiles.Count == 0)
        {
            SetOperation(false, Text.State.OverlayMergePreviewRequired, string.Empty);
            return;
        }

        var confirmed = await ConfirmAsync(CreateApplyOverlayMergeConfirmation(installedSkill));
        if (!confirmed)
        {
            return;
        }

        var decisions = SkillMergeFiles.Select(item => item.BuildDecision()).ToArray();
        await RunBusyAsync(async () =>
        {
            var result = await _skillsCatalogService!.ApplyOverlayMergeAsync(installedSkill.Profile, installedSkill.RelativePath, decisions);
            ApplyOperationResult(result);

            if (result.Success)
            {
                ResetSkillMergePreview();
                await LoadSkillsAsync(installedSkill.SourceLocalName ?? SelectedSkillSource?.LocalName, installedSkill.SourceProfile ?? SelectedSkillSource?.Profile);
            }
        });
    }

    private async Task ValidateCurrentMcpScopeAsync()
    {
        if (!TryResolveCurrentMcpScope(out var scope, out var profile, out var projectPath, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var snapshot = await _mcpControlService!.ValidateCurrentScopeAsync(scope, profile, projectPath);
            ApplyMcpValidationSnapshot(snapshot);
            SetOperation(!snapshot.HasErrors, Text.State.McpValidationCompleted, BuildMcpValidationDetails(snapshot));
        });
    }

    private async Task SyncCurrentMcpClientsAsync()
    {
        if (!TryResolveCurrentMcpScope(out var scope, out var profile, out var projectPath, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.SyncCurrentScopeClientsAsync(scope, profile, projectPath);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
                await RefreshCurrentMcpValidationSnapshotAsync();
            }
        });
    }

    private async Task ImportExternalMcpAsync()
    {
        if (!TryResolveCurrentMcpScope(out var scope, out var profile, out var projectPath, out var validationError))
        {
            SetOperation(false, validationError, string.Empty);
            return;
        }

        var decisions = McpExternalServerImports
            .Select(item => item.BuildDecision())
            .Where(item => item is not null)
            .Cast<McpExternalServerImportDecision>()
            .ToArray();
        if (decisions.Length == 0)
        {
            SetOperation(false, Text.State.SelectExternalMcpFirst, string.Empty);
            return;
        }

        if (!await EnsureRiskConfirmedAsync(HubRiskConsentKind.ExternalMcpImport))
        {
            return;
        }

        var confirmed = await ConfirmAsync(CreateImportExternalMcpConfirmation(decisions));
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _mcpControlService!.ImportExternalServersAsync(scope, profile, projectPath, decisions, SyncImportedExternalServers);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadMcpAsync(SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);
                await RefreshCurrentMcpValidationSnapshotAsync();
            }
        });
    }
    public async Task RunBackgroundMaintenanceCycleAsync()
    {
        if (IsBusy || _mcpControlService is null || _skillsCatalogService is null)
        {
            return;
        }

        try
        {
            var alerts = new List<MaintenanceAlertRecord>();

            var scheduledResult = await _skillsCatalogService.RunDueScheduledUpdatesAsync();
            if (scheduledResult.Sources.Count > 0)
            {
                alerts.AddRange(scheduledResult.Sources.Select(item => item.Alert).Where(item => item is not null).Cast<MaintenanceAlertRecord>());
                await LoadSkillsAsync(SelectedSkillSource?.LocalName, SelectedSkillSource?.Profile);
            }

            await _mcpControlService.MaintainManagedProcessesAsync();
            var mcpSnapshot = await _mcpControlService.LoadAsync();
            ApplyMcpSnapshot(mcpSnapshot, SelectedMcpProfile?.Profile, SelectedManagedProcess?.Name);

            if (TryResolveCurrentMcpScope(out var scope, out var profile, out var projectPath, out _))
            {
                var validationSnapshot = await _mcpControlService.ValidateCurrentScopeAsync(scope, profile, projectPath);
                ApplyMcpValidationSnapshot(validationSnapshot);
                alerts.AddRange(BuildMcpMaintenanceAlerts(mcpSnapshot, validationSnapshot));
            }
            else
            {
                UpdateMcpValidationSelectionState();
            }

            await PublishMaintenanceAlertsAsync(alerts);
        }
        catch (Exception exception)
        {
            SetOperation(false, Text.State.BackgroundMaintenanceFailed, exception.Message);
        }
    }

    private void ApplySkillSourceScheduleState(SkillSourceRecord? source)
    {
        if (source is null)
        {
            SelectedSkillScheduleIntervalOption = SkillScheduleIntervalOptions.FirstOrDefault(option => option.Value == 24);
            SelectedSkillScheduledActionOption = SkillScheduledActionOptions.FirstOrDefault(option => option.Value == SkillScheduledUpdateAction.CheckOnly);
        }
        else
        {
            int? interval = source.AutoUpdate ? source.ScheduledUpdateIntervalHours ?? 24 : null;
            SelectedSkillScheduleIntervalOption = SkillScheduleIntervalOptions.FirstOrDefault(option => option.Value == interval)
                ?? SkillScheduleIntervalOptions.FirstOrDefault(option => option.Value == 24);
            SelectedSkillScheduledActionOption = SkillScheduledActionOptions.FirstOrDefault(option => option.Value == source.ScheduledUpdateAction)
                ?? SkillScheduledActionOptions.FirstOrDefault(option => option.Value == SkillScheduledUpdateAction.CheckOnly);
        }

        UpdateSkillSourceScheduleDisplay();
    }

    private void OnSkillSchedulePolicyChanged()
    {
        UpdateSkillSourceScheduleDisplay();
    }

    private void UpdateSkillSourceScheduleDisplay()
    {
        if (!SkillSourceAutoUpdate || SelectedSkillScheduleIntervalOption?.Value is not int intervalHours)
        {
            SkillSourceScheduledNextRunDisplay = Text.State.RuntimeScheduledUpdateDisabled;
            return;
        }

        if (SelectedSkillSource?.LastScheduledRunAt is DateTimeOffset lastRunAt)
        {
            SkillSourceScheduledNextRunDisplay = Text.State.NextRunAt(lastRunAt.AddHours(intervalHours));
            return;
        }

        SkillSourceScheduledNextRunDisplay = Text.State.NextRunEveryHours(intervalHours);
    }

    private void ApplySkillMergePreview(SkillMergePreview preview)
    {
        var options = CreateSkillMergeDecisionOptions();
        ReplaceCollection(SkillMergeFiles, preview.Files.Select(file => new SkillMergeFileItem(file, options)));
        HasSkillMergePreview = SkillMergeFiles.Count > 0;
        SkillMergeSummaryDisplay = Text.State.SkillMergeSummary(preview.SourceDisplayName, preview.SourceReference, SkillMergeFiles.Count);
        RaiseMaintenanceCommandStates();
    }

    private void ResetSkillMergePreview()
    {
        ReplaceCollection(SkillMergeFiles, Array.Empty<SkillMergeFileItem>());
        HasSkillMergePreview = false;
        SkillMergeSummaryDisplay = SelectedInstalledSkill?.CustomizationMode == SkillCustomizationMode.Overlay
            ? Text.State.OverlayPreviewReady
            : Text.State.OverlayPreviewNotSupported;
        RaiseMaintenanceCommandStates();
    }

    private void ApplyCurrentWorkspaceContext(WorkspaceScope scope, string? projectPath)
    {
        _currentWorkspaceScope = scope;
        _currentScopeProjectPath = scope == WorkspaceScope.Project && !string.IsNullOrWhiteSpace(projectPath)
            ? projectPath
            : null;
        RaisePropertyChanged(nameof(CurrentWorkspaceScope));
        UpdateMcpValidationSelectionState();
        SyncSkillFilterToCurrentScope(force: false);
    }

    private void RememberManagedProcessRestartBaselines(IEnumerable<McpRuntimeRecord> records)
    {
        var currentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            currentNames.Add(record.Name);
            _managedProcessSessionRestartBaselines.TryAdd(record.Name, record.RestartCount);
        }

        foreach (var staleName in _managedProcessSessionRestartBaselines.Keys.Where(name => !currentNames.Contains(name)).ToArray())
        {
            _managedProcessSessionRestartBaselines.Remove(staleName);
        }
    }

    private async Task RefreshCurrentMcpValidationSnapshotAsync()
    {
        if (!TryResolveCurrentMcpScope(out var scope, out var profile, out var projectPath, out _))
        {
            UpdateMcpValidationSelectionState();
            return;
        }

        var snapshot = await _mcpControlService!.ValidateCurrentScopeAsync(scope, profile, projectPath);
        ApplyMcpValidationSnapshot(snapshot);
    }

    private void ApplyMcpValidationSnapshot(McpValidationSnapshot snapshot)
    {
        _lastMcpValidationSnapshot = snapshot;
        RenderMcpValidationSnapshot(snapshot);
    }

    private void UpdateMcpValidationSelectionState()
    {
        McpValidationScopeDisplay = BuildMcpValidationScopeDisplay(
            _currentWorkspaceScope,
            ResolveCurrentMcpProfile(),
            _currentScopeProjectPath);

        if (_lastMcpValidationSnapshot is null)
        {
            ReplaceCollection(McpClientStatuses, Array.Empty<McpClientConfigStatusRecord>());
            ReplaceCollection(McpValidationIssues, Array.Empty<McpValidationIssueRecord>());
            ReplaceCollection(McpExternalServerImports, Array.Empty<McpExternalServerImportItem>());
            McpValidationSummaryDisplay = Text.State.McpValidationNotRun;
            RaiseMaintenanceCommandStates();
            return;
        }

        var expectedProfile = ResolveCurrentMcpProfile();
        var snapshotMatchesCurrentScope =
            _lastMcpValidationSnapshot.Scope == _currentWorkspaceScope &&
            _lastMcpValidationSnapshot.Profile == expectedProfile &&
            string.Equals(
                _lastMcpValidationSnapshot.ProjectPath ?? string.Empty,
                _currentScopeProjectPath ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

        if (!snapshotMatchesCurrentScope)
        {
            ReplaceCollection(McpClientStatuses, Array.Empty<McpClientConfigStatusRecord>());
            ReplaceCollection(McpValidationIssues, Array.Empty<McpValidationIssueRecord>());
            ReplaceCollection(McpExternalServerImports, Array.Empty<McpExternalServerImportItem>());
            McpValidationSummaryDisplay = Text.State.McpValidationSelectionChanged;
            RaiseMaintenanceCommandStates();
            return;
        }

        RenderMcpValidationSnapshot(_lastMcpValidationSnapshot);
    }

    private void RenderMcpValidationSnapshot(McpValidationSnapshot snapshot)
    {
        ReplaceCollection(McpClientStatuses, snapshot.ClientStatuses);
        ReplaceCollection(McpValidationIssues, snapshot.Issues);
        ReplaceCollection(McpExternalServerImports, snapshot.ExternalServers.Select(item => new McpExternalServerImportItem(item)));

        var errorCount = snapshot.Issues.Count(item => item.Severity == McpValidationSeverity.Error);
        var warningCount = snapshot.Issues.Count(item => item.Severity == McpValidationSeverity.Warning);
        var infoCount = snapshot.Issues.Count(item => item.Severity == McpValidationSeverity.Info);

        McpValidationScopeDisplay = BuildMcpValidationScopeDisplay(snapshot.Scope, snapshot.Profile, snapshot.ProjectPath);
        McpValidationSummaryDisplay = Text.State.McpValidationSummary(
            snapshot.ClientStatuses.Count,
            snapshot.Issues.Count,
            errorCount,
            warningCount,
            infoCount,
            snapshot.ExternalServers.Count);
        RaiseMaintenanceCommandStates();
    }

    private void ApplyScheduledUpdateBatchResult(SkillScheduledUpdateBatchResult result, string successMessage)
    {
        if (result.Sources.Count == 0)
        {
            SetOperation(true, Text.State.NoNeedToRunSourcePolicy, string.Empty);
            return;
        }

        var isSuccess = result.Sources.All(item => item.Success);
        var details = string.Join(
            Environment.NewLine + Environment.NewLine,
            result.Sources.Select(item => string.Join(
                Environment.NewLine,
                new[] { item.SourceDisplayName, item.Message, item.Details }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))));

        SetOperation(isSuccess, successMessage, details);
    }

    private async Task PublishMaintenanceAlertsAsync(IEnumerable<MaintenanceAlertRecord> alerts, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alert in alerts)
        {
            activeKeys.Add(alert.Key);

            if (!_maintenanceAlertStates.TryGetValue(alert.Key, out var state))
            {
                state = new AlertState(false, null);
            }

            if (!state.IsActive)
            {
                var shouldNotify = !state.LastNotifiedAt.HasValue || now - state.LastNotifiedAt.Value >= TimeSpan.FromMinutes(15);
                if (shouldNotify && NotificationService is not null)
                {
                    await NotificationService.NotifyAsync(alert, cancellationToken);
                    state = state with { LastNotifiedAt = now };
                }

                state = state with { IsActive = true };
                _maintenanceAlertStates[alert.Key] = state;
                continue;
            }

            _maintenanceAlertStates[alert.Key] = state with { IsActive = true };
        }

        foreach (var key in _maintenanceAlertStates.Keys.ToArray())
        {
            if (!activeKeys.Contains(key))
            {
                _maintenanceAlertStates[key] = _maintenanceAlertStates[key] with { IsActive = false };
            }
        }
    }

    private IReadOnlyList<MaintenanceAlertRecord> BuildMcpMaintenanceAlerts(
        McpWorkspaceSnapshot snapshot,
        McpValidationSnapshot validationSnapshot)
    {
        var alerts = new List<MaintenanceAlertRecord>();

        if (validationSnapshot.HasErrors)
        {
            var firstError = validationSnapshot.Issues.First(issue => issue.Severity == McpValidationSeverity.Error);
            alerts.Add(new MaintenanceAlertRecord(
                $"mcp-validation:{validationSnapshot.Scope}:{validationSnapshot.Profile}:{validationSnapshot.ProjectPath ?? string.Empty}",
                Text.State.McpValidationFailedTitle,
                firstError.Summary,
                BuildMcpValidationDetails(validationSnapshot)));
        }

        var activeNames = new HashSet<string>(snapshot.ManagedProcesses.Select(process => process.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var process in snapshot.ManagedProcesses)
        {
            var healthAlert = IsHealthAlert(process);
            var suspended = process.SupervisorState == McpSupervisorState.SuspendedBySupervisor;
            var hadHealthAlert = _managedProcessHealthAlertStates.TryGetValue(process.Name, out var previousHealthAlert) && previousHealthAlert;
            var wasSuspended = _managedProcessSuspendedStates.TryGetValue(process.Name, out var previousSuspended) && previousSuspended;

            if (suspended && !wasSuspended)
            {
                alerts.Add(new MaintenanceAlertRecord(
                    $"mcp-supervisor-suspended:{process.Name}",
                    Text.State.ManagedMcpSupervisorSuspendedTitle,
                    Text.State.ManagedMcpSupervisorSuspendedSummary(process.Name),
                    process.LastHealthMessage ?? process.Command));
            }
            else if (healthAlert && !hadHealthAlert)
            {
                alerts.Add(new MaintenanceAlertRecord(
                    $"mcp-health:{process.Name}",
                    Text.State.ManagedMcpHealthAlertTitle,
                    Text.State.ManagedMcpHealthAlertSummary(process.Name),
                    process.LastHealthMessage ?? process.LastHealthStatus));
            }
            else if (process.IsRunning && !healthAlert && !suspended && (hadHealthAlert || wasSuspended))
            {
                alerts.Add(new MaintenanceAlertRecord(
                    $"mcp-recovered:{process.Name}",
                    Text.State.ManagedMcpRecoveredTitle,
                    Text.State.ManagedMcpRecoveredSummary(process.Name),
                    process.LastHealthMessage ?? process.Command));
            }

            _managedProcessHealthAlertStates[process.Name] = healthAlert;
            _managedProcessSuspendedStates[process.Name] = suspended;
        }

        foreach (var staleName in _managedProcessHealthAlertStates.Keys.Where(name => !activeNames.Contains(name)).ToArray())
        {
            _managedProcessHealthAlertStates.Remove(staleName);
        }

        foreach (var staleName in _managedProcessSuspendedStates.Keys.Where(name => !activeNames.Contains(name)).ToArray())
        {
            _managedProcessSuspendedStates.Remove(staleName);
        }

        return alerts;
    }

    private bool TryResolveCurrentMcpScope(
        out WorkspaceScope scope,
        out string profileId,
        out string? projectPath,
        out string validationError)
    {
        scope = _currentWorkspaceScope;
        profileId = ResolveCurrentMcpProfile();
        projectPath = _currentWorkspaceScope == WorkspaceScope.Project ? _currentScopeProjectPath : null;
        validationError = string.Empty;

        if (_mcpControlService is null)
        {
            validationError = Text.State.McpServiceNotReady;
            return false;
        }

        return true;
    }

    private string ResolveCurrentMcpProfile()
    {
        return _currentWorkspaceScope == WorkspaceScope.Global
            ? WorkspaceProfiles.GlobalId
            : SelectedMcpProfile?.Profile ?? WorkspaceProfiles.GlobalId;
    }

    private bool CanRunSelectedSkillScheduledUpdate()
    {
        return !IsBusy && _skillsCatalogService is not null && SelectedSkillSource is not null;
    }

    private bool CanPreviewOverlayMerge()
    {
        return !IsBusy &&
               _skillsCatalogService is not null &&
               SelectedInstalledSkill?.IsRegistered == true &&
               SelectedInstalledSkill.CustomizationMode == SkillCustomizationMode.Overlay;
    }

    private bool CanApplyOverlayMerge()
    {
        return CanPreviewOverlayMerge() && SkillMergeFiles.Count > 0;
    }

    private bool CanValidateCurrentMcpScope()
    {
        return !IsBusy && _mcpControlService is not null && SelectedMcpProfile is not null;
    }

    private bool CanSyncCurrentMcpClients()
    {
        return !IsBusy && _mcpControlService is not null && SelectedMcpProfile is not null;
    }

    private bool CanImportExternalMcp()
    {
        return !IsBusy && _mcpControlService is not null && McpExternalServerImports.Count > 0;
    }

    private static bool IsHealthAlert(McpRuntimeRecord record)
    {
        return string.Equals(NormalizeHealthValue(record.LastHealthStatus), "异常", StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrWhiteSpace(record.LastHealthMessage)
                   && (record.LastHealthMessage.Contains("失败", StringComparison.OrdinalIgnoreCase)
                       || record.LastHealthMessage.Contains("错误", StringComparison.OrdinalIgnoreCase)
                       || record.LastHealthMessage.Contains("异常", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? NormalizeHealthValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Contains("异常", StringComparison.OrdinalIgnoreCase) || value.Contains("寮傚父", StringComparison.OrdinalIgnoreCase))
        {
            return "异常";
        }

        if (value.Contains("健康", StringComparison.OrdinalIgnoreCase))
        {
            return "健康";
        }

        return value;
    }

    private static IReadOnlyList<SkillScheduleIntervalOption> CreateSkillScheduleIntervalOptions(DesktopTextCatalog textCatalog)
    {
        return
        [
            new SkillScheduleIntervalOption(null, textCatalog.Skills.ScheduleIntervalOffOption),
            new SkillScheduleIntervalOption(6, textCatalog.Skills.ScheduleInterval6HoursOption),
            new SkillScheduleIntervalOption(12, textCatalog.Skills.ScheduleInterval12HoursOption),
            new SkillScheduleIntervalOption(24, textCatalog.Skills.ScheduleInterval24HoursOption),
            new SkillScheduleIntervalOption(168, textCatalog.Skills.ScheduleInterval7DaysOption)
        ];
    }

    private static IReadOnlyList<SkillScheduledActionOption> CreateSkillScheduledActionOptions(DesktopTextCatalog textCatalog)
    {
        return
        [
            new SkillScheduledActionOption(SkillScheduledUpdateAction.CheckOnly, textCatalog.Skills.ScheduledActionCheckOnlyOption),
            new SkillScheduledActionOption(SkillScheduledUpdateAction.CheckAndSyncSafe, textCatalog.Skills.ScheduledActionCheckAndSyncSafeOption)
        ];
    }

    private IReadOnlyList<SkillMergeDecisionOption> CreateSkillMergeDecisionOptions()
    {
        return
        [
            new SkillMergeDecisionOption(SkillMergeDecisionMode.UseSource, Text.Skills.MergeDecisionUseSourceOption),
            new SkillMergeDecisionOption(SkillMergeDecisionMode.KeepLocal, Text.Skills.MergeDecisionKeepLocalOption),
            new SkillMergeDecisionOption(SkillMergeDecisionMode.ApplyDeletion, Text.Skills.MergeDecisionApplyDeletionOption),
            new SkillMergeDecisionOption(SkillMergeDecisionMode.Skip, Text.Skills.MergeDecisionSkipOption)
        ];
    }

    private sealed record AlertState(bool IsActive, DateTimeOffset? LastNotifiedAt);
}
