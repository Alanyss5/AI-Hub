using System.Text.Json;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    private async Task RunAutomaticMaintenanceIfEnabledAsync(string hubRoot, CancellationToken cancellationToken)
    {
        if (_hubSettingsStoreFactory is null)
        {
            return;
        }

        lock (_automaticMaintenanceGate)
        {
            if (_automaticMaintenanceCompletedRoots.Contains(hubRoot))
            {
                return;
            }

            _automaticMaintenanceCompletedRoots.Add(hubRoot);
        }

        var settings = await _hubSettingsStoreFactory(hubRoot).LoadAsync(cancellationToken);
        if (!settings.AutoCheckSkillUpdatesOnLoad && !settings.AutoSyncSafeSkillsOnLoad)
        {
            return;
        }

        var installs = await LoadInstallsAsync(hubRoot, cancellationToken);
        if (installs.Count == 0)
        {
            return;
        }

        var sources = await LoadSourcesAsync(hubRoot, cancellationToken);
        foreach (var install in installs
                     .OrderBy(item => GetProfileSortOrder(item.Profile))
                     .ThenBy(item => item.Profile, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.InstalledRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (install.CustomizationMode is SkillCustomizationMode.Local or SkillCustomizationMode.Fork)
            {
                continue;
            }

            var source = sources.FirstOrDefault(item => MatchesSource(item, install.SourceLocalName, install.SourceProfile));
            if (source is null || !source.IsEnabled || !source.AutoUpdate)
            {
                continue;
            }

            _ = await CheckForUpdatesAsync(install.Profile, install.InstalledRelativePath, cancellationToken);
            if (settings.AutoSyncSafeSkillsOnLoad)
            {
                _ = await SyncInstalledSkillAsync(install.Profile, install.InstalledRelativePath, force: false, cancellationToken);
            }
        }
    }

    private async Task UpdateScannedSourceAsync(
        string hubRoot,
        SkillSourceRecord source,
        ResolvedSkillSource resolvedSource,
        IReadOnlyList<DiscoveredSkill> discoveredSkills,
        IReadOnlyList<string> availableReferences,
        CancellationToken cancellationToken)
    {
        var sources = (await LoadSourcesAsync(hubRoot, cancellationToken)).ToList();
        var updatedSource = await RefreshSourceVersionMetadataAsync(source, refreshRemote: false, cancellationToken);
        updatedSource = updatedSource with
        {
            LastScannedAt = DateTimeOffset.Now,
            LastScanReference = resolvedSource.ResolvedReference,
            LastDiscoveredSkills = discoveredSkills.Select(item => item.RelativePath).ToArray(),
            AvailableReferences = availableReferences.ToArray()
        };

        sources.RemoveAll(item => MatchesSource(item, source.LocalName, source.Profile));
        sources.Add(updatedSource);
        await SaveSourcesAsync(hubRoot, sources, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetAvailableReferencesAsync(
        ResolvedSkillSource resolvedSource,
        SkillSourceRecord source,
        CancellationToken cancellationToken)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(source.Reference))
        {
            values.Add(source.Reference);
        }

        if (!string.IsNullOrWhiteSpace(resolvedSource.ResolvedReference))
        {
            values.Add(resolvedSource.ResolvedReference);
        }

        if (source.Kind != SkillSourceKind.GitRepository)
        {
            return values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        try
        {
            var branchResult = await RunProcessAsync(
                "git",
                ["branch", "--format=%(refname:short)"],
                resolvedSource.WorkingRootPath,
                cancellationToken);

            foreach (var line in SplitLines(branchResult.StandardOutput))
            {
                values.Add(line.TrimStart('*').Trim());
            }

            var tagResult = await RunProcessAsync(
                "git",
                ["tag", "--list"],
                resolvedSource.WorkingRootPath,
                cancellationToken);

            foreach (var line in SplitLines(tagResult.StandardOutput))
            {
                values.Add(line.Trim());
            }
        }
        catch
        {
            // 忽略来源引用枚举失败，不影响主流程。
        }

        return values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private static string BuildLastSyncDisplay(SkillInstallStateRecord? state)
    {
        return state?.LastSyncAt.HasValue == true
            ? "最近同步：" + state.LastSyncAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "尚未同步来源";
    }

    private static string BuildBackupSummary(string[] backups)
    {
        return backups.Length == 0
            ? "尚无备份"
            : "备份数量：" + backups.Length + " / 最近：" + Path.GetFileName(backups[0]);
    }

    private static string BuildRecentBackupsDisplay(string[] backups)
    {
        return backups.Length == 0
            ? "尚无备份历史"
            : string.Join(Environment.NewLine, backups.Select(path => Path.GetFileName(path) + " -> " + path));
    }

    private static string[] GetRecentBackups(string hubRoot, string profile, string relativePath, int maxCount = 5)
    {
        var backupRoot = GetBackupRoot(hubRoot, profile, relativePath);
        if (!Directory.Exists(backupRoot))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(backupRoot)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();
    }

    private static OverlaySnapshot ResolveOverlaySnapshotForSync(SkillInstallContext context)
    {
        if (context.Install.CustomizationMode != SkillCustomizationMode.Overlay)
        {
            return OverlaySnapshot.Empty;
        }

        if (context.IsDirty)
        {
            return CreateOverlaySnapshot(context);
        }

        return LoadOverlaySnapshot(context.HubRoot, context.Install.Profile, context.Install.InstalledRelativePath);
    }

    private static OverlaySnapshot CreateOverlaySnapshot(SkillInstallContext context)
    {
        var overlayRoot = GetOverlayRoot(context.HubRoot, context.Install.Profile, context.Install.InstalledRelativePath);
        var overlayFilesRoot = Path.Combine(overlayRoot, "files");
        var overlayDeletedFile = GetOverlayDeletedFilePath(context.HubRoot, context.Install.Profile, context.Install.InstalledRelativePath);

        if (Directory.Exists(overlayRoot))
        {
            Directory.Delete(overlayRoot, recursive: true);
        }

        var baselineMap = context.State.BaselineFiles.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var installedMap = context.InstalledFingerprints.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var changedFiles = installedMap.Keys
            .Where(key => !baselineMap.TryGetValue(key, out var baseline) || !string.Equals(baseline.Sha256, installedMap[key].Sha256, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var deletedFiles = baselineMap.Keys
            .Except(installedMap.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var relativePath in changedFiles)
        {
            var sourcePath = Path.Combine(context.InstalledSkillDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var targetPath = Path.Combine(overlayFilesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
        }

        if (deletedFiles.Length > 0)
        {
            Directory.CreateDirectory(overlayRoot);
            File.WriteAllText(overlayDeletedFile, JsonSerializer.Serialize(deletedFiles, SerializerOptions));
        }

        if (changedFiles.Length == 0 && deletedFiles.Length == 0)
        {
            if (Directory.Exists(overlayRoot))
            {
                Directory.Delete(overlayRoot, recursive: true);
            }

            return OverlaySnapshot.Empty;
        }

        return new OverlaySnapshot(overlayRoot, overlayFilesRoot, deletedFiles, changedFiles.Length);
    }

    private static OverlaySnapshot LoadOverlaySnapshot(string hubRoot, string profile, string relativePath)
    {
        var overlayRoot = GetOverlayRoot(hubRoot, profile, relativePath);
        if (!Directory.Exists(overlayRoot))
        {
            return OverlaySnapshot.Empty;
        }

        var overlayFilesRoot = Path.Combine(overlayRoot, "files");
        var deletedFilePath = GetOverlayDeletedFilePath(hubRoot, profile, relativePath);
        string[] deletedFiles = [];
        if (File.Exists(deletedFilePath))
        {
            try
            {
                deletedFiles = JsonSerializer.Deserialize<string[]>(File.ReadAllText(deletedFilePath), SerializerOptions) ?? [];
            }
            catch
            {
                deletedFiles = [];
            }
        }

        var fileCount = Directory.Exists(overlayFilesRoot)
            ? Directory.EnumerateFiles(overlayFilesRoot, "*", SearchOption.AllDirectories).Count()
            : 0;

        if (fileCount == 0 && deletedFiles.Length == 0)
        {
            return OverlaySnapshot.Empty;
        }

        return new OverlaySnapshot(overlayRoot, overlayFilesRoot, deletedFiles, fileCount);
    }

    private static void ApplyOverlaySnapshot(SkillInstallContext context, OverlaySnapshot snapshot)
    {
        if (snapshot.IsEmpty)
        {
            return;
        }

        if (Directory.Exists(snapshot.FilesRoot))
        {
            CopyDirectoryRecursive(snapshot.FilesRoot, context.InstalledSkillDirectory, snapshot.FilesRoot);
        }

        foreach (var relativePath in snapshot.DeletedFiles)
        {
            var targetPath = Path.Combine(context.InstalledSkillDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(targetPath))
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
            }
            else if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }
        }
    }

    private static string GetOverlayRoot(string hubRoot, string profile, string relativePath)
    {
        return Path.Combine(
            hubRoot,
            "skills-overrides",
            WorkspaceProfiles.NormalizeId(profile),
            SanitizePathSegment(relativePath));
    }

    private static string GetOverlayDeletedFilePath(string hubRoot, string profile, string relativePath)
    {
        return Path.Combine(GetOverlayRoot(hubRoot, profile, relativePath), "deleted-files.json");
    }

    private static string[] ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private sealed record OverlaySnapshot(string RootPath, string FilesRoot, IReadOnlyList<string> DeletedFiles, int FileCount)
    {
        public static OverlaySnapshot Empty { get; } = new(string.Empty, string.Empty, [], 0);

        public bool IsEmpty => string.IsNullOrWhiteSpace(RootPath) || (FileCount == 0 && DeletedFiles.Count == 0);
    }
}
