using System.Globalization;
using System.Text;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    public async Task<OperationResult> RollbackInstalledSkillAsync(
        ProfileKind profile,
        string relativePath,
        string? backupPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return await RollbackInstalledSkillAsync(profile, relativePath, cancellationToken);
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法回滚 Skill。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedRelativePath = NormalizePath(relativePath);
        var installDirectory = GetInstalledSkillDirectory(resolution.RootPath, profile, normalizedRelativePath);
        if (!Directory.Exists(installDirectory))
        {
            return OperationResult.Fail("目标 Skill 目录不存在。", installDirectory);
        }

        var normalizedBackupPath = Path.GetFullPath(backupPath.Trim());
        var backupRoot = GetBackupRoot(resolution.RootPath, profile, normalizedRelativePath);
        if (!Directory.Exists(normalizedBackupPath) || !normalizedBackupPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail("指定的备份目录无效。", normalizedBackupPath);
        }

        var states = (await LoadStatesAsync(resolution.RootPath, cancellationToken)).ToList();
        var installKey = GetInstallKey(profile, normalizedRelativePath);
        var state = states.FirstOrDefault(item => GetInstallKey(item.Profile, item.InstalledRelativePath) == installKey);
        if (state is null)
        {
            return OperationResult.Fail("该 Skill 还没有同步状态，无法回滚。", normalizedRelativePath);
        }

        var currentSnapshotBackupPath = CreateBackupSnapshot(resolution.RootPath, profile, normalizedRelativePath, installDirectory, "pre-rollback");
        ReplaceDirectoryWithSource(normalizedBackupPath, installDirectory);

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
        detailBuilder.AppendLine("已从指定备份回滚 Skill。");
        detailBuilder.AppendLine("回滚来源：" + normalizedBackupPath);
        detailBuilder.AppendLine("当前内容备份：" + currentSnapshotBackupPath);
        detailBuilder.AppendLine("安装目录：" + installDirectory);

        return OperationResult.Ok("Skill 已回滚到所选备份。", detailBuilder.ToString().TrimEnd());
    }

    private static IReadOnlyList<SkillBackupRecord> GetRecentBackupRecords(string hubRoot, ProfileKind profile, string relativePath, int maxCount = 8)
    {
        return GetRecentBackups(hubRoot, profile, relativePath, maxCount)
            .Select(path => new SkillBackupRecord
            {
                Name = Path.GetFileName(path),
                Path = path,
                CreatedAt = ParseBackupTimestamp(Path.GetFileName(path))
            })
            .ToArray();
    }

    private static DateTimeOffset? ParseBackupTimestamp(string? backupDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(backupDirectoryName))
        {
            return null;
        }

        var timestampSegment = backupDirectoryName.Split('-', 3, StringSplitOptions.RemoveEmptyEntries);
        if (timestampSegment.Length < 2)
        {
            return null;
        }

        var candidate = timestampSegment[0] + "-" + timestampSegment[1];
        return DateTimeOffset.TryParseExact(
            candidate,
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : null;
    }
}