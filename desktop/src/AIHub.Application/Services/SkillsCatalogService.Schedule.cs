using System.Text;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    public async Task<SkillScheduledUpdateBatchResult> RunDueScheduledUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return SkillScheduledUpdateBatchResult.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var dueSources = sources
            .Where(source => IsSourceDueForScheduledUpdate(source, now))
            .ToArray();

        if (dueSources.Length == 0)
        {
            return SkillScheduledUpdateBatchResult.Empty;
        }

        return await RunScheduledUpdatesCoreAsync(resolution.RootPath, sources, dueSources, now, cancellationToken);
    }

    public async Task<SkillScheduledUpdateBatchResult> RunScheduledUpdateForSourceAsync(
        string localName,
        ProfileKind profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return SkillScheduledUpdateBatchResult.Empty;
        }

        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var source = sources.FirstOrDefault(item => MatchesSource(item, localName, profile));
        if (source is null)
        {
            return new SkillScheduledUpdateBatchResult(
            [
                new SkillScheduledUpdateSourceResult(
                    $"{localName} / {profile.ToDisplayName()}",
                    false,
                    false,
                    "未找到要执行定时策略的来源。",
                    localName,
                    null)
            ]);
        }

        return await RunScheduledUpdatesCoreAsync(
            resolution.RootPath,
            sources,
            [source],
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private async Task<SkillScheduledUpdateBatchResult> RunScheduledUpdatesCoreAsync(
        string hubRoot,
        List<SkillSourceRecord> allSources,
        IReadOnlyList<SkillSourceRecord> targetSources,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var installs = await LoadInstallsAsync(hubRoot, cancellationToken);
        var results = new List<SkillScheduledUpdateSourceResult>(targetSources.Count);

        foreach (var source in targetSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preparedSource = await PrepareSourceForScheduledUpdateAsync(source, cancellationToken);
            var sourceInstalls = installs
                .Where(install =>
                    install.CustomizationMode is SkillCustomizationMode.Managed or SkillCustomizationMode.Overlay &&
                    MatchesSource(preparedSource, install.SourceLocalName, install.SourceProfile))
                .ToArray();

            var result = await ExecuteScheduledUpdateForSourceAsync(preparedSource, sourceInstalls, now, cancellationToken);
            results.Add(result);

            var updatedSource = preparedSource with
            {
                LastScheduledRunAt = now,
                LastScheduledResult = result.Message
            };

            var index = allSources.FindIndex(item => MatchesSource(item, source.LocalName, source.Profile));
            if (index >= 0)
            {
                allSources[index] = NormalizeSource(updatedSource);
            }
        }

        await SaveSourcesAsync(hubRoot, allSources, cancellationToken);
        return new SkillScheduledUpdateBatchResult(results);
    }

    private async Task<SkillSourceRecord> PrepareSourceForScheduledUpdateAsync(
        SkillSourceRecord source,
        CancellationToken cancellationToken)
    {
        var refreshed = await RefreshSourceVersionMetadataAsync(source, refreshRemote: true, cancellationToken);
        if (refreshed.VersionTrackingMode == SkillVersionTrackingMode.FollowReferenceLegacy)
        {
            return refreshed;
        }

        var targetTag = ResolveUpgradeTargetTag(refreshed);
        if (string.IsNullOrWhiteSpace(targetTag))
        {
            return refreshed;
        }

        if (refreshed.ScheduledUpdateAction == SkillScheduledUpdateAction.CheckAndSyncSafe
            && !string.Equals(refreshed.Reference, targetTag, StringComparison.OrdinalIgnoreCase))
        {
            return refreshed with
            {
                Reference = targetTag,
                ResolvedVersionTag = targetTag,
                HasPendingVersionUpgrade = false
            };
        }

        return refreshed;
    }

    private async Task<SkillScheduledUpdateSourceResult> ExecuteScheduledUpdateForSourceAsync(
        SkillSourceRecord source,
        IReadOnlyList<SkillInstallRecord> installs,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (installs.Count == 0)
        {
            return new SkillScheduledUpdateSourceResult(
                source.SourceDisplayName,
                true,
                false,
                "未找到绑定到该来源的托管 / Overlay Skill。",
                BuildSourceVersionDetails(source),
                null);
        }

        var detailBuilder = new StringBuilder();
        detailBuilder.AppendLine(BuildSourceVersionDetails(source));
        detailBuilder.AppendLine();

        var successCount = 0;
        var updateCount = 0;
        var blockedCount = 0;
        var failureCount = 0;
        MaintenanceAlertRecord? alert = null;

        foreach (var install in installs.OrderBy(item => item.Profile).ThenBy(item => item.InstalledRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var installResult = await ExecuteScheduledUpdateForInstallAsync(source, install, now, cancellationToken);
            if (installResult.Success)
            {
                successCount++;
            }
            else if (installResult.Blocked)
            {
                blockedCount++;
            }
            else
            {
                failureCount++;
            }

            if (installResult.Updated)
            {
                updateCount++;
            }

            if (alert is null && installResult.Alert is not null)
            {
                alert = installResult.Alert;
            }

            detailBuilder.AppendLine($"{install.Name} ({install.Profile.ToDisplayName()})");
            detailBuilder.AppendLine(installResult.Message);
            if (!string.IsNullOrWhiteSpace(installResult.Details))
            {
                detailBuilder.AppendLine(installResult.Details);
            }

            detailBuilder.AppendLine();
        }

        var summary = $"定时策略完成：更新 {updateCount} / 正常 {successCount} / 阻塞 {blockedCount} / 失败 {failureCount}";
        return new SkillScheduledUpdateSourceResult(
            source.SourceDisplayName,
            failureCount == 0,
            true,
            summary,
            detailBuilder.ToString().TrimEnd(),
            alert);
    }

    private async Task<ScheduledInstallUpdateResult> ExecuteScheduledUpdateForInstallAsync(
        SkillSourceRecord source,
        SkillInstallRecord install,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var contextResult = await TryCreateInstallContextAsync(
            install.Profile,
            install.InstalledRelativePath,
            refreshRemote: true,
            cancellationToken);
        if (!contextResult.Success || contextResult.Context is null)
        {
            return new ScheduledInstallUpdateResult(
                false,
                false,
                false,
                contextResult.Result.Message,
                contextResult.Result.Details,
                new MaintenanceAlertRecord(
                    $"skills-scheduled-failure:{install.Profile}:{install.InstalledRelativePath}",
                    "Skills 定时检查失败",
                    $"{install.Name} 无法解析来源或安装上下文。",
                    contextResult.Result.Details));
        }

        var context = contextResult.Context;
        var baselineSource = GetReferenceSourceFingerprints(context.State, context.InstalledFingerprints);
        var hasUpdate = baselineSource.Count == 0 || !FingerprintsEqual(baselineSource, context.SourceFingerprints);
        var blockedReason = GetSyncBlockedReason(context, force: false);
        await SaveScheduledCheckStateAsync(context, now, cancellationToken);

        if (!hasUpdate)
        {
            return new ScheduledInstallUpdateResult(
                true,
                false,
                false,
                "当前已与来源一致。",
                BuildUpdateDetails(context, false, blockedReason: null),
                null);
        }

        if (source.ScheduledUpdateAction == SkillScheduledUpdateAction.CheckOnly)
        {
            return new ScheduledInstallUpdateResult(
                true,
                false,
                false,
                "检测到可用更新，已按策略仅检查。",
                BuildUpdateDetails(context, true, blockedReason),
                null);
        }

        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            var shouldNotify = context.IsDirty;
            return new ScheduledInstallUpdateResult(
                false,
                false,
                true,
                blockedReason,
                BuildUpdateDetails(context, true, blockedReason),
                shouldNotify
                    ? new MaintenanceAlertRecord(
                        $"skills-auto-sync-blocked:{install.Profile}:{install.InstalledRelativePath}",
                        "Skills 自动同步被阻塞",
                        $"{install.Name} 检测到本地改动，已跳过自动同步。",
                        blockedReason)
                    : null);
        }

        var syncResult = await SyncInstalledSkillAsync(install.Profile, install.InstalledRelativePath, force: false, cancellationToken);
        return syncResult.Success
            ? new ScheduledInstallUpdateResult(
                true,
                true,
                false,
                syncResult.Message,
                syncResult.Details,
                null)
            : new ScheduledInstallUpdateResult(
                false,
                false,
                false,
                syncResult.Message,
                syncResult.Details,
                new MaintenanceAlertRecord(
                    $"skills-auto-sync-failed:{install.Profile}:{install.InstalledRelativePath}",
                    "Skills 自动同步失败",
                    $"{install.Name} 自动同步失败。",
                    syncResult.Details));
    }

    private async Task SaveScheduledCheckStateAsync(
        SkillInstallContext context,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        var states = (await LoadStatesAsync(context.HubRoot, cancellationToken)).ToList();
        var updatedState = context.State with
        {
            LastCheckedAt = checkedAt
        };

        await UpsertStateAsync(context.HubRoot, states, updatedState, cancellationToken);
    }

    private static bool IsSourceDueForScheduledUpdate(SkillSourceRecord source, DateTimeOffset now)
    {
        if (!source.IsEnabled || !source.AutoUpdate || !source.ScheduledUpdateIntervalHours.HasValue)
        {
            return false;
        }

        if (!source.LastScheduledRunAt.HasValue)
        {
            return true;
        }

        var interval = TimeSpan.FromHours(source.ScheduledUpdateIntervalHours.Value);
        return now - source.LastScheduledRunAt.Value >= interval;
    }

    private sealed record ScheduledInstallUpdateResult(
        bool Success,
        bool Updated,
        bool Blocked,
        string Message,
        string? Details,
        MaintenanceAlertRecord? Alert);
}
