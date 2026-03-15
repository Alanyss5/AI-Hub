using System.IO;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _scriptRiskAccepted;
    private bool _managedMcpRiskAccepted;
    private bool _externalMcpRiskAccepted;
    private string _diagnosticsPackagePath = string.Empty;
    private string _diagnosticsRootDisplay = DefaultText.State.DiagnosticsNotLoaded;
    private string _diagnosticsSummaryDisplay = DefaultText.State.DiagnosticsNotLoaded;
    private string _latestStartupFailureDisplay = DefaultText.State.NoStartupFailureRecorded;
    private string _latestUnhandledExceptionDisplay = DefaultText.State.NoUnhandledExceptionRecorded;
    private string _scriptRiskConsentDisplay = DefaultText.State.RiskConsentPending;
    private string _managedMcpRiskConsentDisplay = DefaultText.State.RiskConsentPending;
    private string _externalMcpRiskConsentDisplay = DefaultText.State.RiskConsentPending;

    public IDiagnosticLogService? DiagnosticLogService { private get; set; }

    public AsyncDelegateCommand ExportDiagnosticsCommand { get; private set; } = null!;

    public AsyncDelegateCommand ResetRiskConfirmationsCommand { get; private set; } = null!;

    public string DiagnosticsPackagePath
    {
        get => _diagnosticsPackagePath;
        set => SetProperty(ref _diagnosticsPackagePath, value);
    }

    public string DiagnosticsRootDisplay
    {
        get => _diagnosticsRootDisplay;
        private set => SetProperty(ref _diagnosticsRootDisplay, value);
    }

    public string DiagnosticsSummaryDisplay
    {
        get => _diagnosticsSummaryDisplay;
        private set => SetProperty(ref _diagnosticsSummaryDisplay, value);
    }

    public string LatestStartupFailureDisplay
    {
        get => _latestStartupFailureDisplay;
        private set => SetProperty(ref _latestStartupFailureDisplay, value);
    }

    public string LatestUnhandledExceptionDisplay
    {
        get => _latestUnhandledExceptionDisplay;
        private set => SetProperty(ref _latestUnhandledExceptionDisplay, value);
    }

    public string ScriptRiskConsentDisplay
    {
        get => _scriptRiskConsentDisplay;
        private set => SetProperty(ref _scriptRiskConsentDisplay, value);
    }

    public string ManagedMcpRiskConsentDisplay
    {
        get => _managedMcpRiskConsentDisplay;
        private set => SetProperty(ref _managedMcpRiskConsentDisplay, value);
    }

    public string ExternalMcpRiskConsentDisplay
    {
        get => _externalMcpRiskConsentDisplay;
        private set => SetProperty(ref _externalMcpRiskConsentDisplay, value);
    }

    private void InitializeDiagnosticsState()
    {
        ExportDiagnosticsCommand = new AsyncDelegateCommand(ExportDiagnosticsAsync, CanExportDiagnostics);
        ResetRiskConfirmationsCommand = new AsyncDelegateCommand(ResetRiskConfirmationsAsync, CanResetRiskConfirmations);
    }

    private void RaiseDiagnosticsCommandStates()
    {
        ExportDiagnosticsCommand?.RaiseCanExecuteChanged();
        ResetRiskConfirmationsCommand?.RaiseCanExecuteChanged();
    }

    private bool CanExportDiagnostics()
    {
        return !IsBusy && DiagnosticLogService is not null && !string.IsNullOrWhiteSpace(DiagnosticsPackagePath);
    }

    private bool CanResetRiskConfirmations()
    {
        return !IsBusy && _workspaceControlService is not null && (_scriptRiskAccepted || _managedMcpRiskAccepted || _externalMcpRiskAccepted);
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (string.IsNullOrWhiteSpace(DiagnosticsPackagePath))
        {
            SetOperation(false, Text.State.DiagnosticsExportPathRequired, string.Empty);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await DiagnosticLogService!.ExportBundleAsync(DiagnosticsPackagePath, HubRootInput);
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadDiagnosticsAsync();
            }
        });
    }

    private async Task ResetRiskConfirmationsAsync()
    {
        var confirmed = await ConfirmAsync(CreateResetRiskConfirmationsConfirmation());
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _workspaceControlService!.ResetRiskAcceptancesAsync();
            ApplyOperationResult(result);

            if (result.Success)
            {
                await LoadWorkspaceAsync(SelectedProject?.Path);
            }
        });
    }

    private async Task LoadDiagnosticsAsync()
    {
        if (DiagnosticLogService is null)
        {
            DiagnosticsRootDisplay = Text.State.DiagnosticsNotLoaded;
            DiagnosticsSummaryDisplay = Text.State.DiagnosticsNotLoaded;
            LatestStartupFailureDisplay = Text.State.NoStartupFailureRecorded;
            LatestUnhandledExceptionDisplay = Text.State.NoUnhandledExceptionRecorded;
            return;
        }

        var snapshot = await DiagnosticLogService.LoadSnapshotAsync();
        ApplyDiagnosticSnapshot(snapshot);
    }

    private void ApplyDiagnosticSnapshot(DiagnosticSnapshot snapshot)
    {
        DiagnosticsRootDisplay = snapshot.DiagnosticsRoot;
        DiagnosticsSummaryDisplay = Text.State.DiagnosticsSummary(snapshot.DiagnosticsRoot, snapshot.LastExportedAt, snapshot.LastExportPath);
        LatestStartupFailureDisplay = string.IsNullOrWhiteSpace(snapshot.LatestStartupFailureSummary)
            ? Text.State.NoStartupFailureRecorded
            : Text.State.DiagnosticEvent(snapshot.LastStartupFailureAt, snapshot.LatestStartupFailureSummary, snapshot.LatestStartupFailureDetails);
        LatestUnhandledExceptionDisplay = string.IsNullOrWhiteSpace(snapshot.LatestUnhandledExceptionSummary)
            ? Text.State.NoUnhandledExceptionRecorded
            : Text.State.DiagnosticEvent(snapshot.LastUnhandledExceptionAt, snapshot.LatestUnhandledExceptionSummary, snapshot.LatestUnhandledExceptionDetails);
    }

    private void ApplyRiskConfirmations(HubSettingsRecord settings)
    {
        _scriptRiskAccepted = settings.ScriptExecutionRiskAccepted;
        _managedMcpRiskAccepted = settings.ManagedMcpRiskAccepted;
        _externalMcpRiskAccepted = settings.ExternalMcpImportRiskAccepted;

        ScriptRiskConsentDisplay = Text.State.RiskConsentStatus(settings.ScriptExecutionRiskAccepted, settings.ScriptExecutionRiskAcceptedAt);
        ManagedMcpRiskConsentDisplay = Text.State.RiskConsentStatus(settings.ManagedMcpRiskAccepted, settings.ManagedMcpRiskAcceptedAt);
        ExternalMcpRiskConsentDisplay = Text.State.RiskConsentStatus(settings.ExternalMcpImportRiskAccepted, settings.ExternalMcpImportRiskAcceptedAt);
        RaiseDiagnosticsCommandStates();
    }

    private async Task<bool> EnsureRiskConfirmedAsync(HubRiskConsentKind kind)
    {
        if (IsRiskConfirmed(kind))
        {
            return true;
        }

        var confirmed = await ConfirmAsync(CreateRiskConfirmation(kind));
        if (!confirmed)
        {
            return false;
        }

        if (_workspaceControlService is null)
        {
            SetOperation(false, Text.State.WorkspaceServiceNotReady, string.Empty);
            return false;
        }

        var result = await _workspaceControlService.ConfirmRiskAcceptanceAsync(kind);
        ApplyOperationResult(result);
        if (!result.Success)
        {
            return false;
        }

        await LoadWorkspaceAsync(SelectedProject?.Path);
        await LoadDiagnosticsAsync();
        return IsRiskConfirmed(kind);
    }

    private bool IsRiskConfirmed(HubRiskConsentKind kind)
    {
        return kind switch
        {
            HubRiskConsentKind.ScriptExecution => _scriptRiskAccepted,
            HubRiskConsentKind.ManagedMcpExecution => _managedMcpRiskAccepted,
            HubRiskConsentKind.ExternalMcpImport => _externalMcpRiskAccepted,
            _ => false
        };
    }

    public void SetDiagnosticsPackagePathFromPicker(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            DiagnosticsPackagePath = Path.GetFullPath(path);
        }
    }
}
