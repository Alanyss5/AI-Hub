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
    private const string LibraryProfileId = "library";
    private const string LibraryProfileDisplayName = "未绑定";
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly IReadOnlyDictionary<string, int> ProfileSortOrder = WorkspaceProfiles.CreateDefaultCatalog()
        .Select((profile, index) => new KeyValuePair<string, int>(profile.Id, index))
        .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    private static readonly ISourcePathLayout SourcePathLayout = new DefaultSourcePathLayout();

    private readonly IHubRootLocator _hubRootLocator;
    private readonly Func<string?, IHubSettingsStore>? _hubSettingsStoreFactory;
    private readonly Func<string?, IProjectRegistry>? _projectRegistryFactory;
    private readonly IWorkspaceAutomationService? _workspaceAutomationService;
    private readonly HashSet<string> _automaticMaintenanceCompletedRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _automaticMaintenanceGate = new();

    public SkillsCatalogService(IHubRootLocator hubRootLocator)
    : this(hubRootLocator, null, null, null)
    {
    }

    public SkillsCatalogService(IHubRootLocator hubRootLocator, Func<string?, IHubSettingsStore>? hubSettingsStoreFactory)
    : this(hubRootLocator, hubSettingsStoreFactory, null, null)
    {
    }

    public SkillsCatalogService(
        IHubRootLocator hubRootLocator,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory,
        IWorkspaceAutomationService? workspaceAutomationService)
        : this(hubRootLocator, hubSettingsStoreFactory, null, workspaceAutomationService)
    {
    }

    public SkillsCatalogService(
        IHubRootLocator hubRootLocator,
        Func<string?, IHubSettingsStore>? hubSettingsStoreFactory,
        Func<string?, IProjectRegistry>? projectRegistryFactory,
        IWorkspaceAutomationService? workspaceAutomationService)
    {
        _hubRootLocator = hubRootLocator;
        _hubSettingsStoreFactory = hubSettingsStoreFactory;
        _projectRegistryFactory = projectRegistryFactory;
        _workspaceAutomationService = workspaceAutomationService;
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

        EnsureSourceLayoutMigrated(resolution.RootPath);
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
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill sources could not be saved.", string.Join(Environment.NewLine, resolution.Errors));
        }

        EnsureSourceLayoutMigrated(resolution.RootPath);
        var normalized = NormalizeSource(draft);
        var validationError = ValidateSource(normalized);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return OperationResult.Fail(validationError);
        }

        EnsureSourceLayoutMigrated(resolution.RootPath);
        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var originalProfileId = string.IsNullOrWhiteSpace(originalProfile) ? null : WorkspaceProfiles.NormalizeId(originalProfile);
        sources.RemoveAll(source => MatchesSource(source, originalLocalName, originalProfileId));

        if (sources.Any(source => MatchesSource(source, normalized.LocalName, normalized.Profile)))
        {
            return OperationResult.Fail("A skill source with the same name and profile already exists.", normalized.SourceDisplayName);
        }

        sources.Add(normalized);
        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);
        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        var originalSource = string.IsNullOrWhiteSpace(originalLocalName)
            ? null
            : new SkillSourceRecord
            {
                LocalName = originalLocalName.Trim(),
                Profile = originalProfileId ?? normalized.Profile
            };
        var impactedProfiles = installs
            .Where(item => MatchesSource(normalized, item.SourceLocalName, item.SourceProfile)
                        || (originalSource is not null && MatchesSource(originalSource, item.SourceLocalName, item.SourceProfile)))
            .Select(item => item.Profile)
            .Append(normalized.Profile)
            .Append(originalProfileId)
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await RefreshRuntimeAsync(resolution.RootPath, impactedProfiles, cancellationToken);

        return OperationResult.Ok("Skill source saved.", GetSourcesPath(resolution.RootPath));
    }



    public async Task<OperationResult> DeleteSourceAsync(
        string localName,
        string profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill sources could not be deleted.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var sources = (await LoadSourcesAsync(resolution.RootPath, cancellationToken)).ToList();
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var removed = sources.RemoveAll(source => MatchesSource(source, localName, profileId));
        if (removed == 0)
        {
            return OperationResult.Fail("The selected skill source does not exist.", localName);
        }

        await SaveSourcesAsync(resolution.RootPath, sources, cancellationToken);
        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        var impactedProfiles = installs
            .Where(item => string.Equals(item.SourceProfile, profileId, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(item.SourceLocalName, localName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Profile)
            .Append(profileId)
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await RefreshRuntimeAsync(resolution.RootPath, impactedProfiles, cancellationToken);
        return OperationResult.Ok("Skill source deleted.", GetSourcesPath(resolution.RootPath));
    }

    public async Task<OperationResult> SaveInstallAsync(
        SkillInstallRecord draft,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill install records could not be saved.", string.Join(Environment.NewLine, resolution.Errors));
        }

        EnsureSourceLayoutMigrated(resolution.RootPath);
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
            return OperationResult.Fail("The skill directory to register does not exist.", skillDirectory);
        }

        if (!File.Exists(Path.Combine(skillDirectory, "SKILL.md")))
        {
            return OperationResult.Fail("The selected skill is missing SKILL.md and cannot be registered.", skillDirectory);
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
            await RuntimeRefreshCoordinator.RefreshAsync(
                resolution.RootPath,
                new[] { normalized.Profile },
                projectRegistryFactory: _projectRegistryFactory,
                hubSettingsStoreFactory: _hubSettingsStoreFactory,
                workspaceAutomationService: _workspaceAutomationService,
                cancellationToken);
            return OperationResult.Ok("Skill install record saved, and a baseline was created from the current files.", GetInstallsPath(resolution.RootPath));
        }

        await RuntimeRefreshCoordinator.RefreshAsync(
            resolution.RootPath,
            new[] { normalized.Profile },
            projectRegistryFactory: _projectRegistryFactory,
            hubSettingsStoreFactory: _hubSettingsStoreFactory,
            workspaceAutomationService: _workspaceAutomationService,
            cancellationToken);
        return OperationResult.Ok("Skill install record saved.", GetInstallsPath(resolution.RootPath));
    }

    public async Task<OperationResult> DeleteInstallAsync(
        string profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill install records could not be deleted.", string.Join(Environment.NewLine, resolution.Errors));
        }

        EnsureSourceLayoutMigrated(resolution.RootPath);
        var normalizedRelativePath = NormalizePath(relativePath);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var installs = (await LoadInstallsAsync(resolution.RootPath, cancellationToken)).ToList();
        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profileId, normalizedRelativePath);

        var removedInstalls = installs.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        var removedStates = states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (removedInstalls == 0 && removedStates == 0)
        {
            return OperationResult.Fail("The selected skill install record does not exist.", normalizedRelativePath);
        }

        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
        await RuntimeRefreshCoordinator.RefreshAsync(
            resolution.RootPath,
            new[] { profileId },
            projectRegistryFactory: _projectRegistryFactory,
            hubSettingsStoreFactory: _hubSettingsStoreFactory,
            workspaceAutomationService: _workspaceAutomationService,
            cancellationToken);
        return OperationResult.Ok("Skill install record deleted.", GetInstallsPath(resolution.RootPath));
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
        EnsureSourceLayoutMigrated(resolution.RootPath);
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
        var libraryDirectory = GetInstalledSkillDirectory(resolution.RootPath, LibraryProfileId, normalizedRelativePath);
        var existingProfiles = GetExistingSkillProfiles(resolution.RootPath, installs, states, normalizedRelativePath);
        var impactedProfiles = existingProfiles
            .Concat(normalizedTargets)
            .Append(normalizedSourceProfile)
            .Append(LibraryProfileId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var publishSourceDirectory = Directory.Exists(sourceDirectory)
            ? sourceDirectory
            : libraryDirectory;
        if (normalizedTargets.Count > 0 && !Directory.Exists(publishSourceDirectory))
        {
            return OperationResult.Fail("The selected skill source directory does not exist.", sourceDirectory);
        }
        var moveToLibrary = normalizedTargets.Count == 0 && !IsLibraryProfile(normalizedSourceProfile);
        if (moveToLibrary
            && !string.Equals(publishSourceDirectory, libraryDirectory, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(publishSourceDirectory))
        {
            if (Directory.Exists(libraryDirectory))
            {
                DeleteDirectory(libraryDirectory);
            }

            CopyDirectory(publishSourceDirectory, libraryDirectory);
            publishSourceDirectory = libraryDirectory;
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
            if (IsLibraryProfile(profile))
            {
                continue;
            }

            if (isSelected)
            {
                if (!string.Equals(profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceDirectoryWithSource(publishSourceDirectory, destinationDirectory);
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
            else if (string.Equals(profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase))
            {
                if (moveToLibrary)
                {
                    installs.RemoveAll(item => IsSkillInstall(item, profile, normalizedRelativePath));
                    states.RemoveAll(item => IsSkillState(item, profile, normalizedRelativePath));
                    if (Directory.Exists(destinationDirectory))
                    {
                        DeleteDirectory(destinationDirectory);
                    }
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
        if (moveToLibrary)
        {
            installs.RemoveAll(item => IsSkillInstall(item, LibraryProfileId, normalizedRelativePath));
            states.RemoveAll(item => IsSkillState(item, LibraryProfileId, normalizedRelativePath));
            if (sourceInstall is not null)
            {
                installs.Add(sourceInstall with { Profile = LibraryProfileId });
            }
            else
            {
                installs.Add(new SkillInstallRecord
                {
                    Name = Path.GetFileName(normalizedRelativePath),
                    Profile = LibraryProfileId,
                    InstalledRelativePath = normalizedRelativePath,
                    CustomizationMode = SkillCustomizationMode.Local
                });
            }

            if (sourceState is not null)
            {
                states.Add(sourceState with { Profile = LibraryProfileId });
            }
            else
            {
                states.Add(CreateStateRecord(LibraryProfileId, normalizedRelativePath, libraryDirectory));
            }
        }

        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
        await RuntimeRefreshCoordinator.RefreshAsync(
            resolution.RootPath,
            impactedProfiles.Where(profile => !IsLibraryProfile(profile)),
            projectRegistryFactory: _projectRegistryFactory,
            hubSettingsStoreFactory: _hubSettingsStoreFactory,
            workspaceAutomationService: _workspaceAutomationService,
            cancellationToken);
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
        EnsureSourceLayoutMigrated(resolution.RootPath);
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
        var libraryGroupDirectory = GetInstalledSkillDirectory(resolution.RootPath, LibraryProfileId, normalizedGroupPath);
        var existingProfiles = GetExistingGroupProfiles(resolution.RootPath, installs, states, normalizedGroupPath);
        var impactedProfiles = existingProfiles
            .Concat(normalizedTargets)
            .Append(normalizedSourceProfile)
            .Append(LibraryProfileId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var publishSourceDirectory = Directory.Exists(sourceGroupDirectory)
            ? sourceGroupDirectory
            : libraryGroupDirectory;
        if (normalizedTargets.Count > 0 && !Directory.Exists(publishSourceDirectory))
        {
            return OperationResult.Fail("The selected skill repository or folder does not exist.", sourceGroupDirectory);
        }
        var moveToLibrary = normalizedTargets.Count == 0 && !IsLibraryProfile(normalizedSourceProfile);
        if (moveToLibrary
            && !string.Equals(publishSourceDirectory, libraryGroupDirectory, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(publishSourceDirectory))
        {
            if (Directory.Exists(libraryGroupDirectory))
            {
                DeleteDirectory(libraryGroupDirectory);
            }

            CopyDirectory(publishSourceDirectory, libraryGroupDirectory);
            publishSourceDirectory = libraryGroupDirectory;
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
            if (IsLibraryProfile(profile))
            {
                continue;
            }

            if (isSelected)
            {
                if (!string.Equals(profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(destinationDirectory))
                    {
                        DeleteDirectory(destinationDirectory);
                    }
                    CopyDirectory(publishSourceDirectory, destinationDirectory);
                }
                installs.RemoveAll(item => string.Equals(item.Profile, profile, StringComparison.OrdinalIgnoreCase)
                                           && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
                installs.AddRange(sourceInstalls.Select(item => item with { Profile = profile }));
                states.RemoveAll(item => string.Equals(item.Profile, profile, StringComparison.OrdinalIgnoreCase)
                                         && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
                states.AddRange(sourceStates.Select(item => item with { Profile = profile }));
            }
            else if (string.Equals(profile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase))
            {
                if (moveToLibrary)
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
        if (moveToLibrary)
        {
            installs.RemoveAll(item => string.Equals(item.Profile, LibraryProfileId, StringComparison.OrdinalIgnoreCase)
                                       && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
            states.RemoveAll(item => string.Equals(item.Profile, LibraryProfileId, StringComparison.OrdinalIgnoreCase)
                                     && IsPathWithinScope(item.InstalledRelativePath, normalizedGroupPath));
            installs.AddRange(sourceInstalls.Select(item => item with { Profile = LibraryProfileId }));
            states.AddRange(sourceStates.Select(item => item with { Profile = LibraryProfileId }));
        }

        await SaveInstallsAsync(resolution.RootPath, installs, cancellationToken);
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);
        await RuntimeRefreshCoordinator.RefreshAsync(
            resolution.RootPath,
            impactedProfiles.Where(profile => !IsLibraryProfile(profile)),
            projectRegistryFactory: _projectRegistryFactory,
            hubSettingsStoreFactory: _hubSettingsStoreFactory,
            workspaceAutomationService: _workspaceAutomationService,
            cancellationToken);
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
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill baselines could not be captured.", string.Join(Environment.NewLine, resolution.Errors));
        }

        EnsureSourceLayoutMigrated(resolution.RootPath);
        var normalizedRelativePath = NormalizePath(relativePath);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var installs = await LoadInstallsAsync(resolution.RootPath, cancellationToken);
        if (!installs.Any(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == GetInstallKey(profileId, normalizedRelativePath)))
        {
            return OperationResult.Fail("Save the skill install record before capturing a baseline.", normalizedRelativePath);
        }

        var skillDirectory = GetInstalledSkillDirectory(resolution.RootPath, profileId, normalizedRelativePath);
        if (!Directory.Exists(skillDirectory))
        {
            return OperationResult.Fail("The target skill directory does not exist.", skillDirectory);
        }

        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profileId, normalizedRelativePath);
        states.RemoveAll(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        states.Add(CreateStateRecord(profileId, normalizedRelativePath, skillDirectory));
        await SaveStatesAsync(resolution.RootPath, states, cancellationToken);

        return OperationResult.Ok("Skill baseline rebuilt.", GetStatesPath(resolution.RootPath));
    }

    public async Task<OperationResult> ScanSourceAsync(
        string localName,
        string profile,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub hub root is invalid. Skill sources could not be scanned.", string.Join(Environment.NewLine, resolution.Errors));
        }

        EnsureSourceLayoutMigrated(resolution.RootPath);
        var sources = await LoadSourcesAsync(resolution.RootPath, cancellationToken);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var source = sources.FirstOrDefault(item => MatchesSource(item, localName, profileId));
        if (source is null)
        {
            return OperationResult.Fail("The selected skill source does not exist.", localName);
        }

        if (!source.IsEnabled)
        {
            return OperationResult.Fail("The selected skill source is disabled.", source.SourceDisplayName);
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
                    "The skill source was scanned, but no skills were discovered.",
                    BuildSourceDetails(source, resolvedSource, Array.Empty<string>()));
            }

            var detailLines = discoveredSkills
                .Select(item => $"- {item.RelativePath} ({item.Name})")
                .ToArray();

            return OperationResult.Ok(
                "Skill source scan completed.",
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
            return OperationResult.Ok("The installed skill already matches the current source baseline.", BuildUpdateDetails(context, hasUpdate: false, blockedReason: null));
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
        await RuntimeRefreshCoordinator.RefreshAsync(
            context.HubRoot,
            new[] { context.Install.Profile },
            projectRegistryFactory: _projectRegistryFactory,
            hubSettingsStoreFactory: _hubSettingsStoreFactory,
            workspaceAutomationService: _workspaceAutomationService,
            cancellationToken);

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
            return OperationResult.Fail("AI-Hub hub root is invalid. Skills could not be rolled back.", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var installDirectory = GetInstalledSkillDirectory(resolution.RootPath, profileId, normalizedRelativePath);
        if (!Directory.Exists(installDirectory))
        {
            return OperationResult.Fail("The target skill directory does not exist.", installDirectory);
        }

        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profileId, normalizedRelativePath);
        var state = states.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (state is null)
        {
            return OperationResult.Fail("No baseline record exists for the selected skill.", normalizedRelativePath);
        }

        var backupPath = ResolveRollbackBackupPath(resolution.RootPath, state, profileId, normalizedRelativePath);
        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
        {
            return OperationResult.Fail("No rollback backup is available.", normalizedRelativePath);
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
        await RuntimeRefreshCoordinator.RefreshAsync(
            resolution.RootPath,
            new[] { profileId },
            projectRegistryFactory: _projectRegistryFactory,
            hubSettingsStoreFactory: _hubSettingsStoreFactory,
            workspaceAutomationService: _workspaceAutomationService,
            cancellationToken);

        var detailBuilder = new StringBuilder();
        detailBuilder.AppendLine("The skill was restored from a rollback backup.");
        detailBuilder.AppendLine("Rollback backup: " + backupPath);
        detailBuilder.AppendLine("Pre-rollback snapshot: " + currentSnapshotBackupPath);
        detailBuilder.AppendLine("Installed directory: " + installDirectory);

        return OperationResult.Ok("Skill rolled back to the latest backup.", detailBuilder.ToString().TrimEnd());
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

        foreach (var (profile, skillsRoot) in EnumerateSkillRoots(hubRoot))
        {
            if (!Directory.Exists(skillsRoot))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(skillsRoot, "SKILL.md", SearchOption.AllDirectories))
            {
                var skillDirectory = Path.GetDirectoryName(manifestPath);
                if (string.IsNullOrWhiteSpace(skillDirectory))
                {
                    continue;
                }

                var relativePath = NormalizePath(Path.GetRelativePath(skillsRoot, skillDirectory));
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
                    ProfileDisplayName = IsLibraryProfile(profile) ? LibraryProfileDisplayName : WorkspaceProfiles.ToDisplayName(profile),
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
            .GroupBy(skill => skill.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(AggregateInstalledSkill)
            .OrderBy(skill => GetInstalledSkillSortOrder(skill))
            .ThenBy(skill => skill.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static InstalledSkillRecord AggregateInstalledSkill(IGrouping<string, InstalledSkillRecord> group)
    {
        var records = group.ToArray();
        var primary = records
            .OrderBy(skill => IsLibraryProfile(skill.Profile) ? 1 : 0)
            .ThenBy(skill => GetProfileSortOrder(skill.Profile))
            .ThenBy(skill => skill.Profile, StringComparer.OrdinalIgnoreCase)
            .First();

        var bindingProfileIds = records
            .Where(skill => !IsLibraryProfile(skill.Profile))
            .Select(skill => WorkspaceProfiles.NormalizeId(skill.Profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetProfileSortOrder)
            .ThenBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var bindingDisplayTags = bindingProfileIds.Length == 0
            ? new[] { "未绑定" }
            : bindingProfileIds.Select(WorkspaceProfiles.ToDisplayName).ToArray();

        return primary with
        {
            BindingProfileIds = bindingProfileIds,
            BindingDisplayTags = bindingDisplayTags
        };
    }

    private static int GetInstalledSkillSortOrder(InstalledSkillRecord skill)
    {
        var sortProfile = skill.BindingProfileIds.FirstOrDefault();
        return GetProfileSortOrder(string.IsNullOrWhiteSpace(sortProfile) ? skill.Profile : sortProfile);
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
            return "Not registered yet.";
        }

        if (sourceMissing && mode != SkillCustomizationMode.Local)
        {
            return "Registered, but the bound source record is missing.";
        }

        if (!hasBaseline)
        {
            return "Baseline has not been captured yet.";
        }

        if (isDirty)
        {
            return mode switch
            {
                SkillCustomizationMode.Managed => "Managed mode has new local changes.",
                SkillCustomizationMode.Overlay => "Overlay mode has new local changes.",
                SkillCustomizationMode.Fork => "Fork mode has new local changes.",
                SkillCustomizationMode.Local => "Local mode has new local changes.",
                _ => "Local modifications detected."
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
            var workingRootPath = NormalizeExistingDirectory(source.Location, "The local skill source directory does not exist.");
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
        builder.AppendLine("Source: " + source.SourceDisplayName);
        builder.AppendLine("Kind: " + source.KindDisplay);
        builder.AppendLine("Working root: " + resolvedSource.WorkingRootPath);
        builder.AppendLine("Catalog root: " + resolvedSource.CatalogRootPath);
        builder.AppendLine("Resolved reference: " + resolvedSource.ResolvedReference);

        if (detailLines.Count == 0)
        {
            builder.AppendLine("No skills were discovered.");
        }
        else
        {
            builder.AppendLine("Discovered skills:");
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
            builder.AppendLine("Last applied reference: " + context.State.LastAppliedReference);
        }

        if (context.State.LastSyncAt.HasValue)
        {
            builder.AppendLine("Last synced at: " + context.State.LastSyncAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (!string.IsNullOrWhiteSpace(context.State.LastBackupPath))
        {
            builder.AppendLine("Last backup path: " + context.State.LastBackupPath);
        }

        if (!string.IsNullOrWhiteSpace(blockedReason))
        {
            builder.AppendLine("Blocked reason: " + blockedReason);
        }

        return builder.ToString().TrimEnd();
    }

    private static string? GetSyncBlockedReason(SkillInstallContext context, bool force)
    {
        if (context.Install.CustomizationMode == SkillCustomizationMode.Local)
        {
            return "Local mode skills do not participate in upstream sync.";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Overlay && context.State.BaselineFiles.Count == 0)
        {
            return "Overlay mode requires a baseline before local files can be safely reapplied.";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Fork && !force)
        {
            return "Fork mode detected local changes. Review whether to keep them before forcing sync.";
        }

        if (context.Install.CustomizationMode == SkillCustomizationMode.Overlay)
        {
            return null;
        }

        if (context.IsDirty && !force)
        {
            return "Local changes were detected. Rebuild the baseline, change the mode, or use force sync if you intend to overwrite them.";
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
                failureMessage: "Failed to clone the Git source repository.",
                cancellationToken);
        }
        else if (refreshRemote)
        {
            await EnsureProcessSuccessAsync(
                "git",
                ["-C", cacheDirectory, "fetch", "--all", "--tags", "--prune"],
                workingDirectory: null,
                failureMessage: "Failed to fetch updates from the Git source repository.",
                cancellationToken);
        }

        var reference = string.IsNullOrWhiteSpace(source.Reference) ? "main" : source.Reference;
        await EnsureProcessSuccessAsync(
            "git",
            ["-C", cacheDirectory, "checkout", "--force", reference],
            workingDirectory: null,
            failureMessage: "Failed to check out the requested Git reference.",
            cancellationToken);

        var remoteReference = "origin/" + reference;
        var remoteReferenceExists = await DoesGitReferenceExistAsync(cacheDirectory, remoteReference, cancellationToken);
        if (remoteReferenceExists)
        {
            await EnsureProcessSuccessAsync(
                "git",
                ["-C", cacheDirectory, "reset", "--hard", remoteReference],
                workingDirectory: null,
                failureMessage: "Failed to reset the cached Git repository to the remote reference.",
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
            throw new InvalidOperationException("The skill catalog root directory does not exist: " + catalogRootPath);
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

        throw new InvalidOperationException("The installed skill path could not be resolved to a unique source skill directory.");
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

    private async Task UpsertStateAsync(
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

    private async Task<IReadOnlyList<SkillSourceRecord>> LoadSourcesAsync(string hubRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSourceLayoutMigrated(hubRoot);

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

    private async Task SaveSourcesAsync(
        string hubRoot,
        IReadOnlyList<SkillSourceRecord> sources,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSourceLayoutMigrated(hubRoot);

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

    private async Task<IReadOnlyList<SkillInstallRecord>> LoadInstallsAsync(string hubRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSourceLayoutMigrated(hubRoot);

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

    private async Task SaveInstallsAsync(
        string hubRoot,
        IReadOnlyList<SkillInstallRecord> installs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSourceLayoutMigrated(hubRoot);

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

    private async Task<IReadOnlyList<SkillInstallStateRecord>> LoadStatesAsync(string hubRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSourceLayoutMigrated(hubRoot);

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

    private async Task SaveStatesAsync(
        string hubRoot,
        IReadOnlyList<SkillInstallStateRecord> states,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSourceLayoutMigrated(hubRoot);

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
            return "Skill source name cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(record.Location))
        {
            return "Skill source location cannot be empty.";
        }

        return null;
    }

    private static string? ValidateInstall(SkillInstallRecord record, IReadOnlyList<SkillSourceRecord> sources)
    {
        if (string.IsNullOrWhiteSpace(record.InstalledRelativePath))
        {
            return "The skill install path cannot be empty.";
        }

        if (record.CustomizationMode == SkillCustomizationMode.Local)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(record.SourceLocalName) || string.IsNullOrWhiteSpace(record.SourceProfile))
        {
            return "Managed, overlay, and fork modes all require a bound source.";
        }

        if (!sources.Any(source => MatchesSource(source, record.SourceLocalName, record.SourceProfile)))
        {
            return "The selected source does not exist. Save the source list first.";
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
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        var normalizedRelativePath = NormalizePath(relativePath).Replace('/', Path.DirectorySeparatorChar);
        var sourceRoot = GetCompanySourceRoot(hubRoot);

        if (IsLibraryProfile(normalizedProfile))
        {
            return Path.Combine(SourcePathLayout.GetSkillsLibraryRoot(sourceRoot), normalizedRelativePath);
        }

        return Path.Combine(SourcePathLayout.GetProfileSkillsRoot(sourceRoot, normalizedProfile), normalizedRelativePath);
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
            .Where(profile => !IsLibraryProfile(profile))
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

        foreach (var (profile, skillsRoot) in EnumerateSkillRoots(hubRoot))
        {
            if (!Directory.Exists(skillsRoot))
            {
                continue;
            }

            var directory = GetInstalledSkillDirectory(hubRoot, profile, normalizedRelativePath);
            if (Directory.Exists(directory))
            {
                profiles.Add(profile);
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

        foreach (var (profile, skillsRoot) in EnumerateSkillRoots(hubRoot))
        {
            if (!Directory.Exists(skillsRoot))
            {
                continue;
            }

            var directory = GetInstalledSkillDirectory(hubRoot, profile, normalizedGroupPath);
            if (Directory.Exists(directory))
            {
                profiles.Add(profile);
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

    private static bool IsLibraryProfile(string? profile)
    {
        return string.Equals(WorkspaceProfiles.NormalizeId(profile), LibraryProfileId, StringComparison.OrdinalIgnoreCase);
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
        var sourceRoot = GetCompanySourceRoot(hubRoot);
        return SourcePathLayout.GetSkillSourcesPath(sourceRoot);
    }

    private static string GetInstallsPath(string hubRoot)
    {
        var sourceRoot = GetCompanySourceRoot(hubRoot);
        return SourcePathLayout.GetSkillInstallsPath(sourceRoot);
    }

    private static string GetStatesPath(string hubRoot)
    {
        var sourceRoot = GetCompanySourceRoot(hubRoot);
        return SourcePathLayout.GetSkillStatesPath(sourceRoot);
    }

    private void EnsureSourceLayoutMigrated(string hubRoot)
    {
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var sourceRoot = GetCompanySourceRoot(normalizedHubRoot);
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(SourcePathLayout.GetSkillsLibraryRoot(sourceRoot));
        Directory.CreateDirectory(SourcePathLayout.GetMcpDraftsRoot(sourceRoot));
        Directory.CreateDirectory(SourcePathLayout.GetRegistryRoot(sourceRoot));

        foreach (var profile in GetLegacyProfiles(normalizedHubRoot))
        {
            Directory.CreateDirectory(SourcePathLayout.GetProfileSkillsRoot(sourceRoot, profile));
            Directory.CreateDirectory(SourcePathLayout.GetProfileCommandsRoot(sourceRoot, profile));
            Directory.CreateDirectory(SourcePathLayout.GetProfileAgentsRoot(sourceRoot, profile));
            Directory.CreateDirectory(Path.GetDirectoryName(SourcePathLayout.GetProfileSettingsPath(sourceRoot, profile))!);
            Directory.CreateDirectory(Path.GetDirectoryName(SourcePathLayout.GetProfileManifestPath(sourceRoot, profile))!);

            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(normalizedHubRoot, "skills", profile),
                SourcePathLayout.GetProfileSkillsRoot(sourceRoot, profile));
            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(normalizedHubRoot, "claude", "commands", profile),
                SourcePathLayout.GetProfileCommandsRoot(sourceRoot, profile));
            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(normalizedHubRoot, "agents", profile),
                SourcePathLayout.GetProfileAgentsRoot(sourceRoot, profile));
            CopyDirectoryContentsIfTargetMissing(
                Path.Combine(normalizedHubRoot, "claude", "agents", profile),
                SourcePathLayout.GetProfileAgentsRoot(sourceRoot, profile));
            CopyFileIfTargetMissing(
                Path.Combine(normalizedHubRoot, "claude", "settings", profile + ".settings.json"),
                SourcePathLayout.GetProfileSettingsPath(sourceRoot, profile));
            CopyFileIfTargetMissing(
                Path.Combine(normalizedHubRoot, "mcp", "manifest", profile + ".json"),
                SourcePathLayout.GetProfileManifestPath(sourceRoot, profile));
        }

        CopyFileIfTargetMissing(
            Path.Combine(normalizedHubRoot, "skills", "sources.json"),
            SourcePathLayout.GetSkillSourcesPath(sourceRoot));
        CopyFileIfTargetMissing(
            Path.Combine(normalizedHubRoot, "config", "skills-installs.json"),
            SourcePathLayout.GetSkillInstallsPath(sourceRoot));
        CopyFileIfTargetMissing(
            Path.Combine(normalizedHubRoot, "config", "skills-state.json"),
            SourcePathLayout.GetSkillStatesPath(sourceRoot));
        CopyFileIfTargetMissing(
            Path.Combine(normalizedHubRoot, "config", "profile-catalog.json"),
            SourcePathLayout.GetProfileCatalogPath(sourceRoot));
    }

    private static IEnumerable<string> GetLegacyProfiles(string hubRoot)
    {
        var profiles = new HashSet<string>(WorkspaceProfiles.CreateDefaultCatalog().Select(profile => WorkspaceProfiles.NormalizeId(profile.Id)), StringComparer.OrdinalIgnoreCase);
        AddProfilesFromRoot(Path.Combine(hubRoot, "skills"), profiles);
        AddProfilesFromRoot(Path.Combine(hubRoot, "claude", "commands"), profiles);
        AddProfilesFromRoot(Path.Combine(hubRoot, "agents"), profiles);
        AddProfilesFromRoot(Path.Combine(hubRoot, "claude", "agents"), profiles);
        AddProfilesFromRoot(Path.Combine(hubRoot, "claude", "settings"), profiles, trimSettingsSuffix: true);
        AddProfilesFromRoot(Path.Combine(hubRoot, "mcp", "manifest"), profiles, trimJsonSuffix: true);

        return profiles
            .OrderBy(GetProfileSortOrder)
            .ThenBy(profile => profile, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddProfilesFromRoot(string rootPath, ISet<string> profiles, bool trimSettingsSuffix = false, bool trimJsonSuffix = false)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(rootPath))
        {
            var profile = Path.GetFileName(directoryPath);
            if (trimSettingsSuffix && profile.EndsWith(".settings", StringComparison.OrdinalIgnoreCase))
            {
                profile = profile[..^".settings".Length];
            }

            if (trimJsonSuffix && profile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                profile = profile[..^".json".Length];
            }

            profiles.Add(WorkspaceProfiles.NormalizeId(profile));
        }

        if (trimSettingsSuffix)
        {
            foreach (var filePath in Directory.EnumerateFiles(rootPath, "*.settings.json", SearchOption.TopDirectoryOnly))
            {
                var profile = Path.GetFileNameWithoutExtension(filePath);
                if (profile.EndsWith(".settings", StringComparison.OrdinalIgnoreCase))
                {
                    profile = profile[..^".settings".Length];
                }

                profiles.Add(WorkspaceProfiles.NormalizeId(profile));
            }
        }

        if (trimJsonSuffix)
        {
            foreach (var filePath in Directory.EnumerateFiles(rootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                var profile = Path.GetFileNameWithoutExtension(filePath);
                profiles.Add(WorkspaceProfiles.NormalizeId(profile));
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetProfilesUsingSourceAsync(
        string hubRoot,
        string sourceLocalName,
        string sourceProfile,
        CancellationToken cancellationToken)
    {
        var normalizedSourceProfile = WorkspaceProfiles.NormalizeId(sourceProfile);
        var installs = await LoadInstallsAsync(hubRoot, cancellationToken);
        return installs
            .Where(item => string.Equals(item.SourceLocalName, sourceLocalName, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(item.SourceProfile, normalizedSourceProfile, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Profile)
            .Append(normalizedSourceProfile)
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task RefreshRuntimeAsync(string hubRoot, IEnumerable<string> affectedProfiles, CancellationToken cancellationToken)
    {
        var profiles = affectedProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(WorkspaceProfiles.NormalizeId)
            .Where(profile => !IsLibraryProfile(profile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (profiles.Length == 0)
        {
            return;
        }

        await RuntimeRefreshCoordinator.RefreshAsync(
            hubRoot,
            profiles,
            projectRegistryFactory: _projectRegistryFactory,
            hubSettingsStoreFactory: _hubSettingsStoreFactory,
            workspaceAutomationService: _workspaceAutomationService,
            cancellationToken);
    }

    private static string GetCompanySourceRoot(string hubRoot)
    {
        return SourcePathLayout.GetCompanySourceRoot(Path.GetFullPath(hubRoot));
    }

    private static IEnumerable<(string Profile, string SkillsRoot)> EnumerateSkillRoots(string hubRoot)
    {
        var sourceRoot = GetCompanySourceRoot(hubRoot);
        yield return (LibraryProfileId, SourcePathLayout.GetSkillsLibraryRoot(sourceRoot));

        var profilesRoot = Path.Combine(sourceRoot, "profiles");
        if (!Directory.Exists(profilesRoot))
        {
            yield break;
        }

        foreach (var profileRoot in Directory.EnumerateDirectories(profilesRoot)
                     .OrderBy(path => GetProfileSortOrder(Path.GetFileName(path)))
                     .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var profile = WorkspaceProfiles.NormalizeId(Path.GetFileName(profileRoot));
            yield return (profile, SourcePathLayout.GetProfileSkillsRoot(sourceRoot, profile));
        }
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

    private static void CopyFileIfTargetMissing(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(targetPath))
        {
            return;
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.Copy(sourcePath, targetPath, overwrite: false);
    }

    private static void CopyDirectoryContentsIfTargetMissing(string sourceRoot, string targetRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        if (Directory.Exists(targetRoot)
            && (Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories).Any()
                || Directory.EnumerateDirectories(targetRoot, "*", SearchOption.AllDirectories).Any()))
        {
            return;
        }

        CopyDirectory(sourceRoot, targetRoot);
    }

}

