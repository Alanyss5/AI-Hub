using System.Text;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ConfirmationRequest CreateDeleteProjectConfirmation(ProjectRecord project)
    {
        return new ConfirmationRequest(
            Text.Dialogs.DeleteProjectTitle,
            Text.Dialogs.DeleteProjectMessage,
            string.Join(Environment.NewLine, new[]
            {
                Text.State.DetailProjectNameLabel + project.Name,
                Text.State.DetailScopeLabel + WorkspaceProfiles.ToDisplayName(project.Profile),
                Text.State.DetailProjectPathLabel + project.Path
            }),
            ConfirmText: Text.Dialogs.DeleteProjectConfirmText);
    }

    private ConfirmationRequest CreateDeleteManagedProcessConfirmation(McpManagedProcessItem process)
    {
        return new ConfirmationRequest(
            Text.Dialogs.DeleteManagedProcessTitle,
            Text.Dialogs.DeleteManagedProcessMessage,
            string.Join(Environment.NewLine, new[]
            {
                Text.State.DetailNameLabel + process.Name,
                Text.State.DetailStatusLabel + process.StatusDisplay,
                Text.State.DetailCommandLabel + (process.Record.Command ?? string.Empty),
                Text.State.DetailWorkingDirectoryLabel + (process.Record.WorkingDirectory ?? Text.State.NotSet)
            }),
            ConfirmText: Text.Dialogs.DeleteManagedProcessConfirmText);
    }

    private ConfirmationRequest CreateDeleteSkillSourceConfirmation(SkillSourceRecord source)
    {
        return new ConfirmationRequest(
            Text.Dialogs.DeleteSkillSourceTitle,
            Text.Dialogs.DeleteSkillSourceMessage,
            string.Join(Environment.NewLine, new[]
            {
                Text.State.DetailSourceNameLabel + source.LocalName,
                Text.State.DetailScopeLabel + source.ProfileDisplay,
                Text.State.DetailSourceTypeLabel + source.KindDisplay,
                Text.State.DetailAddressLabel + source.LocationDisplay,
                Text.State.DetailReferenceLabel + source.ReferenceDisplay
            }),
            ConfirmText: Text.Dialogs.DeleteSkillSourceConfirmText);
    }

    private ConfirmationRequest CreateDeleteSkillInstallConfirmation(InstalledSkillRecord installedSkill)
    {
        return new ConfirmationRequest(
            Text.Dialogs.DeleteSkillInstallTitle,
            Text.Dialogs.DeleteSkillInstallMessage,
            BuildSkillTargetDetails(installedSkill),
            ConfirmText: Text.Dialogs.DeleteSkillInstallConfirmText);
    }

    private ConfirmationRequest CreateStopAllManagedProcessesConfirmation()
    {
        return new ConfirmationRequest(
            Text.Dialogs.StopAllManagedProcessesTitle,
            Text.Dialogs.StopAllManagedProcessesMessage,
            string.Join(Environment.NewLine, new[]
            {
                Text.State.DetailManagedProcessCountLabel + ManagedProcesses.Count,
                TrayRuntimeSummaryDisplay,
                TrayAlertSummaryDisplay
            }),
            ConfirmText: Text.Dialogs.StopAllManagedProcessesConfirmText);
    }

    private ConfirmationRequest CreateImportConfigurationPackageConfirmation(ConfigurationPackageImportPreview preview)
    {
        return new ConfirmationRequest(
            Text.Dialogs.ImportConfigurationPackageTitle,
            Text.Dialogs.ImportConfigurationPackageMessage,
            preview.Details,
            ConfirmText: Text.Dialogs.ImportConfigurationPackageConfirmText);
    }

    private ConfirmationRequest CreateForceSyncSkillConfirmation(InstalledSkillRecord installedSkill)
    {
        return new ConfirmationRequest(
            Text.Dialogs.ForceSyncSkillTitle,
            Text.Dialogs.ForceSyncSkillMessage,
            BuildSkillTargetDetails(installedSkill),
            ConfirmText: Text.Dialogs.ForceSyncSkillConfirmText);
    }

    private ConfirmationRequest CreateRollbackSkillConfirmation(InstalledSkillRecord installedSkill, SkillBackupRecord backup)
    {
        return new ConfirmationRequest(
            Text.Dialogs.RollbackSkillTitle,
            Text.Dialogs.RollbackSkillMessage,
            BuildRollbackSkillDetails(installedSkill, backup),
            ConfirmText: Text.Dialogs.RollbackSkillConfirmText);
    }

    private ConfirmationRequest CreateApplyOverlayMergeConfirmation(InstalledSkillRecord installedSkill)
    {
        return new ConfirmationRequest(
            Text.Dialogs.OverlayMergeTitle,
            Text.Dialogs.OverlayMergeMessage,
            BuildSkillTargetDetails(installedSkill),
            ConfirmText: Text.Dialogs.OverlayMergeConfirmText);
    }

    private ConfirmationRequest CreateImportExternalMcpConfirmation(IReadOnlyList<McpExternalServerImportDecision> decisions)
    {
        return new ConfirmationRequest(
            Text.Dialogs.ImportExternalMcpTitle,
            SyncImportedExternalServers
                ? Text.Dialogs.ImportExternalMcpWithSyncMessage
                : Text.Dialogs.ImportExternalMcpWithoutSyncMessage,
            BuildExternalMcpImportDetails(decisions),
            ConfirmText: Text.Dialogs.ImportExternalMcpConfirmText);
    }

    private ConfirmationRequest CreateRiskConfirmation(HubRiskConsentKind kind)
    {
        return kind switch
        {
            HubRiskConsentKind.ScriptExecution => new ConfirmationRequest(
                Text.Dialogs.ConfirmScriptRiskTitle,
                Text.Dialogs.ConfirmScriptRiskMessage,
                string.Join(Environment.NewLine, Text.Dialogs.ScriptRiskDetails),
                ConfirmText: Text.Dialogs.ConfirmScriptRiskConfirmText),
            HubRiskConsentKind.ManagedMcpExecution => new ConfirmationRequest(
                Text.Dialogs.ConfirmManagedMcpRiskTitle,
                Text.Dialogs.ConfirmManagedMcpRiskMessage,
                string.Join(Environment.NewLine, Text.Dialogs.ManagedMcpRiskDetails),
                ConfirmText: Text.Dialogs.ConfirmManagedMcpRiskConfirmText),
            HubRiskConsentKind.ExternalMcpImport => new ConfirmationRequest(
                Text.Dialogs.ConfirmExternalMcpRiskTitle,
                Text.Dialogs.ConfirmExternalMcpRiskMessage,
                string.Join(Environment.NewLine, Text.Dialogs.ExternalMcpRiskDetails),
                ConfirmText: Text.Dialogs.ConfirmExternalMcpRiskConfirmText),
            _ => new ConfirmationRequest(
                Text.Dialogs.ResetRiskConfirmationsTitle,
                Text.Dialogs.ResetRiskConfirmationsMessage,
                string.Empty,
                ConfirmText: Text.Dialogs.ResetRiskConfirmationsConfirmText)
        };
    }

    private ConfirmationRequest CreateResetRiskConfirmationsConfirmation()
    {
        return new ConfirmationRequest(
            Text.Dialogs.ResetRiskConfirmationsTitle,
            Text.Dialogs.ResetRiskConfirmationsMessage,
            string.Join(Environment.NewLine, new[]
            {
                Text.Settings.ScriptRiskTitle + " / " + ScriptRiskConsentDisplay,
                Text.Settings.ManagedMcpRiskTitle + " / " + ManagedMcpRiskConsentDisplay,
                Text.Settings.ExternalMcpRiskTitle + " / " + ExternalMcpRiskConsentDisplay
            }),
            ConfirmText: Text.Dialogs.ResetRiskConfirmationsConfirmText);
    }

    private string BuildSkillTargetDetails(InstalledSkillRecord installedSkill)
    {
        return string.Join(Environment.NewLine, new[]
        {
            Text.State.DetailSkillLabel + installedSkill.Name,
            Text.State.DetailScopeLabel + installedSkill.ProfileDisplay,
            Text.State.DetailRelativePathLabel + installedSkill.RelativePath,
            Text.State.DetailInstalledDirectoryLabel + installedSkill.DirectoryPath,
            Text.State.DetailSourceLabel + installedSkill.SourceDisplay,
            Text.State.DetailModeLabel + installedSkill.ModeDisplay
        });
    }

    private string BuildRollbackSkillDetails(InstalledSkillRecord installedSkill, SkillBackupRecord backup)
    {
        var builder = new StringBuilder();
        builder.AppendLine(BuildSkillTargetDetails(installedSkill));
        builder.AppendLine(Text.State.DetailRollbackBackupLabel + backup.DisplayName);
        builder.AppendLine(Text.State.DetailBackupPathLabel + backup.Path);
        return builder.ToString().TrimEnd();
    }

    private string BuildSkillMergePreviewDetails(SkillMergePreview preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Text.State.DetailSkillPathLabel + preview.RelativePath);
        builder.AppendLine(Text.State.DetailSourceLabel + preview.SourceDisplayName);
        builder.AppendLine(Text.State.DetailReferenceLabel + preview.SourceReference);
        builder.AppendLine(Text.State.DetailFileCountLabel + preview.Files.Count);

        foreach (var file in preview.Files.Take(20))
        {
            builder.AppendLine(Text.State.SkillMergePreviewLine(file.RelativePath, DescribeSkillMergeStatus(file.Status), DescribeSkillMergeDecision(file.SuggestedDecision)));
        }

        if (preview.Files.Count > 20)
        {
            builder.AppendLine(Text.State.MergePreviewRemainingFilesHint);
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildMcpValidationScopeDisplay(WorkspaceScope scope, string profileId, string? projectPath)
    {
        return Text.State.McpValidationScope(scope, WorkspaceProfiles.NormalizeId(profileId), projectPath);
    }

    private string DescribeSkillMergeStatus(SkillMergeFileStatus status)
    {
        return status switch
        {
            SkillMergeFileStatus.SourceOnly => Text.Skills.MergeStatusSourceOnly,
            SkillMergeFileStatus.LocalOnly => Text.Skills.MergeStatusLocalOnly,
            SkillMergeFileStatus.SourceChanged => Text.Skills.MergeStatusSourceChanged,
            SkillMergeFileStatus.SourceDeleted => Text.Skills.MergeStatusSourceDeleted,
            SkillMergeFileStatus.Conflict => Text.Skills.MergeStatusConflict,
            _ => Text.Skills.MergeStatusPending
        };
    }

    private string DescribeSkillMergeDecision(SkillMergeDecisionMode decision)
    {
        return decision switch
        {
            SkillMergeDecisionMode.UseSource => Text.Skills.MergeDecisionUseSourceOption,
            SkillMergeDecisionMode.KeepLocal => Text.Skills.MergeDecisionKeepLocalOption,
            SkillMergeDecisionMode.ApplyDeletion => Text.Skills.MergeDecisionApplyDeletionOption,
            SkillMergeDecisionMode.Skip => Text.Skills.MergeDecisionSkipOption,
            _ => Text.Skills.MergeDecisionSkipOption
        };
    }

    private string BuildMcpValidationDetails(McpValidationSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine(BuildMcpValidationScopeDisplay(snapshot.Scope, snapshot.Profile, snapshot.ProjectPath));
        builder.AppendLine(Text.State.DetailClientConfigsHeader);
        foreach (var status in snapshot.ClientStatuses)
        {
            builder.AppendLine($"{status.Client} / {status.Summary}");
            builder.AppendLine(status.FilePath);
        }

        if (snapshot.Issues.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(Text.State.DetailIssuesHeader);
            foreach (var issue in snapshot.Issues)
            {
                builder.AppendLine($"{issue.Severity} / {issue.Summary}");
                if (!string.IsNullOrWhiteSpace(issue.Details))
                {
                    builder.AppendLine(issue.Details);
                }
            }
        }

        if (snapshot.ExternalServers.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(Text.State.DetailExternalMcpHeader);
            foreach (var externalServer in snapshot.ExternalServers)
            {
                builder.AppendLine(Text.State.ExternalMcpDisplay(externalServer.Name, externalServer.HasConflict));
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildExternalMcpImportDetails(IReadOnlyList<McpExternalServerImportDecision> decisions)
    {
        return string.Join(Environment.NewLine, decisions.Select(item => item.Name));
    }
}
