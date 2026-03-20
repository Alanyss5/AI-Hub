using System.Text;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    public async Task<SkillMergePreview?> PreviewOverlayMergeAsync(
        string profile,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var contextResult = await TryCreateInstallContextAsync(profileId, relativePath, refreshRemote: true, cancellationToken);
        if (!contextResult.Success || contextResult.Context is null)
        {
            return null;
        }

        var context = contextResult.Context;
        if (context.Install.CustomizationMode != SkillCustomizationMode.Overlay || context.State.BaselineFiles.Count == 0)
        {
            return null;
        }

        return BuildOverlayMergePreview(context);
    }

    public async Task<OperationResult> ApplyOverlayMergeAsync(
        string profile,
        string relativePath,
        IReadOnlyList<SkillMergeDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        var profileId = WorkspaceProfiles.NormalizeId(profile);
        var contextResult = await TryCreateInstallContextAsync(profileId, relativePath, refreshRemote: true, cancellationToken);
        if (!contextResult.Success || contextResult.Context is null)
        {
            return contextResult.Result;
        }

        var context = contextResult.Context;
        if (context.Install.CustomizationMode != SkillCustomizationMode.Overlay)
        {
            return OperationResult.Fail("只有覆盖层模式支持细粒度合并。", context.Install.CustomizationMode.ToDisplayName());
        }

        if (context.State.BaselineFiles.Count == 0)
        {
            return OperationResult.Fail("覆盖层模式需要先建立基线，才能执行细粒度合并。", context.Install.InstalledRelativePath);
        }

        var preview = BuildOverlayMergePreview(context);
        if (!preview.HasChanges)
        {
            return OperationResult.Ok("当前没有需要合并的来源差异。", context.Install.InstalledRelativePath);
        }

        var backupPath = CreateBackupSnapshot(
            context.HubRoot,
            context.Install.Profile,
            context.Install.InstalledRelativePath,
            context.InstalledSkillDirectory,
            "pre-merge");

        var decisionMap = decisions.ToDictionary(
            item => NormalizePath(item.RelativePath),
            item => item.Decision,
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in preview.Files)
        {
            var decision = decisionMap.TryGetValue(NormalizePath(entry.RelativePath), out var selectedDecision)
                ? selectedDecision
                : entry.SuggestedDecision;

            ApplyMergeDecision(context, entry, decision);
        }

        var overlaySnapshot = RebuildOverlaySnapshotFromSource(
            context.HubRoot,
            context.Install.Profile,
            context.Install.InstalledRelativePath,
            context.SourceSkillDirectory,
            context.InstalledSkillDirectory);
        var updatedInstalledFingerprints = CaptureFingerprints(context.InstalledSkillDirectory);
        var states = (await LoadStatesAsync(context.HubRoot, cancellationToken)).ToList();
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

        await UpsertStateAsync(context.HubRoot, states, updatedState, cancellationToken);

        var detailBuilder = new StringBuilder();
        detailBuilder.AppendLine("已应用覆盖层细粒度合并。");
        detailBuilder.AppendLine("Skill：" + context.Install.Name);
        detailBuilder.AppendLine("安装目录：" + context.InstalledSkillDirectory);
        detailBuilder.AppendLine("来源：" + context.Source.SourceDisplayName);
        detailBuilder.AppendLine("来源引用：" + context.ResolvedSource.ResolvedReference);
        detailBuilder.AppendLine("合并前备份：" + backupPath);
        detailBuilder.AppendLine("覆盖层文件：" + overlaySnapshot.FileCount);
        detailBuilder.AppendLine("覆盖层删除：" + overlaySnapshot.DeletedFiles.Count);

        return OperationResult.Ok("覆盖层合并已应用。", detailBuilder.ToString().TrimEnd());
    }

    private static SkillMergePreview BuildOverlayMergePreview(SkillInstallContext context)
    {
        var baselineMap = context.State.BaselineFiles.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var sourceMap = context.SourceFingerprints.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var installedMap = context.InstalledFingerprints.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var entries = baselineMap.Keys
            .Union(sourceMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(installedMap.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(relativePath => BuildMergeEntry(relativePath, baselineMap, sourceMap, installedMap))
            .Where(entry => entry is not null)
            .Cast<SkillMergeFileEntry>()
            .ToArray();

        return new SkillMergePreview(
            context.Install.Profile,
            context.Install.InstalledRelativePath,
            context.Source.SourceDisplayName,
            context.ResolvedSource.ResolvedReference,
            entries);
    }

    private static SkillMergeFileEntry? BuildMergeEntry(
        string relativePath,
        IReadOnlyDictionary<string, SkillFileFingerprintRecord> baselineMap,
        IReadOnlyDictionary<string, SkillFileFingerprintRecord> sourceMap,
        IReadOnlyDictionary<string, SkillFileFingerprintRecord> installedMap)
    {
        baselineMap.TryGetValue(relativePath, out var baseline);
        sourceMap.TryGetValue(relativePath, out var source);
        installedMap.TryGetValue(relativePath, out var installed);

        if (FingerprintsMatch(source, installed))
        {
            return null;
        }

        var baselineMatchesSource = FingerprintsMatch(baseline, source);
        var baselineMatchesInstalled = FingerprintsMatch(baseline, installed);

        if (baseline is null && source is not null && installed is null)
        {
            return new SkillMergeFileEntry(
                relativePath,
                SkillMergeFileStatus.SourceOnly,
                SkillMergeDecisionMode.UseSource,
                "来源新增了该文件，本地尚未保留。",
                false,
                true,
                false);
        }

        if (baseline is null && source is null && installed is not null)
        {
            return new SkillMergeFileEntry(
                relativePath,
                SkillMergeFileStatus.LocalOnly,
                SkillMergeDecisionMode.KeepLocal,
                "仅本地新增了该文件。",
                false,
                false,
                true);
        }

        if (baselineMatchesInstalled)
        {
            return source is null
                ? new SkillMergeFileEntry(
                    relativePath,
                    SkillMergeFileStatus.SourceDeleted,
                    SkillMergeDecisionMode.ApplyDeletion,
                    "来源已删除，当前本地仍保持基线版本。",
                    baseline is not null,
                    false,
                    installed is not null)
                : new SkillMergeFileEntry(
                    relativePath,
                    SkillMergeFileStatus.SourceChanged,
                    SkillMergeDecisionMode.UseSource,
                    "来源已更新，当前本地仍保持基线版本。",
                    baseline is not null,
                    true,
                    installed is not null);
        }

        if (baselineMatchesSource)
        {
            return new SkillMergeFileEntry(
                relativePath,
                SkillMergeFileStatus.LocalOnly,
                SkillMergeDecisionMode.KeepLocal,
                "仅本地覆盖层修改了该文件。",
                baseline is not null,
                source is not null,
                installed is not null);
        }

        return new SkillMergeFileEntry(
            relativePath,
            SkillMergeFileStatus.Conflict,
            SkillMergeDecisionMode.Skip,
            "来源和本地覆盖层都修改了该文件，需要显式选择。",
            baseline is not null,
            source is not null,
            installed is not null);
    }

    private static bool FingerprintsMatch(SkillFileFingerprintRecord? left, SkillFileFingerprintRecord? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase)
            && left.Size == right.Size;
    }

    private static void ApplyMergeDecision(
        SkillInstallContext context,
        SkillMergeFileEntry entry,
        SkillMergeDecisionMode decision)
    {
        var installedFilePath = Path.Combine(
            context.InstalledSkillDirectory,
            entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var sourceFilePath = Path.Combine(
            context.SourceSkillDirectory,
            entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        switch (decision)
        {
            case SkillMergeDecisionMode.UseSource:
                if (File.Exists(sourceFilePath))
                {
                    var directory = Path.GetDirectoryName(installedFilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.Copy(sourceFilePath, installedFilePath, overwrite: true);
                }
                else
                {
                    DeleteInstalledPath(context.InstalledSkillDirectory, installedFilePath);
                }

                break;

            case SkillMergeDecisionMode.ApplyDeletion:
                DeleteInstalledPath(context.InstalledSkillDirectory, installedFilePath);
                break;

            case SkillMergeDecisionMode.KeepLocal:
            case SkillMergeDecisionMode.Skip:
            default:
                break;
        }
    }

    private static void DeleteInstalledPath(string installedRoot, string installedFilePath)
    {
        if (File.Exists(installedFilePath))
        {
            File.SetAttributes(installedFilePath, FileAttributes.Normal);
            File.Delete(installedFilePath);
        }
        else if (Directory.Exists(installedFilePath))
        {
            Directory.Delete(installedFilePath, recursive: true);
        }

        RemoveEmptyParentDirectories(installedRoot, Path.GetDirectoryName(installedFilePath));
    }

    private static void RemoveEmptyParentDirectories(string rootPath, string? directoryPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentDirectory = directoryPath;
        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            var normalizedDirectory = Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedDirectory, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                !normalizedDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                Directory.EnumerateFileSystemEntries(normalizedDirectory).Any())
            {
                return;
            }

            Directory.Delete(normalizedDirectory, recursive: false);
            currentDirectory = Path.GetDirectoryName(normalizedDirectory);
        }
    }

    private static OverlaySnapshot RebuildOverlaySnapshotFromSource(
        string hubRoot,
        string profile,
        string relativePath,
        string sourceDirectory,
        string installedDirectory)
    {
        var overlayRoot = GetOverlayRoot(hubRoot, profile, relativePath);
        var overlayFilesRoot = Path.Combine(overlayRoot, "files");
        var deletedFilePath = GetOverlayDeletedFilePath(hubRoot, profile, relativePath);

        if (Directory.Exists(overlayRoot))
        {
            Directory.Delete(overlayRoot, recursive: true);
        }

        var sourceMap = CaptureFingerprints(sourceDirectory).ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var installedMap = CaptureFingerprints(installedDirectory).ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var changedFiles = installedMap.Keys
            .Where(key => !sourceMap.TryGetValue(key, out var sourceFingerprint) || !FingerprintsMatch(sourceFingerprint, installedMap[key]))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deletedFiles = sourceMap.Keys
            .Except(installedMap.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var file in changedFiles)
        {
            var sourcePath = Path.Combine(installedDirectory, file.Replace('/', Path.DirectorySeparatorChar));
            var targetPath = Path.Combine(overlayFilesRoot, file.Replace('/', Path.DirectorySeparatorChar));
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
            File.WriteAllText(deletedFilePath, System.Text.Json.JsonSerializer.Serialize(deletedFiles, SerializerOptions));
        }

        if (changedFiles.Length == 0 && deletedFiles.Length == 0)
        {
            return OverlaySnapshot.Empty;
        }

        return new OverlaySnapshot(overlayRoot, overlayFilesRoot, deletedFiles, changedFiles.Length);
    }
}
