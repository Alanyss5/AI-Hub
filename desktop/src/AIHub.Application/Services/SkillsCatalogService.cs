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
    private static readonly IReadOnlyDictionary<string, int> ProfileSortOrder = WorkspaceProfiles.CreateDefaultCatalog()
        .Select((profile, index) => new KeyValuePair<string, int>(profile.Id, index))
        .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

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
        string? originalProfile,
        SkillSourceRecord draft,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 閺嶅湱娲拌ぐ鏇熸￥閺佸牞绱濋弮鐘崇《娣囨繂鐡?Skills 閺夈儲绨€?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var normalized = NormalizeSource(draft);
        var validationError = ValidateSource(normalized);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return OperationResult.Fail(validationError);
        }

        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var originalProfileId = string.IsNullOrWhiteSpace(originalProfile) ? null : WorkspaceProfiles.NormalizeId(originalProfile);
        sources.RemoveAll(source => MatchesSource(source, originalLocalName, originalProfileId));

        if (sources.Any(source => MatchesSource(source, normalized.LocalName, normalized.Profile)))
        {
            return OperationResult.Fail("瀹告彃鐡ㄩ崷銊ユ倱閸氬秴鎮撴担婊呮暏閸╃喓娈?Skills 閺夈儲绨€?, normalized.SourceDisplayName");
        }

        sources.Add(normalized);
        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);

        return OperationResult.Ok("Skills 鏉ユ簮宸蹭繚瀛樸€?, GetSourcesPath(resolution.RootPath)");
    }

    public async Task<OperationResult> DeleteSourceAsync(
        string localName,
        string profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 閺嶅湱娲拌ぐ鏇熸￥閺佸牞绱濋弮鐘崇《閸掔娀娅?Skills 閺夈儲绨€?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var removed = sources.RemoveAll(source => MatchesSource(source, localName, profileId));
        if (removed == 0)
        {
            return OperationResult.Fail("閺堫亝澹橀崚鎷岊洣閸掔娀娅庨惃?Skills 閺夈儲绨€?, localName");
        }

        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);
        return OperationResult.Ok("Skills 鏉ユ簮宸插垹闄ゃ€?, GetSourcesPath(resolution.RootPath)");
    }

    public async Task<OperationResult> SaveInstallAsync(
        SkillInstallRecord draft,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 閺嶅湱娲拌ぐ鏇熸￥閺佸牞绱濋弮鐘崇《娣囨繂鐡?Skill 鐎瑰顥婇惂鏄忣唶銆?, string.Join(Environment.NewLine, resolution.Errors)");
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
            return OperationResult.Fail("瑕佺櫥璁扮殑 Skill 鐩綍涓嶅瓨鍦ㄣ€?, skillDirectory");
        }

        if (!File.Exists(Path.Combine(skillDirectory, "SKILL.md")))
        {
            return OperationResult.Fail("閻╊喗鐖ｉ惄顔肩秿娑擃厾宸辩亸?SKILL.md閿涘本妫ゅ▔鏇犳鐠侀璐?Skill銆?, skillDirectory");
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
            return OperationResult.Ok("Skill 瀹夎鐧昏宸蹭繚瀛橈紝骞跺凡涓哄綋鍓嶅唴瀹瑰缓绔嬪熀绾裤€?, GetInstallsPath(resolution.RootPath)");
        }

        return OperationResult.Ok("Skill 瀹夎鐧昏宸蹭繚瀛樸€?, GetInstallsPath(resolution.RootPath)");
    }

    public async Task<OperationResult> DeleteInstallAsync(
        string profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 閺嶅湱娲拌ぐ鏇熸￥閺佸牞绱濋弮鐘崇《閸掔娀娅?Skill 閻ф槒顔囥€?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var installs = (await LoadInstallsAsync(resolution.RootPath, cancellationToken)).ToList();
        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profileId, normalizedRelativePath);

        var removedInstalls = installs.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        var removedStates = states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (removedInstalls == 0 && removedStates == 0)
        {
            return OperationResult.Fail("閺堫亝澹橀崚鎷岊洣閸掔娀娅庨惃?Skill 閻ф槒顔囥€?, normalizedRelativePath");
        }

        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
        return OperationResult.Ok("Skill 閻ф槒顔囨稉搴＄唨缁惧灝鍑￠崚鐘绘珟銆?, GetInstallsPath(resolution.RootPath)");
    }

    public async Task<OperationResult> SaveSkillBindingsAsync(
        string sourceProfile,
        string relativePath,
        IReadOnlyList<string> targetProfiles,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill bindings could not be saved.", string.Join(Environment.NewLine, resolution.Errors));
        }
        var normalizedSourceProfile = WorkspaceProfiles.NormalizeId(sourceProfile);
        var normalizedRelativePath = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return OperationResult.Fail("Select a skill before editing bindings.");
        }
        var normalizedTargets = NormalizeProfiles(targetProfiles);
        var installs = (await LoadInstallsAsync(resolution.RootPath, cancellationToken)).ToList();
        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var sourceDirectory = GetInstalledSkillDirectory(resolution.RootPath, normalizedSourceProfile, normalizedRelativePath);
        var existingProfiles = GetExistingSkillProfiles(resolution.RootPath, installs, states, normalizedRelativePath);
        var impactedProfiles = existingProfiles
            .Concat(normalizedTargets)
            .Append(normalizedSourceProfile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedTargets.Count > 0 && !Directory.Exists(sourceDirectory))
        {
            return OperationResult.Fail("The selected skill source directory does not exist.", sourceDirectory);
        }
        var sourceInstall = installs.FirstOrDefault(item =>
            string.Equals(item.Profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.InstalledRelativePath, normalizedRelativePath, StringComparison.OrdinalIgnoreCase));
        var sourceState = states.FirstOrDefault(item =>
            string.Equals(item.Profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.InstalledRelativePath, normalizedRelativePath, StringComparison.OrdinalIgnoreCase));
        foreach (var profile in impactedProfiles)
        {
            var destinationDirectory = GetInstalledSkillDirectory(resolution.RootPath, profile, normalizedRelativePath);
            var isSelected = normalizedTargets.Contains(profile, StringComparer.OrdinalIgnoreCase);
            if (isSelected)
            {
                if (!string.Equals(profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceDirectoryWithSource(sourceDirectory, destinationDirectory);
                }
                installs.RemoveAll(item => IsSkillInstall(item, profile, normalizedRelativePath));
                if (sourceInstall is not null)
                {
                    installs.Add(sourceInstall with { Profile = profile });
                }
                states.RemoveAll(item => IsSkillState(item, profile, normalizedRelativePath));
                if (sourceState is not null)
                {
                    states.Add(sourceState with { Profile = profile });
                }
            }
            else
            {
                installs.RemoveAll(item => IsSkillInstall(item, profile, normalizedRelativePath));
                states.RemoveAll(item => IsSkillState(item, profile, normalizedRelativePath));
                if (Directory.Exists(destinationDirectory))
                {
                    DeleteDirectory(destinationDirectory);
                }
            }
        }
        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
        return OperationResult.Ok(
            "Skill bindings saved.",
            $"{normalizedRelativePath}{Environment.NewLine}{string.Join(Environment.NewLine, normalizedTargets)}");
    }
    public async Task<OperationResult> SaveSkillGroupBindingsAsync(
        string sourceProfile,
        string relativeGroupPath,
        IReadOnlyList<string> targetProfiles,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill folder bindings could not be saved.", string.Join(Environment.NewLine, resolution.Errors));
        }
        var normalizedSourceProfile = WorkspaceProfiles.NormalizeId(sourceProfile);
        var normalizedGroupPath = NormalizePath(relativeGroupPath);
        if (string.IsNullOrWhiteSpace(normalizedGroupPath))
        {
            return OperationResult.Fail("Select a skill repository or folder before editing bindings.");
        }
        var normalizedTargets = NormalizeProfiles(targetProfiles);
        var installs = (await LoadInstallsAsync(resolution.RootPath, cancellationToken)).ToList();
        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var sourceGroupDirectory = GetInstalledSkillDirectory(resolution.RootPath, normalizedSourceProfile, normalizedGroupPath);
        var existingProfiles = GetExistingGroupProfiles(resolution.RootPath, installs, states, normalizedGroupPath);
        var impactedProfiles = existingProfiles
            .Concat(normalizedTargets)
            .Append(normalizedSourceProfile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedTargets.Count > 0 && !Directory.Exists(sourceGroupDirectory))
        {
            return OperationResult.Fail("The selected skill repository or folder does not exist.", sourceGroupDirectory);
        }
        var sourceInstalls = installs
            .Where(item => string.Equals(item.Profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase)
                           && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath))
            .Select(item => item with { })
            .ToArray();
        var sourceStates = states
            .Where(item => string.Equals(item.Profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase)
                           && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath))
            .Select(item => item with { })
            .ToArray();
        foreach (var profile in impactedProfiles)
        {
            var destinationDirectory = GetInstalledSkillDirectory(resolution.RootPath, profile, normalizedGroupPath);
            var isSelected = normalizedTargets.Contains(profile, StringComparer.OrdinalIgnoreCase);
            if (isSelected)
            {
                if (!string.Equals(profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(destinationDirectory))
                    {
                        DeleteDirectory(destinationDirectory);
                    }
                    CopyDirectory(sourceGroupDirectory, destinationDirectory);
                }
                installs.RemoveAll(item => string.Equals(item.Profile, profile, StringComparison.OrdinalIgnoreCase)
                                           && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
                installs.AddRange(sourceInstalls.Select(item => item with { Profile = profile }));
                states.RemoveAll(item => string.Equals(item.Profile, profile, StringComparison.OrdinalIgnoreCase)
                                         && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
                states.AddRange(sourceStates.Select(item => item with { Profile = profile }));
            }
            else
            {
                installs.RemoveAll(item => string.Equals(item.Profile, profile, StringComparison.OrdinalIgnoreCase)
                                           && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
                states.RemoveAll(item => string.Equals(item.Profile, profile, StringComparison.OrdinalIgnoreCase)
                                         && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
                if (Directory.Exists(destinationDirectory))
                {
                    DeleteDirectory(destinationDirectory);
                }
            }
        }
        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
        return OperationResult.Ok(
            "Skill repository bindings saved.",
            $"{normalizedGroupPath}{Environment.NewLine}{string.Join(Environment.NewLine, normalizedTargets)}");
    }
    public async Task<OperationResult> CaptureBaselineAsync(
        string profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 閺嶅湱娲拌ぐ鏇熸￥閺佸牞绱濋弮鐘崇《闁插秴缂?Skill 閸╄櫣鍤庛€?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        if (!installs.Any(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(profileId, normalizedRelativePath)))
        {
            return OperationResult.Fail("璇峰厛淇濆瓨璇?Skill 鐨勭櫥璁颁俊鎭紝鍐嶅缓绔嬪熀绾裤€?, normalizedRelativePath");
        }

        var skillDirectory = GetInstalledSkillDirectory(resolution.RootPath, profileId, normalizedRelativePath);
        if (!Directory.Exists(skillDirectory))
        {
            return OperationResult.Fail("鐩爣 Skill 鐩綍涓嶅瓨鍦ㄣ€?, skillDirectory");
        }

        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profileId, normalizedRelativePath);
        states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        states.Add(CreateStateRecord(profileId, normalizedRelativePath, skillDirectory));
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);

        return OperationResult.Ok("Skill 鍩虹嚎宸查噸寤恒€?, GetStatesPath(resolution.RootPath)");
    }

    public async Task<OperationResult> ScanSourceAsync(
        string localName,
        string profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 閺嶅湱娲拌ぐ鏇熸￥閺佸牞绱濋弮鐘崇《閹殿偅寮?Skills 閺夈儲绨€?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var sources = await LoadSourcesAsync(resolution.RootPath, cancellationToken);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var source = sources.FirstOrDefault(item => MatchesSource(item, localName, profileId));
        if (source is null)
        {
            return OperationResult.Fail("閺堫亝澹橀崚鎷岊洣閹殿偅寮块惃?Skills 閺夈儲绨€?, localName");
        }

        if (!source.IsEnabled)
        {
            return OperationResult.Fail("瑜版挸澧犻弶銉︾爱瀹歌尙顩﹂悽顭掔礉閺冪姵纭堕幍顐ｅ伎銆?, source.SourceDisplayName");
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
                    "閺夈儲绨幍顐ｅ伎鐎瑰本鍨氶敍灞肩稻濞屸剝婀侀崣鎴犲箛娴犺缍?Skill銆?",
                    BuildSourceDetails(source, resolvedSource, Array.Empty<string>()));
            }

            var detailLines = discoveredSkills
                .Select(item => $"- {item.RelativePath} ({item.Name})")
                .ToArray();

            return OperationResult.Ok(
                "閺夈儲绨幍顐ｅ伎鐎瑰本鍨氥€?",
                BuildSourceDetails(source, resolvedSource, detailLines));
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("The skill source could not be scanned.", exception.Message);
        }
    }

    public async Task<OperationResult> CheckForUpdatesAsync(
        string profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var contextResult = await TryCreateInstallContextAsync(profileId, relativePath, refreshRemote: true, cancellationToken);
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

        var message = hasUpdate ? "Updates are available." : "Installed files already match the source baseline.";
        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            message += " Sync is currently blocked by local state.";
        }

        return OperationResult.Ok(message, BuildUpdateDetails(context, hasUpdate, blockedReason));
    }

    public async Task<OperationResult> PreviewInstalledSkillDiffAsync(
        string profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var contextResult = await TryCreateInstallContextAsync(profileId, relativePath, refreshRemote: true, cancellationToken);
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
        var message = hasSourceDiff ? "A diff preview has been generated." : "Installed files already match the source.";

        return OperationResult.Ok(
            message,
            BuildDiffPreviewDetails(context, blockedReason, sourceAdded, sourceChanged, sourceRemoved, localAdded, localChanged, localRemoved));
    }
    public async Task<OperationResult> SyncInstalledSkillAsync(
        string profile,
        string relativePath,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var contextResult = await TryCreateInstallContextAsync(profileId, relativePath, refreshRemote: true, cancellationToken);
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
            return OperationResult.Ok("瑜版挸澧犲鍙夋Ц閺堚偓閺傛澘鍞寸€圭櫢绱濋弮鐘绘付閸氬本顒炪€?, BuildUpdateDetails(context, hasUpdate: false, blockedReason: null)");
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
        detailBuilder.AppendLine("Backup snapshot: " + backupPath);
        detailBuilder.AppendLine("Sync mode: " + (force ? "force" : "safe"));
        if (!overlaySnapshot.IsEmpty)
        {
            detailBuilder.AppendLine("Overlay reapplied: " + overlaySnapshot.FileCount + " files / deleted " + overlaySnapshot.DeletedFiles.Count + " entries");
        }

        return OperationResult.Ok(force ? "Skill force sync completed." : "Skill safe sync completed.", detailBuilder.ToString().TrimEnd());
    }

    public async Task<OperationResult> RollbackInstalledSkillAsync(
        string profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 閺嶅湱娲拌ぐ鏇熸￥閺佸牞绱濋弮鐘崇《閸ョ偞绮?Skill銆?, string.Join(Environment.NewLine, resolution.Errors)");
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var installDirectory = GetInstalledSkillDirectory(resolution.RootPath, profileId, normalizedRelativePath);
        if (!Directory.Exists(installDirectory))
        {
            return OperationResult.Fail("鐩爣 Skill 鐩綍涓嶅瓨鍦ㄣ€?, installDirectory");
        }

        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profileId, normalizedRelativePath);
        var state = states.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (state is null)
        {
            return OperationResult.Fail("鐠?Skill 鏉╂ɑ鐥呴張澶婃倱濮濄儳濮搁幀渚婄礉閺冪姵纭堕崶鐐寸泊銆?, normalizedRelativePath");
        }

        var backupPath = ResolveRollbackBackupPath(resolution.RootPath, state, profileId, normalizedRelativePath);
        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
        {
            return OperationResult.Fail("娌℃湁鍙敤鐨勫洖婊氬浠姐€?, normalizedRelativePath");
        }

        var currentSnapshotBackupPath = CreateBackupSnapshot(resolution.RootPath, profileId, normalizedRelativePath, installDirectory, "pre-rollback");
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
        detailBuilder.AppendLine("瀹歌弓绮犳径鍥﹀敜閸ョ偞绮?Skill銆?");
        detailBuilder.AppendLine("閸ョ偞绮撮弶銉︾爱锛? + backupPath");
        detailBuilder.AppendLine("瑜版挸澧犻崘鍛啇婢跺洣鍞わ細" + currentSnapshotBackupPath);
        detailBuilder.AppendLine("鐎瑰顥婇惄顔肩秿锛? + installDirectory");

        return OperationResult.Ok("Skill 宸插洖婊氬埌鏈€杩戝浠姐€?, detailBuilder.ToString().TrimEnd()");
    }

    private async Task<SkillContextResult> TryCreateInstallContextAsync(
        string profile,
        string relativePath,
        bool refreshRemote,
        CancellationToken cancellationToken)
    {
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return SkillContextResult.Fail(OperationResult.Fail("AI-Hub hub root is invalid. Skill context could not be created.", string.Join(Environment.NewLine, resolution.Errors)));
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return SkillContextResult.Fail(OperationResult.Fail("Skill path cannot be empty."));
        }

        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        var install = installs.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(profileId, normalizedRelativePath));
        if (install is null)
        {
            return SkillContextResult.Fail(OperationResult.Fail("The skill is not registered yet.", normalizedRelativePath));
        }

        if (install.CustomizationMode == SkillCustomizationMode.Local)
        {
            return SkillContextResult.Fail(OperationResult.Fail("Local-mode skills do not participate in upstream sync.", normalizedRelativePath));
        }

        if (string.IsNullOrWhiteSpace(install.SourceLocalName) || string.IsNullOrWhiteSpace(install.SourceProfile))
        {
            return SkillContextResult.Fail(OperationResult.Fail("The skill is missing a bound source.", normalizedRelativePath));
        }

        var sources = await LoadSourcesAsync(resolution.RootPath, cancellationToken);
        var source = sources.FirstOrDefault(item => MatchesSource(item, install.SourceLocalName, install.SourceProfile));
        if (source is null)
        {
            return SkillContextResult.Fail(OperationResult.Fail("The bound source does not exist.", install.SourceLocalName));
        }

        if (!source.IsEnabled)
        {
            return SkillContextResult.Fail(OperationResult.Fail("The bound source is disabled.", source.SourceDisplayName));
        }

        var installedSkillDirectory = GetInstalledSkillDirectory(resolution.RootPath, install.Profile, install.InstalledRelativePath);
        if (!Directory.Exists(installedSkillDirectory))
        {
            return SkillContextResult.Fail(OperationResult.Fail("The installed skill directory does not exist.", installedSkillDirectory));
        }

        try
        {
            var resolvedSource = await ResolveSourceAsync(source, refreshRemote, cancellationToken);
            var sourceSkillDirectory = ResolveSourceSkillDirectory(resolvedSource, install);
            var sourceFingerprints = CaptureFingerprints(sourceSkillDirectory);
            var installedFingerprints = CaptureFingerprints(installedSkillDirectory);
            var states = await LoadStatesAsync(resolution.RootPath, cancellationToken);
            var state = states.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(profileId, normalizedRelativePath))
                ?? new SkillInstallStateRecord
                {
                    Profile = profileId,
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
            return SkillContextResult.Fail(OperationResult.Fail("The skill source could not be resolved.", exception.Message));
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

        var skillsRoot = Path.Combine(hubRoot, "skills");
        if (!Directory.Exists(skillsRoot))
        {
            return [];
        }

        foreach (var profileRoot in Directory.EnumerateDirectories(skillsRoot)
                     .OrderBy(path => GetProfileSortOrder(Path.GetFileName(path)))
                     .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var profile = WorkspaceProfiles.NormalizeId(Path.GetFileName(profileRoot));
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
                if (!string.IsNullOrWhiteSpace(install?.SourceLocalName) && !string.IsNullOrWhiteSpace(install.SourceProfile))
                {
                    sourceMap.TryGetValue(GetSourceKey(install.SourceLocalName, install.SourceProfile), out source);
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
                    ProfileDisplayName = WorkspaceProfiles.ToDisplayName(profile),
                    DirectoryPath = skillDirectory,
                    RelativePath = relativePath,
                    HasManifest = true,
                    IsRegistered = install is not null,
                    CustomizationMode = mode,
                    HasBaseline = hasBaseline,
                    IsDirty = isDirty,
                    SourceLocalName = install?.SourceLocalName,
                    SourceProfile = install?.SourceProfile,
                    SourceProfileDisplayName = string.IsNullOrWhiteSpace(install?.SourceProfile) ? string.Empty : WorkspaceProfiles.ToDisplayName(install.SourceProfile),
                    SourceSkillPath = install?.SourceSkillPath,
                    BaselineDisplay = hasBaseline
                        ? "Baseline captured: " + state!.BaselineCapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : "No baseline captured yet",
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
            .OrderBy(skill => GetProfileSortOrder(skill.Profile))
            .ThenBy(skill => skill.Profile, StringComparer.OrdinalIgnoreCase)
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
            return "閺堫亞娅ョ拋鐗堟降濠ф劒绗岄弴瀛樻煀缁涙牜鏆?";
        }

        if (sourceMissing && mode != SkillCustomizationMode.Local)
        {
            return "宸茬櫥璁帮紝浣嗙粦瀹氱殑鏉ユ簮璁板綍涓嶅瓨鍦?";
        }

        if (!hasBaseline)
        {
            return "瀹歌尙娅ョ拋甯礉鐏忔碍婀铏圭彌閸╄櫣鍤?";
        }

        if (isDirty)
        {
            return mode switch
            {
                SkillCustomizationMode.Managed => "閹垫顓稿Ο鈥崇础閿涘本顥呭ù瀣煂閺堫剙婀存穱顔芥暭",
                SkillCustomizationMode.Overlay => "鐟曞棛娲婄仦鍌浤佸蹇ョ礉鐎涙ê婀張顒€婀寸憰鍡欐磰閺€鐟板З",
                SkillCustomizationMode.Fork => "Fork 妯″紡锛屽瓨鍦ㄦ柊鐨勬湰鍦颁慨鏀?",
                SkillCustomizationMode.Local => "鏈湴妯″紡锛屽瓨鍦ㄦ柊鐨勬湰鍦颁慨鏀?",
                _ => "鐎涙ê婀張顒€婀存穱顔芥暭"
            };
        }

        return mode switch
        {
            SkillCustomizationMode.Managed => "Managed mode, in sync with baseline",
            SkillCustomizationMode.Overlay => "Overlay mode, in sync with baseline",
            SkillCustomizationMode.Fork => "Fork mode, in sync with baseline",
            SkillCustomizationMode.Local => "Local mode, in sync with baseline",
            _ => "In sync with baseline"
        };
    }

    private static SkillInstallStateRecord CreateStateRecord(string profile, string relativePath, string skillDirectory)
    {
        return new SkillInstallStateRecord
        {
            Profile = WorkspaceProfiles.NormalizeId(profile),
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
            var workingRootPath = NormalizeExistingDirectory(source.Location, "閺夈儲绨惄顔肩秿娑撳秴鐡ㄩ崷顭掔窗");
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
        builder.AppendLine("閺夈儲绨細" + source.SourceDisplayName);
        builder.AppendLine("缁鐎凤細" + source.KindDisplay);
        builder.AppendLine("鐟欙絾鐎介惄顔肩秿锛? + resolvedSource.WorkingRootPath");
        builder.AppendLine("閻╊喖缍嶉懠鍐ㄦ纯锛? + resolvedSource.CatalogRootPath");
        builder.AppendLine("瑜版挸澧犲鏇犳暏锛? + resolvedSource.ResolvedReference");

        if (detailLines.Count == 0)
        {
            builder.AppendLine("閺堫亜褰傞悳棰佹崲娴?Skill銆?");
        }
        else
        {
            builder.AppendLine("鍙戠幇鐨?Skills锛?");
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
        builder.AppendLine("Skill: " + context.Install.Name);
        builder.AppendLine("Installed directory: " + context.InstalledSkillDirectory);
        builder.AppendLine("Source: " + context.Source.SourceDisplayName);
        builder.AppendLine("Source directory: " + context.SourceSkillDirectory);
        builder.AppendLine("Mode: " + context.Install.CustomizationMode.ToDisplayName());
        builder.AppendLine("Local state: " + (context.IsDirty ? "Local modifications detected" : "Matches baseline"));
        builder.AppendLine("Resolved source ref: " + context.ResolvedSource.ResolvedReference);
        builder.AppendLine("Upstream state: " + (hasUpdate ? "Updates available" : "Matches source baseline"));

        if (!string.IsNullOrWhiteSpace(context.State.LastAppliedReference))
        {
            builder.AppendLine("娑撳﹥顐奸崥灞绢劄瀵洜鏁わ細" + context.State.LastAppliedReference);
        }

        if (context.State.LastSyncAt.HasValue)
        {
            builder.AppendLine("娑撳﹥顐奸崥灞绢劄閺冨爼妫匡細" + context.State.LastSyncAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (!string.IsNullOrWhiteSpace(context.State.LastBackupPath))
        {
            builder.AppendLine("閺堚偓鏉╂垵顦禒鏂ょ窗" + context.State.LastBackupPath);
        }

        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            builder.AppendLine("閸氬本顒為梽鎰煑锛? + blockedReason");
        }

        return builder.ToString().TrimEnd();
    }

    private static string? GetSyncBlockedReason(SkillInstallContext context, bool force)
    {
        if (context.Install.CustomizationMode == SkillCustomizationMode.Local)
        {
            return "鏈湴妯″紡鐨?Skill 涓嶅弬涓庝笂娓稿悓姝ャ€?";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Overlay && context.State.BaselineFiles.Count == 0)
        {
            return "瑕嗙洊灞傛ā寮忛渶瑕佸厛寤虹珛鍩虹嚎锛屾墠鑳藉畨鍏ㄩ噸鏀炬湰鍦拌鐩栧唴瀹广€?";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Fork && !force)
        {
            return "Fork 濡€崇础姒涙顓绘稉宥堝殰閸斻劏顩惄鏍电礉鐠囬攱鏁奸悽銊ュ繁閸掕泛鎮撳銉﹀灗閹靛浼愭径鍕倞銆?";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Overlay)
        {
            return null;
        }

        if (context.IsDirty && !force)
        {
            return "妫€娴嬪埌鏈湴淇敼锛岄粯璁や笉浼氳鐩栥€傝鍏堥噸寤哄熀绾裤€佽皟鏁存ā寮忥紝鎴栨敼鐢ㄥ己鍒跺悓姝ャ€?";
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
        string profile,
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
        string profile,
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

    private static string GetBackupRoot(string hubRoot, string profile, string relativePath)
    {
        return Path.Combine(
            hubRoot,
            "backups",
            "skills",
            WorkspaceProfiles.NormalizeId(profile),
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

    private static void DeleteDirectory(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
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
                failureMessage: "閸掓繂顫愰崠?Git 閺夈儲绨紓鎾崇摠婢惰精瑙︺€?",
                cancellationToken);
        }
        else if (refreshRemote)
        {
            await EnsureProcessSuccessAsync(
                "git",
                ["-C", cacheDirectory, "fetch", "--all", "--tags", "--prune"],
                workingDirectory: null,
                failureMessage: "閺囧瓨鏌?Git 閺夈儲绨紓鎾崇摠婢惰精瑙︺€?",
                cancellationToken);
        }

        var reference = string.IsNullOrWhiteSpace(source.Reference) ? "main" : source.Reference;
        await EnsureProcessSuccessAsync(
            "git",
            ["-C", cacheDirectory, "checkout", "--force", reference],
            workingDirectory: null,
            failureMessage: "閸掑洦宕查崚鐗堝瘹鐎?Git 瀵洜鏁ゆ径杈Е銆?",
            cancellationToken);

        var remoteReference = "origin/" + reference;
        var remoteReferenceExists = await DoesGitReferenceExistAsync(cacheDirectory, remoteReference, cancellationToken);
        if (remoteReferenceExists)
        {
            await EnsureProcessSuccessAsync(
                "git",
                ["-C", cacheDirectory, "reset", "--hard", remoteReference],
                workingDirectory: null,
                failureMessage: "閸氬本顒炴潻婊咁伂閸掑棙鏁径杈Е銆?",
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
            throw new InvalidOperationException("閺夈儲绨惄顔肩秿閼煎啫娲挎稉宥呯摠閸︻煉绱? + catalogRootPath");
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

        throw new InvalidOperationException("閺冪姵纭剁涵顔肩暰閺夈儲绨稉顓犳畱 Skill 閻╊喖缍嶉敍宀冾嚞閸︺劍娼靛┃鎰厬閻ㄥ嫭濡ч懗鍊熺熅瀵板嫪鑵戦弰搴ｂ€樻繅顐㈠晸銆?");
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
            .OrderBy(source => GetProfileSortOrder(source.Profile))
            .ThenBy(source => source.Profile, StringComparer.OrdinalIgnoreCase)
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
            Profile = ReadProfile(element, "profile", WorkspaceProfiles.GlobalId),
            ProfileDisplayName = ReadString(element, "profileDisplayName") ?? string.Empty,
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
            .OrderBy(item => GetProfileSortOrder(item.Profile))
            .ThenBy(item => item.Profile, StringComparer.OrdinalIgnoreCase)
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
            .OrderBy(item => GetProfileSortOrder(item.Profile))
            .ThenBy(item => item.Profile, StringComparer.OrdinalIgnoreCase)
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
            Profile = WorkspaceProfiles.NormalizeId(record.Profile),
            ProfileDisplayName = WorkspaceProfiles.NormalizeDisplayName(record.ProfileDisplayName, record.Profile),
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
            Profile = WorkspaceProfiles.NormalizeId(record.Profile),
            Name = string.IsNullOrWhiteSpace(record.Name) ? Path.GetFileName(normalizedRelativePath) : record.Name.Trim(),
            InstalledRelativePath = normalizedRelativePath,
            SourceLocalName = string.IsNullOrWhiteSpace(record.SourceLocalName) ? null : record.SourceLocalName.Trim(),
            SourceProfile = string.IsNullOrWhiteSpace(record.SourceProfile) ? null : WorkspaceProfiles.NormalizeId(record.SourceProfile),
            SourceSkillPath = string.IsNullOrWhiteSpace(record.SourceSkillPath) ? null : NormalizePath(record.SourceSkillPath)
        };
    }

    private static SkillInstallStateRecord NormalizeState(SkillInstallStateRecord record)
    {
        return record with
        {
            Profile = WorkspaceProfiles.NormalizeId(record.Profile),
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
            return "閺夈儲绨崥宥囆炴稉宥堝厴娑撹櫣鈹栥€?";
        }

        if (string.IsNullOrWhiteSpace(record.Location))
        {
            return "閺夈儲绨崷鏉挎絻娑撳秷鍏樻稉铏光敄銆?";
        }

        return null;
    }

    private static string? ValidateInstall(SkillInstallRecord record, IReadOnlyList<SkillSourceRecord> sources)
    {
        if (string.IsNullOrWhiteSpace(record.InstalledRelativePath))
        {
            return "Skill 鐨勫畨瑁呰矾寰勪笉鑳戒负绌恒€?";
        }

        if (record.CustomizationMode == SkillCustomizationMode.Local)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(record.SourceLocalName) || string.IsNullOrWhiteSpace(record.SourceProfile))
        {
            return "闄も€滄湰鍦扳€濇ā寮忓锛屽叾瀹冩ā寮忛兘闇€瑕佺粦瀹氫竴涓潵婧愩€?";
        }

        if (!sources.Any(source => MatchesSource(source, record.SourceLocalName, record.SourceProfile)))
        {
            return "鎵€閫夋潵婧愪笉瀛樺湪锛岃鍏堜繚瀛樻潵婧愭竻鍗曘€?";
        }

        return null;
    }

    private static bool MatchesSource(SkillSourceRecord source, string? localName, string? profile)
    {
        return !string.IsNullOrWhiteSpace(localName)
            && !string.IsNullOrWhiteSpace(profile)
            && string.Equals(source.Profile, WorkspaceProfiles.NormalizeId(profile), StringComparison.OrdinalIgnoreCase)
            && string.Equals(source.LocalName, localName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInstalledSkillDirectory(string hubRoot, string profile, string relativePath)
    {
        return Path.Combine(
            hubRoot,
            "skills",
            WorkspaceProfiles.NormalizeId(profile),
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

    private static int GetProfileSortOrder(string? profile)
    {
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        return ProfileSortOrder.TryGetValue(normalizedProfile, out var sortOrder)
            ? sortOrder
            : ProfileSortOrder.Count;
    }

    private static string GetInstallKey(string profile, string relativePath)
    {
        return WorkspaceProfiles.NormalizeId(profile) + ":" + NormalizePath(relativePath);
    }

    private static List<string> NormalizeProfiles(IEnumerable<string> profiles)
    {
        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetProfileSortOrder)
            .ThenBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetExistingSkillProfiles(
        string hubRoot,
        IReadOnlyList<SkillInstallRecord> installs,
        IReadOnlyList<SkillInstallStateRecord> states,
        string relativePath)
    {
        var normalizedRelativePath = NormalizePath(relativePath);
        var profiles = installs
            .Where(item => string.Equals(item.InstalledRelativePath, normalizedRelativePath, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Profile)
            .Concat(states
                .Where(item => string.Equals(item.InstalledRelativePath, normalizedRelativePath, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Profile))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skillsRoot = Path.Combine(hubRoot, "skills");
        if (Directory.Exists(skillsRoot))
        {
            foreach (var profileRoot in Directory.EnumerateDirectories(skillsRoot))
            {
                var profile = WorkspaceProfiles.NormalizeId(Path.GetFileName(profileRoot));
                var directory = GetInstalledSkillDirectory(hubRoot, profile, normalizedRelativePath);
                if (Directory.Exists(directory))
                {
                    profiles.Add(profile);
                }
            }
        }

        return profiles.ToArray();
    }

    private static IReadOnlyList<string> GetExistingGroupProfiles(
        string hubRoot,
        IReadOnlyList<SkillInstallRecord> installs,
        IReadOnlyList<SkillInstallStateRecord> states,
        string groupPath)
    {
        var normalizedGroupPath = NormalizePath(groupPath);
        var profiles = installs
            .Where(item => IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath))
            .Select(item => item.Profile)
            .Concat(states
                .Where(item => IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath))
                .Select(item => item.Profile))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skillsRoot = Path.Combine(hubRoot, "skills");
        if (Directory.Exists(skillsRoot))
        {
            foreach (var profileRoot in Directory.EnumerateDirectories(skillsRoot))
            {
                var profile = WorkspaceProfiles.NormalizeId(Path.GetFileName(profileRoot));
                var directory = GetInstalledSkillDirectory(hubRoot, profile, normalizedGroupPath);
                if (Directory.Exists(directory))
                {
                    profiles.Add(profile);
                }
            }
        }

        return profiles.ToArray();
    }

    private static bool IsSkillInstall(SkillInstallRecord record, string profile, string relativePath)
    {
        return string.Equals(record.Profile, WorkspaceProfiles.NormalizeId(profile), StringComparison.OrdinalIgnoreCase)
               && string.Equals(record.InstalledRelativePath, NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkillState(SkillInstallStateRecord record, string profile, string relativePath)
    {
        return string.Equals(record.Profile, WorkspaceProfiles.NormalizeId(profile), StringComparison.OrdinalIgnoreCase)
               && string.Equals(record.InstalledRelativePath, NormalizePath(relativePath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathWithinScope(string relativePath, string groupPath)
    {
        var normalizedPath = NormalizePath(relativePath);
        var normalizedGroupPath = NormalizePath(groupPath);
        return string.Equals(normalizedPath, normalizedGroupPath, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(normalizedGroupPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSourceKey(string localName, string profile)
    {
        return WorkspaceProfiles.NormalizeId(profile) + ":" + localName.Trim();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return TryGetPropertyCaseInsensitive(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName, bool fallback)
    {
        return TryGetPropertyCaseInsensitive(element, propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : fallback;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property))
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

    private static string ReadProfile(JsonElement element, string propertyName, string fallback)
    {
        var rawValue = ReadString(element, propertyName);
        return WorkspaceProfiles.NormalizeId(rawValue ?? fallback);
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

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
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

