using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly ProfileKind[] Profiles =
    [
        ProfileKind.Global,
        ProfileKind.Frontend,
        ProfileKind.Backend
    ];

    private readonly IHubRootLocator _hubRootLocator;
    private readonly Func<string?, IHubSettingsStore>? _hubSettingsStoreFactory;
    private readonly HashSet<string> _automaticMaintenanceCompletedRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _automaticMaintenanceGate = new();

    public SkillsCatalogService(IHubRootLocator hubRootLocator)
    : this(hubRootLocator, null)
    {
    }

    public SkillsCatalogService(IHubRootLocator hubRootLocator, Func<string?, IHubSettingsStore>? hubSettingsStoreFactory)
    {
        _hubRootLocator = hubRootLocator;
        _hubSettingsStoreFactory = hubSettingsStoreFactory;
    }

    public async Task<SkillCatalogSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return new SkillCatalogSnapshot(
                resolution,
                Array.Empty<InstalledSkillRecord>(),
                Array.Empty<SkillSourceRecord>());
        }

        await RunAutomaticMaintenanceIfEnabledAsync(resolution.RootPath, cancellationToken);

        var sources = await LoadSourcesAsync(resolution.RootPath, cancellationToken);
        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        var states = await LoadStatesAsync(resolution.RootPath, cancellationToken);
        var installedSkills = EnumerateInstalledSkills(resolution.RootPath, installs, states, sources);

        return new SkillCatalogSnapshot(resolution, installedSkills, sources);
    }

    public async Task<OperationResult> SaveSourceAsync(
        string? originalLocalName,
        ProfileKind? originalProfile,
        SkillSourceRecord draft,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶淇濆瓨 Skills 鏉ユ簮。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalized = NormalizeSource(draft);
        var validationError = ValidateSource(normalized);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return OperationResult.Fail(validationError);
        }

        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        sources.RemoveAll(source => MatchesSource(source, originalLocalName, originalProfile));

        if (sources.Any(source => MatchesSource(source, normalized.LocalName, normalized.Profile)))
        {
            return OperationResult.Fail("宸插瓨鍦ㄥ悓鍚嶅悓浣滅敤鍩熺殑 Skills 鏉ユ簮。", normalized.SourceDisplayName);
        }

        sources.Add(normalized);
        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);

        return OperationResult.Ok("Skills 来源已保存。", GetSourcesPath(resolution.RootPath));
    }

    public async Task<OperationResult> DeleteSourceAsync(
        string localName,
        ProfileKind profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鍒犻櫎 Skills 鏉ユ簮。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var removed = sources.RemoveAll(source => MatchesSource(source, localName, profile));
        if (removed == 0)
        {
            return OperationResult.Fail("鏈壘鍒拌鍒犻櫎鐨?Skills 鏉ユ簮。", localName);
        }

        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);
        return OperationResult.Ok("Skills 来源已删除。", GetSourcesPath(resolution.RootPath));
    }

    public async Task<OperationResult> SaveInstallAsync(
        SkillInstallRecord draft,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶淇濆瓨 Skill 瀹夎鐧昏。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalized = NormalizeInstall(draft);
        var sources = await LoadSourcesAsync(resolution.RootPath, cancellationToken);
        var validationError = ValidateInstall(normalized, sources);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return OperationResult.Fail(validationError);
        }

        var skillDirectory = GetInstalledSkillDirectory(resolution.RootPath, normalized.Profile, normalized.InstalledRelativePath);
        if (!Directory.Exists(skillDirectory))
        {
            return OperationResult.Fail("要登记的 Skill 目录不存在。", skillDirectory);
        }

        if (!File.Exists(Path.Combine(skillDirectory, "SKILL.md")))
        {
            return OperationResult.Fail("鐩爣鐩綍涓己灏?SKILL.md锛屾棤娉曠櫥璁颁负 Skill。", skillDirectory);
        }

        var installs = (await LoadInstallsAsync(resolution.RootPath, cancellationToken)).ToList();
        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(normalized.Profile, normalized.InstalledRelativePath);

        installs.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        installs.Add(normalized);
        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);

        var stateExists = states.Any(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (!stateExists)
        {
            states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
            states.Add(CreateStateRecord(normalized.Profile, normalized.InstalledRelativePath, skillDirectory));
            await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
            return OperationResult.Ok("Skill 安装登记已保存，并已为当前内容建立基线。", GetInstallsPath(resolution.RootPath));
        }

        return OperationResult.Ok("Skill 安装登记已保存。", GetInstallsPath(resolution.RootPath));
    }

    public async Task<OperationResult> DeleteInstallAsync(
        ProfileKind profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鍒犻櫎 Skill 鐧昏。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var installs = (await LoadInstallsAsync(resolution.RootPath, cancellationToken)).ToList();
        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profile, normalizedRelativePath);

        var removedInstalls = installs.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        var removedStates = states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (removedInstalls == 0 && removedStates == 0)
        {
            return OperationResult.Fail("鏈壘鍒拌鍒犻櫎鐨?Skill 鐧昏。", normalizedRelativePath);
        }

        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
        return OperationResult.Ok("Skill 鐧昏涓庡熀绾垮凡鍒犻櫎。", GetInstallsPath(resolution.RootPath));
    }

    public async Task<OperationResult> CaptureBaselineAsync(
        ProfileKind profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶閲嶅缓 Skill 鍩虹嚎。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        if (!installs.Any(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(profile, normalizedRelativePath)))
        {
            return OperationResult.Fail("请先保存该 Skill 的登记信息，再建立基线。", normalizedRelativePath);
        }

        var skillDirectory = GetInstalledSkillDirectory(resolution.RootPath, profile, normalizedRelativePath);
        if (!Directory.Exists(skillDirectory))
        {
            return OperationResult.Fail("目标 Skill 目录不存在。", skillDirectory);
        }

        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profile, normalizedRelativePath);
        states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        states.Add(CreateStateRecord(profile, normalizedRelativePath, skillDirectory));
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);

        return OperationResult.Ok("Skill 基线已重建。", GetStatesPath(resolution.RootPath));
    }

    public async Task<OperationResult> ScanSourceAsync(
        string localName,
        ProfileKind profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鎵弿 Skills 鏉ユ簮。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var sources = await LoadSourcesAsync(resolution.RootPath, cancellationToken);
        var source = sources.FirstOrDefault(item => MatchesSource(item, localName, profile));
        if (source is null)
        {
            return OperationResult.Fail("鏈壘鍒拌鎵弿鐨?Skills 鏉ユ簮。", localName);
        }

        if (!source.IsEnabled)
        {
            return OperationResult.Fail("褰撳墠鏉ユ簮宸茬鐢紝鏃犳硶鎵弿。", source.SourceDisplayName);
        }

        try
        {
            var resolvedSource = await ResolveSourceAsync(source, refreshRemote: source.Kind == SkillSourceKind.GitRepository, cancellationToken);
            var availableReferences = await GetAvailableReferencesAsync(resolvedSource, source, cancellationToken);
            var discoveredSkills = DiscoverSourceSkills(resolvedSource.CatalogRootPath);
            await UpdateScannedSourceAsync(resolution.RootPath, source, resolvedSource, discoveredSkills, availableReferences, cancellationToken);
            if (discoveredSkills.Count == 0)
            {
                return OperationResult.Fail(
                    "鏉ユ簮鎵弿瀹屾垚锛屼絾娌℃湁鍙戠幇浠讳綍 Skill。",
                    BuildSourceDetails(source, resolvedSource, Array.Empty<string>()));
            }

            var detailLines = discoveredSkills
                .Select(item => $"- {item.RelativePath} ({item.Name})")
                .ToArray();

            return OperationResult.Ok(
                "鏉ユ簮鎵弿瀹屾垚。",
                BuildSourceDetails(source, resolvedSource, detailLines));
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("鎵弿鏉ユ簮澶辫触。", exception.Message);
        }
    }

    public async Task<OperationResult> CheckForUpdatesAsync(
        ProfileKind profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var contextResult = await TryCreateInstallContextAsync(profile, relativePath, refreshRemote: true, cancellationToken);
        if (!contextResult.Success || contextResult.Context is null)
        {
            return contextResult.Result;
        }

        var context = contextResult.Context;
        var states = (await LoadStatesAsync(context.HubRoot, cancellationToken)).ToList();
        var state = context.State with { LastCheckedAt = DateTimeOffset.UtcNow };
        await UpsertStateAsync(context.HubRoot, states, state, cancellationToken);

        var baselineSource = GetReferenceSourceFingerprints(context.State, context.InstalledFingerprints);
        var hasUpdate = baselineSource.Count == 0 || !FingerprintsEqual(baselineSource, context.SourceFingerprints);
        var blockedReason = GetSyncBlockedReason(context, force: false);

        var message = hasUpdate ? "检测到可用更新。" : "当前已与来源基线一致。";
        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            message += " 浣嗗綋鍓嶇姸鎬佷笉閫傚悎鐩存帴鍚屾。";
        }

        return OperationResult.Ok(message, BuildUpdateDetails(context, hasUpdate, blockedReason));
    }

    
    public async Task<OperationResult> PreviewInstalledSkillDiffAsync(
        ProfileKind profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var contextResult = await TryCreateInstallContextAsync(profile, relativePath, refreshRemote: true, cancellationToken);
        if (!contextResult.Success || contextResult.Context is null)
        {
            return contextResult.Result;
        }

        var context = contextResult.Context;
        var sourceMap = context.SourceFingerprints.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var installedMap = context.InstalledFingerprints.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var baselineMap = context.State.BaselineFiles.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var sourceAdded = sourceMap.Keys.Except(installedMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var sourceRemoved = installedMap.Keys.Except(sourceMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var sourceChanged = sourceMap.Keys
            .Intersect(installedMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(sourceMap[key].Sha256, installedMap[key].Sha256, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var localAdded = baselineMap.Count == 0
            ? Array.Empty<string>()
            : installedMap.Keys.Except(baselineMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var localRemoved = baselineMap.Count == 0
            ? Array.Empty<string>()
            : baselineMap.Keys.Except(installedMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        var localChanged = baselineMap.Count == 0
            ? Array.Empty<string>()
            : baselineMap.Keys
                .Intersect(installedMap.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(key => !string.Equals(baselineMap[key].Sha256, installedMap[key].Sha256, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var hasSourceDiff = sourceAdded.Length > 0 || sourceRemoved.Length > 0 || sourceChanged.Length > 0;
        var blockedReason = GetSyncBlockedReason(context, force: false);
        var message = hasSourceDiff ? "已生成差异预览。" : "当前安装内容与来源一致。";

        return OperationResult.Ok(
            message,
            BuildDiffPreviewDetails(context, blockedReason, sourceAdded, sourceChanged, sourceRemoved, localAdded, localChanged, localRemoved));
    }
    public async Task<OperationResult> SyncInstalledSkillAsync(
        ProfileKind profile,
        string relativePath,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var contextResult = await TryCreateInstallContextAsync(profile, relativePath, refreshRemote: true, cancellationToken);
        if (!contextResult.Success || contextResult.Context is null)
        {
            return contextResult.Result;
        }

        var context = contextResult.Context;
        var blockedReason = GetSyncBlockedReason(context, force);
        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            return OperationResult.Fail(blockedReason, BuildUpdateDetails(context, hasUpdate: true, blockedReason));
        }

        var baselineSource = GetReferenceSourceFingerprints(context.State, context.InstalledFingerprints);
        var hasUpdate = baselineSource.Count == 0 || !FingerprintsEqual(baselineSource, context.SourceFingerprints);
        if (!hasUpdate && !force)
        {
            return OperationResult.Ok("褰撳墠宸叉槸鏈€鏂板唴瀹癸紝鏃犻渶鍚屾。", BuildUpdateDetails(context, hasUpdate: false, blockedReason: null));
        }

        var backupPath = CreateBackupSnapshot(context.HubRoot, context.Install.Profile, context.Install.InstalledRelativePath, context.InstalledSkillDirectory, "sync");
        var overlaySnapshot = ResolveOverlaySnapshotForSync(context);
        ReplaceDirectoryWithSource(context.SourceSkillDirectory, context.InstalledSkillDirectory);
        ApplyOverlaySnapshot(context, overlaySnapshot);

        var updatedInstalledFingerprints = CaptureFingerprints(context.InstalledSkillDirectory);
        var updatedState = context.State with
        {
            BaselineCapturedAt = DateTimeOffset.UtcNow,
            BaselineFiles = updatedInstalledFingerprints.ToList(),
            SourceBaselineFiles = context.SourceFingerprints.ToList(),
            OverlayDeletedFiles = overlaySnapshot.DeletedFiles.ToList(),
            LastSyncAt = DateTimeOffset.UtcNow,
            LastCheckedAt = DateTimeOffset.UtcNow,
            LastAppliedReference = context.ResolvedSource.ResolvedReference,
            LastBackupPath = backupPath
        };

        var states = (await LoadStatesAsync(context.HubRoot, cancellationToken)).ToList();
        await UpsertStateAsync(context.HubRoot, states, updatedState, cancellationToken);

        var detailBuilder = new StringBuilder();
        detailBuilder.AppendLine(BuildUpdateDetails(context, hasUpdate: true, blockedReason: null));
        detailBuilder.AppendLine();
        detailBuilder.AppendLine("澶囦唤璺緞：" + backupPath);
        detailBuilder.AppendLine("鍚屾鏂瑰紡：" + (force ? "寮哄埗鍚屾" : "瀹夊叏鍚屾"));
        if (!overlaySnapshot.IsEmpty)
        {
            detailBuilder.AppendLine("覆盖层已重放：" + overlaySnapshot.FileCount + " 个文件 / 删除 " + overlaySnapshot.DeletedFiles.Count + " 项");
        }

        return OperationResult.Ok(force ? "Skill 已强制同步。" : "Skill 已安全同步。", detailBuilder.ToString().TrimEnd());
    }

    public async Task<OperationResult> RollbackInstalledSkillAsync(
        ProfileKind profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶鍥炴粴 Skill。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var installDirectory = GetInstalledSkillDirectory(resolution.RootPath, profile, normalizedRelativePath);
        if (!Directory.Exists(installDirectory))
        {
            return OperationResult.Fail("目标 Skill 目录不存在。", installDirectory);
        }

        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profile, normalizedRelativePath);
        var state = states.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (state is null)
        {
            return OperationResult.Fail("璇?Skill 杩樻病鏈夊悓姝ョ姸鎬侊紝鏃犳硶鍥炴粴。", normalizedRelativePath);
        }

        var backupPath = ResolveRollbackBackupPath(resolution.RootPath, state, profile, normalizedRelativePath);
        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
        {
            return OperationResult.Fail("没有可用的回滚备份。", normalizedRelativePath);
        }

        var currentSnapshotBackupPath = CreateBackupSnapshot(resolution.RootPath, profile, normalizedRelativePath, installDirectory, "pre-rollback");
        ReplaceDirectoryWithSource(backupPath, installDirectory);

        var currentFingerprints = CaptureFingerprints(installDirectory);
        var updatedState = state with
        {
            BaselineCapturedAt = DateTimeOffset.UtcNow,
            BaselineFiles = currentFingerprints.ToList(),
            LastSyncAt = DateTimeOffset.UtcNow,
            LastCheckedAt = DateTimeOffset.UtcNow,
            LastAppliedReference = "rollback",
            LastBackupPath = currentSnapshotBackupPath
        };

        await UpsertStateAsync(resolution.RootPath, states, updatedState, cancellationToken);

        var detailBuilder = new StringBuilder();
        detailBuilder.AppendLine("宸蹭粠澶囦唤鍥炴粴 Skill。");
        detailBuilder.AppendLine("鍥炴粴鏉ユ簮：" + backupPath);
        detailBuilder.AppendLine("褰撳墠鍐呭澶囦唤：" + currentSnapshotBackupPath);
        detailBuilder.AppendLine("瀹夎鐩綍：" + installDirectory);

        return OperationResult.Ok("Skill 已回滚到最近备份。", detailBuilder.ToString().TrimEnd());
    }

    private async Task<SkillContextResult> TryCreateInstallContextAsync(
        ProfileKind profile,
        string relativePath,
        bool refreshRemote,
        CancellationToken cancellationToken)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return SkillContextResult.Fail(OperationResult.Fail("AI-Hub 鏍圭洰褰曟棤鏁堬紝鏃犳硶澶勭悊 Skill。", string.Join(Environment.NewLine, resolution.Errors)));
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return SkillContextResult.Fail(OperationResult.Fail("Skill 路径不能为空。"));
        }

        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        var install = installs.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(profile, normalizedRelativePath));
        if (install is null)
        {
            return SkillContextResult.Fail(OperationResult.Fail("该 Skill 尚未登记来源与同步策略。", normalizedRelativePath));
        }

        if (install.CustomizationMode == SkillCustomizationMode.Local)
        {
            return SkillContextResult.Fail(OperationResult.Fail("本地模式的 Skill 不参与上游同步。", normalizedRelativePath));
        }

        if (string.IsNullOrWhiteSpace(install.SourceLocalName) || !install.SourceProfile.HasValue)
        {
            return SkillContextResult.Fail(OperationResult.Fail("璇?Skill 灏氭湭缁戝畾鏉ユ簮。", normalizedRelativePath));
        }

        var sources = await LoadSourcesAsync(resolution.RootPath, cancellationToken);
        var source = sources.FirstOrDefault(item => MatchesSource(item, install.SourceLocalName, install.SourceProfile));
        if (source is null)
        {
            return SkillContextResult.Fail(OperationResult.Fail("绑定的来源不存在，请先检查来源清单。", install.SourceLocalName));
        }

        if (!source.IsEnabled)
        {
            return SkillContextResult.Fail(OperationResult.Fail("缁戝畾鐨勬潵婧愬綋鍓嶅凡绂佺敤。", source.SourceDisplayName));
        }

        var installedSkillDirectory = GetInstalledSkillDirectory(resolution.RootPath, install.Profile, install.InstalledRelativePath);
        if (!Directory.Exists(installedSkillDirectory))
        {
            return SkillContextResult.Fail(OperationResult.Fail("安装目录不存在。", installedSkillDirectory));
        }

        try
        {
            var resolvedSource = await ResolveSourceAsync(source, refreshRemote, cancellationToken);
            var sourceSkillDirectory = ResolveSourceSkillDirectory(resolvedSource, install);
            var sourceFingerprints = CaptureFingerprints(sourceSkillDirectory);
            var installedFingerprints = CaptureFingerprints(installedSkillDirectory);
            var states = await LoadStatesAsync(resolution.RootPath, cancellationToken);
            var state = states.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(profile, normalizedRelativePath))
                ?? new SkillInstallStateRecord
                {
                    Profile = profile,
                    InstalledRelativePath = normalizedRelativePath
                };

            var isDirty = state.BaselineFiles.Count > 0 && !FingerprintsEqual(state.BaselineFiles, installedFingerprints);
            return SkillContextResult.Ok(new SkillInstallContext(
                resolution.RootPath,
                install,
                state,
                source,
                installedSkillDirectory,
                installedFingerprints,
                isDirty,
                resolvedSource,
                sourceSkillDirectory,
                sourceFingerprints));
        }
        catch (Exception exception)
        {
            return SkillContextResult.Fail(OperationResult.Fail("瑙ｆ瀽 Skill 鏉ユ簮澶辫触。", exception.Message));
        }
    }

    private static IReadOnlyList<InstalledSkillRecord> EnumerateInstalledSkills(
        string hubRoot,
        IReadOnlyList<SkillInstallRecord> installs,
        IReadOnlyList<SkillInstallStateRecord> states,
        IReadOnlyList<SkillSourceRecord> sources)
    {
        var installedSkills = new List<InstalledSkillRecord>();
        var installMap = installs.ToDictionary(
            item => GetInstallKey(item.Profile, item.InstalledRelativePath),
            item => item,
            StringComparer.OrdinalIgnoreCase);
        var stateMap = states.ToDictionary(
            item => GetInstallKey(item.Profile, item.InstalledRelativePath),
            item => item,
            StringComparer.OrdinalIgnoreCase);
        var sourceMap = sources.ToDictionary(
            item => GetSourceKey(item.LocalName, item.Profile),
            item => item,
            StringComparer.OrdinalIgnoreCase);

        foreach (var profile in Profiles)
        {
            var profileRoot = Path.Combine(hubRoot, "skills", profile.ToStorageValue());
            if (!Directory.Exists(profileRoot))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(profileRoot, "SKILL.md", SearchOption.AllDirectories))
            {
                var skillDirectory = Path.GetDirectoryName(manifestPath);
                if (string.IsNullOrWhiteSpace(skillDirectory))
                {
                    continue;
                }

                var relativePath = NormalizePath(Path.GetRelativePath(profileRoot, skillDirectory));
                var installKey = GetInstallKey(profile, relativePath);
                installMap.TryGetValue(installKey, out var install);
                stateMap.TryGetValue(installKey, out var state);

                SkillSourceRecord? source = null;
                var sourceMissing = false;
                if (!string.IsNullOrWhiteSpace(install?.SourceLocalName) && install.SourceProfile.HasValue)
                {
                    sourceMap.TryGetValue(GetSourceKey(install.SourceLocalName, install.SourceProfile.Value), out source);
                    sourceMissing = source is null;
                }

                var hasBaseline = state is not null && state.BaselineFiles.Count > 0;
                var isDirty = false;
                if (hasBaseline)
                {
                    var currentFingerprints = CaptureFingerprints(skillDirectory);
                    isDirty = !FingerprintsEqual(state!.BaselineFiles, currentFingerprints);
                }

                var mode = install?.CustomizationMode ?? SkillCustomizationMode.Local;
                var backupRecords = GetRecentBackupRecords(hubRoot, profile, relativePath);
                var backupPaths = backupRecords.Select(item => item.Path).ToArray();

                installedSkills.Add(new InstalledSkillRecord
                {
                    Name = Path.GetFileName(skillDirectory),
                    Profile = profile,
                    DirectoryPath = skillDirectory,
                    RelativePath = relativePath,
                    HasManifest = true,
                    IsRegistered = install is not null,
                    CustomizationMode = mode,
                    HasBaseline = hasBaseline,
                    IsDirty = isDirty,
                    SourceLocalName = install?.SourceLocalName,
                    SourceProfile = install?.SourceProfile,
                    SourceSkillPath = install?.SourceSkillPath,
                    BaselineDisplay = hasBaseline
                        ? "鏈€杩戝熀绾匡細" + state!.BaselineCapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : "灏氭湭寤虹珛鍩虹嚎",
                    StatusDisplay = BuildStatusText(install is not null, mode, hasBaseline, isDirty, sourceMissing),
                    LastSyncDisplay = BuildLastSyncDisplay(state),
                    BackupSummaryDisplay = BuildBackupSummary(backupPaths),
                    RecentBackupsDisplay = BuildRecentBackupsDisplay(backupPaths),
                    BackupRecords = backupRecords
                });
            }
        }

        return installedSkills
            .DistinctBy(skill => GetInstallKey(skill.Profile, skill.RelativePath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(skill => skill.Profile)
            .ThenBy(skill => skill.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildStatusText(
        bool isRegistered,
        SkillCustomizationMode mode,
        bool hasBaseline,
        bool isDirty,
        bool sourceMissing)
    {
        if (!isRegistered)
        {
            return "鏈櫥璁版潵婧愪笌鏇存柊绛栫暐";
        }

        if (sourceMissing && mode != SkillCustomizationMode.Local)
        {
            return "已登记，但绑定的来源记录不存在";
        }

        if (!hasBaseline)
        {
            return "宸茬櫥璁帮紝灏氭湭寤虹珛鍩虹嚎";
        }

        if (isDirty)
        {
            return mode switch
            {
                SkillCustomizationMode.Managed => "鎵樼妯″紡锛屾娴嬪埌鏈湴淇敼",
                SkillCustomizationMode.Overlay => "瑕嗙洊灞傛ā寮忥紝瀛樺湪鏈湴瑕嗙洊鏀瑰姩",
                SkillCustomizationMode.Fork => "Fork 模式，存在新的本地修改",
                SkillCustomizationMode.Local => "本地模式，存在新的本地修改",
                _ => "瀛樺湪鏈湴淇敼"
            };
        }

        return mode switch
        {
            SkillCustomizationMode.Managed => "托管模式，与基线一致",
            SkillCustomizationMode.Overlay => "覆盖层模式，与基线一致",
            SkillCustomizationMode.Fork => "Fork 模式，与基线一致",
            SkillCustomizationMode.Local => "本地模式，与基线一致",
            _ => "与基线一致"
        };
    }

    private static SkillInstallStateRecord CreateStateRecord(ProfileKind profile, string relativePath, string skillDirectory)
    {
        return new SkillInstallStateRecord
        {
            Profile = profile,
            InstalledRelativePath = NormalizePath(relativePath),
            BaselineCapturedAt = DateTimeOffset.UtcNow,
            BaselineFiles = CaptureFingerprints(skillDirectory).ToList()
        };
    }

    private static IReadOnlyList<SkillFileFingerprintRecord> CaptureFingerprints(string skillDirectory)
    {
        return Directory
            .EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizePath(Path.GetRelativePath(skillDirectory, path))
            })
            .Where(item => !ShouldIgnoreFingerprint(item.RelativePath))
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                using var stream = File.OpenRead(item.FullPath);
                return new SkillFileFingerprintRecord
                {
                    RelativePath = item.RelativePath,
                    Sha256 = Convert.ToHexString(SHA256.HashData(stream)),
                    Size = new FileInfo(item.FullPath).Length
                };
            })
            .ToArray();
    }

    private static bool ShouldIgnorePath(string relativePath)
    {
        return ShouldIgnoreFingerprint(relativePath);
    }

    private static bool ShouldIgnoreFingerprint(string relativePath)
    {
        var segments = NormalizePath(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".svn", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool FingerprintsEqual(
        IReadOnlyList<SkillFileFingerprintRecord> left,
        IReadOnlyList<SkillFileFingerprintRecord> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var orderedLeft = left
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var orderedRight = right
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < orderedLeft.Length; index++)
        {
            var currentLeft = orderedLeft[index];
            var currentRight = orderedRight[index];
            if (!string.Equals(currentLeft.RelativePath, currentRight.RelativePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentLeft.Sha256, currentRight.Sha256, StringComparison.OrdinalIgnoreCase) ||
                currentLeft.Size != currentRight.Size)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<ResolvedSkillSource> ResolveSourceAsync(
        SkillSourceRecord source,
        bool refreshRemote,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (source.Kind == SkillSourceKind.LocalDirectory)
        {
            var workingRootPath = NormalizeExistingDirectory(source.Location, "鏉ユ簮鐩綍涓嶅瓨鍦細");
            var catalogRootPath = ResolveCatalogRootPath(workingRootPath, source.CatalogPath);
            return new ResolvedSkillSource(workingRootPath, catalogRootPath, "local");
        }

        var cacheDirectory = await EnsureGitWorkingCopyAsync(source, refreshRemote, cancellationToken);
        var catalogRoot = ResolveCatalogRootPath(cacheDirectory, source.CatalogPath);
        var resolvedReference = await ResolveGitReferenceAsync(cacheDirectory, cancellationToken);
        return new ResolvedSkillSource(cacheDirectory, catalogRoot, resolvedReference);
    }

    private static string BuildSourceDetails(
        SkillSourceRecord source,
        ResolvedSkillSource resolvedSource,
        IReadOnlyList<string> detailLines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("鏉ユ簮：" + source.SourceDisplayName);
        builder.AppendLine("绫诲瀷：" + source.KindDisplay);
        builder.AppendLine("瑙ｆ瀽鐩綍：" + resolvedSource.WorkingRootPath);
        builder.AppendLine("鐩綍鑼冨洿：" + resolvedSource.CatalogRootPath);
        builder.AppendLine("褰撳墠寮曠敤：" + resolvedSource.ResolvedReference);

        if (detailLines.Count == 0)
        {
            builder.AppendLine("鏈彂鐜颁换浣?Skill。");
        }
        else
        {
            builder.AppendLine("发现的 Skills：");
            foreach (var line in detailLines)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildUpdateDetails(SkillInstallContext context, bool hasUpdate, string? blockedReason)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Skill：" + context.Install.Name);
        builder.AppendLine("瀹夎璺緞：" + context.InstalledSkillDirectory);
        builder.AppendLine("鏉ユ簮：" + context.Source.SourceDisplayName);
        builder.AppendLine("鏉ユ簮鐩綍：" + context.SourceSkillDirectory);
        builder.AppendLine("褰撳墠妯″紡：" + context.Install.CustomizationMode.ToDisplayName());
        builder.AppendLine("本地状态：" + (context.IsDirty ? "检测到本地修改" : "与基线一致"));
        builder.AppendLine("褰撳墠寮曠敤：" + context.ResolvedSource.ResolvedReference);
        builder.AppendLine("上游状态：" + (hasUpdate ? "检测到可用更新" : "与来源基线一致"));

        if (!string.IsNullOrWhiteSpace(context.State.LastAppliedReference))
        {
            builder.AppendLine("涓婃鍚屾寮曠敤：" + context.State.LastAppliedReference);
        }

        if (context.State.LastSyncAt.HasValue)
        {
            builder.AppendLine("涓婃鍚屾鏃堕棿：" + context.State.LastSyncAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (!string.IsNullOrWhiteSpace(context.State.LastBackupPath))
        {
            builder.AppendLine("鏈€杩戝浠斤細" + context.State.LastBackupPath);
        }

        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            builder.AppendLine("鍚屾闄愬埗：" + blockedReason);
        }

        return builder.ToString().TrimEnd();
    }

    private static string? GetSyncBlockedReason(SkillInstallContext context, bool force)
    {
        if (context.Install.CustomizationMode == SkillCustomizationMode.Local)
        {
            return "本地模式的 Skill 不参与上游同步。";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Overlay && context.State.BaselineFiles.Count == 0)
        {
            return "覆盖层模式需要先建立基线，才能安全重放本地覆盖内容。";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Fork && !force)
        {
            return "Fork 妯″紡榛樿涓嶈嚜鍔ㄨ鐩栵紝璇锋敼鐢ㄥ己鍒跺悓姝ユ垨鎵嬪伐澶勭悊。";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Overlay)
        {
            return null;
        }

        if (context.IsDirty && !force)
        {
            return "检测到本地修改，默认不会覆盖。请先重建基线、调整模式，或改用强制同步。";
        }

        return null;
    }

    private static IReadOnlyList<SkillFileFingerprintRecord> GetReferenceSourceFingerprints(
        SkillInstallStateRecord state,
        IReadOnlyList<SkillFileFingerprintRecord> installedFingerprints)
    {
        if (state.SourceBaselineFiles.Count > 0)
        {
            return state.SourceBaselineFiles;
        }

        if (state.BaselineFiles.Count > 0)
        {
            return state.BaselineFiles;
        }

        return installedFingerprints;
    }

    private static string ResolveRollbackBackupPath(
        string hubRoot,
        SkillInstallStateRecord state,
        ProfileKind profile,
        string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(state.LastBackupPath) && Directory.Exists(state.LastBackupPath))
        {
            return state.LastBackupPath;
        }

        var backupRoot = GetBackupRoot(hubRoot, profile, relativePath);
        if (!Directory.Exists(backupRoot))
        {
            return string.Empty;
        }

        return Directory
            .EnumerateDirectories(backupRoot)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string CreateBackupSnapshot(
        string hubRoot,
        ProfileKind profile,
        string relativePath,
        string sourceDirectory,
        string reason)
    {
        var backupDirectory = Path.Combine(
            GetBackupRoot(hubRoot, profile, relativePath),
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" + reason);

        if (Directory.Exists(backupDirectory))
        {
            Directory.Delete(backupDirectory, recursive: true);
        }

        CopyDirectory(sourceDirectory, backupDirectory);
        return backupDirectory;
    }

    private static string GetBackupRoot(string hubRoot, ProfileKind profile, string relativePath)
    {
        return Path.Combine(
            hubRoot,
            "backups",
            "skills",
            profile.ToStorageValue(),
            SanitizePathSegment(relativePath));
    }

    private static void ReplaceDirectoryWithSource(string sourceDirectory, string destinationDirectory)
    {
        if (Directory.Exists(destinationDirectory))
        {
            DeleteDirectoryContents(destinationDirectory);
        }
        else
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        CopyDirectory(sourceDirectory, destinationDirectory);
    }

    private static void DeleteDirectoryContents(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            Directory.Delete(childDirectory, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        CopyDirectoryRecursive(sourceDirectory, destinationDirectory, sourceDirectory);
    }

    private static void CopyDirectoryRecursive(string sourceDirectory, string destinationDirectory, string rootDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(rootDirectory, directory));
            if (ShouldIgnorePath(relativePath))
            {
                continue;
            }

            var targetDirectory = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            Directory.CreateDirectory(targetDirectory);
            CopyDirectoryRecursive(directory, targetDirectory, rootDirectory);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var relativePath = NormalizePath(Path.GetRelativePath(rootDirectory, file));
            if (ShouldIgnorePath(relativePath))
            {
                continue;
            }

            var targetFilePath = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, targetFilePath, overwrite: true);
        }
    }

    private async Task<string> EnsureGitWorkingCopyAsync(
        SkillSourceRecord source,
        bool refreshRemote,
        CancellationToken cancellationToken)
    {
        var cacheDirectory = Path.Combine(GetSkillCacheRoot(), ComputeSourceCacheKey(source));
        var gitDirectory = Path.Combine(cacheDirectory, ".git");

        if (!Directory.Exists(gitDirectory))
        {
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cacheDirectory)!);
            await EnsureProcessSuccessAsync(
                "git",
                ["clone", source.Location, cacheDirectory],
                workingDirectory: null,
                failureMessage: "鍒濆鍖?Git 鏉ユ簮缂撳瓨澶辫触。",
                cancellationToken);
        }
        else if (refreshRemote)
        {
            await EnsureProcessSuccessAsync(
                "git",
                ["-C", cacheDirectory, "fetch", "--all", "--tags", "--prune"],
                workingDirectory: null,
                failureMessage: "鏇存柊 Git 鏉ユ簮缂撳瓨澶辫触。",
                cancellationToken);
        }

        var reference = string.IsNullOrWhiteSpace(source.Reference) ? "main" : source.Reference;
        await EnsureProcessSuccessAsync(
            "git",
            ["-C", cacheDirectory, "checkout", "--force", reference],
            workingDirectory: null,
            failureMessage: "鍒囨崲鍒版寚瀹?Git 寮曠敤澶辫触。",
            cancellationToken);

        var remoteReference = "origin/" + reference;
        var remoteReferenceExists = await DoesGitReferenceExistAsync(cacheDirectory, remoteReference, cancellationToken);
        if (remoteReferenceExists)
        {
            await EnsureProcessSuccessAsync(
                "git",
                ["-C", cacheDirectory, "reset", "--hard", remoteReference],
                workingDirectory: null,
                failureMessage: "鍚屾杩滅鍒嗘敮澶辫触。",
                cancellationToken);
        }

        return cacheDirectory;
    }

    private static async Task<bool> DoesGitReferenceExistAsync(string cacheDirectory, string reference, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "git",
            ["-C", cacheDirectory, "rev-parse", "--verify", reference],
            workingDirectory: null,
            cancellationToken);

        return result.ExitCode == 0;
    }

    private static async Task<string> ResolveGitReferenceAsync(string cacheDirectory, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "git",
            ["-C", cacheDirectory, "rev-parse", "HEAD"],
            workingDirectory: null,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return "unknown";
        }

        var reference = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(reference) ? "unknown" : reference;
    }

    private static async Task EnsureProcessSuccessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(fileName, arguments, workingDirectory, cancellationToken);
        if (result.ExitCode == 0)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? failureMessage : failureMessage + Environment.NewLine + details);
    }

    private static async Task<ProcessExecutionResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : workingDirectory
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessExecutionResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static string GetSkillCacheRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIHub",
            "cache",
            "skills");
    }

    private static string ComputeSourceCacheKey(SkillSourceRecord source)
    {
        var input = source.Kind + ":" + source.Location.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveCatalogRootPath(string workingRootPath, string? catalogPath)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            return workingRootPath;
        }

        var catalogRootPath = Path.GetFullPath(Path.Combine(
            workingRootPath,
            NormalizePath(catalogPath).Replace('/', Path.DirectorySeparatorChar)));

        if (!Directory.Exists(catalogRootPath))
        {
            throw new InvalidOperationException("鏉ユ簮鐩綍鑼冨洿涓嶅瓨鍦細" + catalogRootPath);
        }

        return catalogRootPath;
    }

    private static string ResolveSourceSkillDirectory(ResolvedSkillSource resolvedSource, SkillInstallRecord install)
    {
        var candidateDirectories = new List<string>();

        if (!string.IsNullOrWhiteSpace(install.SourceSkillPath))
        {
            candidateDirectories.Add(CombineCandidatePath(resolvedSource.WorkingRootPath, install.SourceSkillPath));
            candidateDirectories.Add(CombineCandidatePath(resolvedSource.CatalogRootPath, install.SourceSkillPath));
        }

        candidateDirectories.Add(CombineCandidatePath(resolvedSource.CatalogRootPath, install.InstalledRelativePath));
        candidateDirectories.Add(Path.Combine(resolvedSource.CatalogRootPath, install.Name));

        foreach (var candidateDirectory in candidateDirectories
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidateDirectory) && File.Exists(Path.Combine(candidateDirectory, "SKILL.md")))
            {
                return candidateDirectory;
            }
        }

        var discoveredSkills = DiscoverSourceSkills(resolvedSource.CatalogRootPath);
        if (discoveredSkills.Count == 1)
        {
            return discoveredSkills[0].SkillDirectory;
        }

        var nameMatches = discoveredSkills
            .Where(item => string.Equals(item.Name, install.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nameMatches.Length == 1)
        {
            return nameMatches[0].SkillDirectory;
        }

        var relativePathMatches = discoveredSkills
            .Where(item => string.Equals(item.RelativePath, NormalizePath(install.InstalledRelativePath), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (relativePathMatches.Length == 1)
        {
            return relativePathMatches[0].SkillDirectory;
        }

        throw new InvalidOperationException("鏃犳硶纭畾鏉ユ簮涓殑 Skill 鐩綍锛岃鍦ㄦ潵婧愪腑鐨勬妧鑳借矾寰勪腑鏄庣‘濉啓。");
    }

    private static string CombineCandidatePath(string rootPath, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(
            rootPath,
            NormalizePath(relativePath).Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string NormalizeExistingDirectory(string rawPath, string prefix)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(rawPath.Trim());
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(prefix + exception.Message);
        }

        if (!Directory.Exists(normalizedPath))
        {
            throw new InvalidOperationException(prefix + normalizedPath);
        }

        return normalizedPath;
    }

    private static IReadOnlyList<DiscoveredSkill> DiscoverSourceSkills(string catalogRootPath)
    {
        if (!Directory.Exists(catalogRootPath))
        {
            return Array.Empty<DiscoveredSkill>();
        }

        return Directory
            .EnumerateFiles(catalogRootPath, "SKILL.md", SearchOption.AllDirectories)
            .Select(manifestPath =>
            {
                var skillDirectory = Path.GetDirectoryName(manifestPath)!;
                var relativePath = NormalizePath(Path.GetRelativePath(catalogRootPath, skillDirectory));
                return new DiscoveredSkill(
                    Path.GetFileName(skillDirectory),
                    string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath,
                    skillDirectory);
            })
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task UpsertStateAsync(
        string hubRoot,
        List<SkillInstallStateRecord> states,
        SkillInstallStateRecord state,
        CancellationToken cancellationToken)
    {
        states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(state.Profile, state.InstalledRelativePath));
        states.Add(NormalizeState(state));
        await SaveStatesAsync(hubRoot, states, cancellationToken);
    }

    private static string SanitizePathSegment(string relativePath)
    {
        return NormalizePath(relativePath).Replace('/', '_').Replace(':', '_');
    }

    private static async Task<IReadOnlyList<SkillSourceRecord>> LoadSourcesAsync(string hubRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourcesPath = GetSourcesPath(hubRoot);
        if (!File.Exists(sourcesPath))
        {
            return Array.Empty<SkillSourceRecord>();
        }

        var json = await File.ReadAllTextAsync(sourcesPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SkillSourceRecord>();
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("sources", out var sourcesElement) || sourcesElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SkillSourceRecord>();
        }

        var sources = new List<SkillSourceRecord>();
        foreach (var item in sourcesElement.EnumerateArray())
        {
            sources.Add(NormalizeSource(ReadSourceRecord(item)));
        }

        return sources
            .OrderBy(source => source.Profile)
            .ThenBy(source => source.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SkillSourceRecord ReadSourceRecord(JsonElement element)
    {
        var kind = ReadSourceKind(element);
        var location = ReadString(element, "location");
        if (string.IsNullOrWhiteSpace(location))
        {
            location = ReadString(element, "repository");
        }

        var catalogPath = ReadString(element, "catalogPath");
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            catalogPath = ReadString(element, "skillPath");
        }

        return new SkillSourceRecord
        {
            LocalName = ReadString(element, "localName") ?? string.Empty,
            Profile = ReadProfile(element, "profile", ProfileKind.Global),
            Kind = kind,
            Location = location ?? string.Empty,
            CatalogPath = catalogPath,
            Reference = ReadString(element, "reference") ?? "main",
            IsEnabled = ReadBoolean(element, "isEnabled", true),
            AutoUpdate = ReadBoolean(element, "autoUpdate", true),
            ScheduledUpdateIntervalHours = ReadNullableInt(element, "scheduledUpdateIntervalHours"),
            ScheduledUpdateAction = ReadScheduledUpdateAction(element, "scheduledUpdateAction"),
            LastScheduledRunAt = ReadDateTimeOffset(element, "lastScheduledRunAt"),
            LastScheduledResult = ReadString(element, "lastScheduledResult"),
            LastScannedAt = ReadDateTimeOffset(element, "lastScannedAt"),
            LastScanReference = ReadString(element, "lastScanReference"),
            LastDiscoveredSkills = ReadStringArray(element, "lastDiscoveredSkills"),
            AvailableReferences = ReadStringArray(element, "availableReferences"),
            VersionTrackingMode = ReadVersionTrackingMode(element, "versionTrackingMode", kind),
            PinnedTag = ReadString(element, "pinnedTag"),
            ResolvedVersionTag = ReadString(element, "resolvedVersionTag"),
            AvailableVersionTags = ReadStringArray(element, "availableVersionTags"),
            HasPendingVersionUpgrade = ReadBoolean(element, "hasPendingVersionUpgrade", false)
        };
    }

    private static async Task SaveSourcesAsync(
        string hubRoot,
        IReadOnlyList<SkillSourceRecord> sources,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourcesPath = GetSourcesPath(hubRoot);
        var directory = Path.GetDirectoryName(sourcesPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new SkillSourcesDocument
        {
            Sources = sources.Select(NormalizeSource).ToList()
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(sourcesPath, json, cancellationToken);
    }

    private static async Task<IReadOnlyList<SkillInstallRecord>> LoadInstallsAsync(string hubRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var installsPath = GetInstallsPath(hubRoot);
        if (!File.Exists(installsPath))
        {
            return Array.Empty<SkillInstallRecord>();
        }

        var json = await File.ReadAllTextAsync(installsPath, cancellationToken);
        var document = JsonSerializer.Deserialize<SkillInstallsDocument>(json, SerializerOptions) ?? new SkillInstallsDocument();
        return document.Installs
            .Select(NormalizeInstall)
            .OrderBy(item => item.Profile)
            .ThenBy(item => item.InstalledRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task SaveInstallsAsync(
        string hubRoot,
        IReadOnlyList<SkillInstallRecord> installs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var installsPath = GetInstallsPath(hubRoot);
        var directory = Path.GetDirectoryName(installsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new SkillInstallsDocument
        {
            Installs = installs.Select(NormalizeInstall).ToList()
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(installsPath, json, cancellationToken);
    }

    private static async Task<IReadOnlyList<SkillInstallStateRecord>> LoadStatesAsync(string hubRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var statesPath = GetStatesPath(hubRoot);
        if (!File.Exists(statesPath))
        {
            return Array.Empty<SkillInstallStateRecord>();
        }

        var json = await File.ReadAllTextAsync(statesPath, cancellationToken);
        var document = JsonSerializer.Deserialize<SkillInstallStatesDocument>(json, SerializerOptions) ?? new SkillInstallStatesDocument();
        return document.States
            .Select(NormalizeState)
            .OrderBy(item => item.Profile)
            .ThenBy(item => item.InstalledRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task SaveStatesAsync(
        string hubRoot,
        IReadOnlyList<SkillInstallStateRecord> states,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var statesPath = GetStatesPath(hubRoot);
        var directory = Path.GetDirectoryName(statesPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new SkillInstallStatesDocument
        {
            States = states.Select(NormalizeState).ToList()
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        await File.WriteAllTextAsync(statesPath, json, cancellationToken);
    }

    private static SkillSourceRecord NormalizeSource(SkillSourceRecord record)
    {
        var normalizedReference = string.IsNullOrWhiteSpace(record.Reference) ? "main" : record.Reference.Trim();
        if (record.Kind == SkillSourceKind.LocalDirectory)
        {
            normalizedReference = string.Empty;
        }

        var normalizedInterval = NormalizeScheduledUpdateInterval(record.ScheduledUpdateIntervalHours);
        var autoUpdate = normalizedInterval.HasValue && record.AutoUpdate;
        if (record.ScheduledUpdateIntervalHours is null)
        {
            autoUpdate = record.AutoUpdate;
            normalizedInterval = record.AutoUpdate ? 24 : null;
        }
        else if (!record.AutoUpdate)
        {
            normalizedInterval = null;
        }

        var versionTrackingMode = NormalizeVersionTrackingMode(record.Kind, record.VersionTrackingMode, record.PinnedTag, record.AvailableVersionTags, allowLegacyFallbackWhenNoTags: false);
        var normalizedPinnedTag = string.IsNullOrWhiteSpace(record.PinnedTag) ? null : record.PinnedTag.Trim();
        if (versionTrackingMode != SkillVersionTrackingMode.PinTag)
        {
            normalizedPinnedTag = null;
        }

        return record with
        {
            LocalName = record.LocalName.Trim(),
            Location = record.Location.Trim(),
            CatalogPath = string.IsNullOrWhiteSpace(record.CatalogPath) ? null : NormalizePath(record.CatalogPath),
            Reference = normalizedReference,
            AutoUpdate = autoUpdate,
            ScheduledUpdateIntervalHours = normalizedInterval,
            ScheduledUpdateAction = record.ScheduledUpdateAction,
            LastScheduledResult = string.IsNullOrWhiteSpace(record.LastScheduledResult) ? null : record.LastScheduledResult.Trim(),
            LastScanReference = string.IsNullOrWhiteSpace(record.LastScanReference) ? null : record.LastScanReference.Trim(),
            LastDiscoveredSkills = record.LastDiscoveredSkills.Where(item => !string.IsNullOrWhiteSpace(item)).Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            AvailableReferences = record.AvailableReferences.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            VersionTrackingMode = versionTrackingMode,
            PinnedTag = normalizedPinnedTag,
            ResolvedVersionTag = string.IsNullOrWhiteSpace(record.ResolvedVersionTag) ? null : record.ResolvedVersionTag.Trim(),
            AvailableVersionTags = record.AvailableVersionTags.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static SkillInstallRecord NormalizeInstall(SkillInstallRecord record)
    {
        var normalizedRelativePath = NormalizePath(record.InstalledRelativePath);
        return record with
        {
            Name = string.IsNullOrWhiteSpace(record.Name) ? Path.GetFileName(normalizedRelativePath) : record.Name.Trim(),
            InstalledRelativePath = normalizedRelativePath,
            SourceLocalName = string.IsNullOrWhiteSpace(record.SourceLocalName) ? null : record.SourceLocalName.Trim(),
            SourceSkillPath = string.IsNullOrWhiteSpace(record.SourceSkillPath) ? null : NormalizePath(record.SourceSkillPath)
        };
    }

    private static SkillInstallStateRecord NormalizeState(SkillInstallStateRecord record)
    {
        return record with
        {
            InstalledRelativePath = NormalizePath(record.InstalledRelativePath),
            BaselineFiles = record.BaselineFiles
                .Select(item => item with { RelativePath = NormalizePath(item.RelativePath) })
                .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static string? ValidateSource(SkillSourceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.LocalName))
        {
            return "鏉ユ簮鍚嶇О涓嶈兘涓虹┖。";
        }

        if (string.IsNullOrWhiteSpace(record.Location))
        {
            return "鏉ユ簮鍦板潃涓嶈兘涓虹┖。";
        }

        return null;
    }

    private static string? ValidateInstall(SkillInstallRecord record, IReadOnlyList<SkillSourceRecord> sources)
    {
        if (string.IsNullOrWhiteSpace(record.InstalledRelativePath))
        {
            return "Skill 的安装路径不能为空。";
        }

        if (record.CustomizationMode == SkillCustomizationMode.Local)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(record.SourceLocalName) || !record.SourceProfile.HasValue)
        {
            return "除“本地”模式外，其它模式都需要绑定一个来源。";
        }

        if (!sources.Any(source => MatchesSource(source, record.SourceLocalName, record.SourceProfile)))
        {
            return "所选来源不存在，请先保存来源清单。";
        }

        return null;
    }

    private static bool MatchesSource(SkillSourceRecord source, string? localName, ProfileKind? profile)
    {
        return !string.IsNullOrWhiteSpace(localName)
            && profile.HasValue
            && source.Profile == profile.Value
            && string.Equals(source.LocalName, localName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInstalledSkillDirectory(string hubRoot, ProfileKind profile, string relativePath)
    {
        return Path.Combine(
            hubRoot,
            "skills",
            profile.ToStorageValue(),
            NormalizePath(relativePath).Replace('/', Path.DirectorySeparatorChar));
    }

    private static string NormalizePath(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        return rawValue
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/')
            .TrimEnd('/');
    }

    private static string GetInstallKey(ProfileKind profile, string relativePath)
    {
        return profile + ":" + NormalizePath(relativePath);
    }

    private static string GetSourceKey(string localName, ProfileKind profile)
    {
        return profile + ":" + localName.Trim();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName, bool fallback)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : fallback;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
        {
            return value;
        }

        return null;
    }

    private static ProfileKind ReadProfile(JsonElement element, string propertyName, ProfileKind fallback)
    {
        var rawValue = ReadString(element, propertyName);
        return ProfileKindExtensions.TryParse(rawValue, out var profile) ? profile : fallback;
    }

    private static SkillScheduledUpdateAction ReadScheduledUpdateAction(JsonElement element, string propertyName)
    {
        var rawValue = ReadString(element, propertyName);
        return Enum.TryParse<SkillScheduledUpdateAction>(rawValue, true, out var parsed)
            ? parsed
            : SkillScheduledUpdateAction.CheckOnly;
    }

    private static SkillVersionTrackingMode ReadVersionTrackingMode(JsonElement element, string propertyName, SkillSourceKind kind)
    {
        var rawValue = ReadString(element, propertyName);
        if (Enum.TryParse<SkillVersionTrackingMode>(rawValue, true, out var parsed))
        {
            return parsed;
        }

        return kind == SkillSourceKind.GitRepository
            ? SkillVersionTrackingMode.FollowLatestStableTag
            : SkillVersionTrackingMode.FollowReferenceLegacy;
    }

    private static SkillSourceKind ReadSourceKind(JsonElement element)
    {
        var rawValue = ReadString(element, "kind");
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return SkillSourceKind.GitRepository;
        }

        if (Enum.TryParse<SkillSourceKind>(rawValue, true, out var parsed))
        {
            return parsed;
        }

        return rawValue.Equals("localDirectory", StringComparison.OrdinalIgnoreCase)
            ? SkillSourceKind.LocalDirectory
            : SkillSourceKind.GitRepository;
    }

    private static string GetSourcesPath(string hubRoot)
    {
        return Path.Combine(hubRoot, "skills", "sources.json");
    }

    private static string GetInstallsPath(string hubRoot)
    {
        return Path.Combine(hubRoot, "config", "skills-installs.json");
    }

    private static string GetStatesPath(string hubRoot)
    {
        return Path.Combine(hubRoot, "config", "skills-state.json");
    }

    private static int? NormalizeScheduledUpdateInterval(int? intervalHours)
    {
        return intervalHours switch
        {
            6 or 12 or 24 or 168 => intervalHours,
            _ => null
        };
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class SkillSourcesDocument
    {
        public List<SkillSourceRecord> Sources { get; set; } = new();
    }

    private sealed class SkillInstallsDocument
    {
        public List<SkillInstallRecord> Installs { get; set; } = new();
    }

    private sealed class SkillInstallStatesDocument
    {
        public List<SkillInstallStateRecord> States { get; set; } = new();
    }
}





