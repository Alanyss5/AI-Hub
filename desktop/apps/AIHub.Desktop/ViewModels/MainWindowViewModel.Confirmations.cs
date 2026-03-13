using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private SkillBackupRecord? _selectedSkillBackup;
    private string? _selectedSkillSourceReferenceOption;
    private int _mcpManagedProcessCount;
    private int _mcpRunningProcessCount;
    private int _mcpStoppedProcessCount;
    private int _mcpAlertProcessCount;
    private int _mcpRecoverableProcessCount;
    private int _mcpAttentionProcessCount;
    private int _mcpSuspendedProcessCount;

    public Func<ConfirmationRequest, Task<bool>>? ConfirmationHandler { get; set; }

    public SkillBackupRecord? SelectedSkillBackup
    {
        get => _selectedSkillBackup;
        set => SetProperty(ref _selectedSkillBackup, value);
    }

    public string? SelectedSkillSourceReferenceOption
    {
        get => _selectedSkillSourceReferenceOption;
        set
        {
            if (SetProperty(ref _selectedSkillSourceReferenceOption, value) && !string.IsNullOrWhiteSpace(value))
            {
                SkillSourceReference = value;
            }
        }
    }

    public string TrayToolTipText => string.Join(Environment.NewLine, new[]
    {
        Text.Shell.AppTitle,
        TrayRuntimeSummaryDisplay,
        TrayAlertSummaryDisplay
    });

    public string TrayRuntimeSummaryDisplay =>
        $"MCP：总计 {_mcpManagedProcessCount} / 运行 {_mcpRunningProcessCount} / 停止 {_mcpStoppedProcessCount} / 暂停 {_mcpSuspendedProcessCount} / 告警 {_mcpAlertProcessCount}";

    public string TrayAlertSummaryDisplay =>
        $"可恢复 {_mcpRecoverableProcessCount} / 需关注 {_mcpAttentionProcessCount} / 被监督暂停 {_mcpSuspendedProcessCount}";

    private async Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        var handler = ConfirmationHandler;
        if (handler is null)
        {
            SetOperation(false, Text.State.AppNotReadyForConfirmation, request.Message);
            return false;
        }

        return await handler(request);
    }

    private void ApplySelectedSkillBackups(InstalledSkillRecord? installedSkill)
    {
        SelectedSkillBackup = installedSkill?.BackupRecords.FirstOrDefault();
    }

    private void ApplySelectedSkillSourceReference(SkillSourceRecord? source)
    {
        var selected = source?.AvailableReferences.FirstOrDefault(item => string.Equals(item, source.Reference, StringComparison.OrdinalIgnoreCase));
        if (!SetProperty(ref _selectedSkillSourceReferenceOption, selected, nameof(SelectedSkillSourceReferenceOption)) && selected is null)
        {
            RaisePropertyChanged(nameof(SelectedSkillSourceReferenceOption));
        }
    }

    private void ApplyRuntimeSummary(McpRuntimeSummary summary)
    {
        _mcpManagedProcessCount = summary.ManagedProcessCount;
        _mcpRunningProcessCount = summary.RunningProcessCount;
        _mcpStoppedProcessCount = summary.StoppedProcessCount;
        _mcpAlertProcessCount = summary.AlertProcessCount;
        _mcpRecoverableProcessCount = summary.RecoverableProcessCount;
        _mcpAttentionProcessCount = summary.AttentionProcessCount;
        _mcpSuspendedProcessCount = summary.SuspendedProcessCount;

        McpRuntimeSummaryDisplay = $"托管进程：{summary.ManagedProcessCount} / 运行中：{summary.RunningProcessCount} / 已停止：{summary.StoppedProcessCount} / 已暂停：{summary.SuspendedProcessCount} / 告警：{summary.AlertProcessCount}";

        RaisePropertyChanged(nameof(TrayToolTipText));
        RaisePropertyChanged(nameof(TrayRuntimeSummaryDisplay));
        RaisePropertyChanged(nameof(TrayAlertSummaryDisplay));
    }
}
