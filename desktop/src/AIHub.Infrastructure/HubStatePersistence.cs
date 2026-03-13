using System.Text;

namespace AIHub.Infrastructure;

internal static class HubStatePersistence
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteTextWithBackup(string hubRoot, string targetPath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var fullHubRoot = Path.GetFullPath(hubRoot);
        var fullTargetPath = Path.GetFullPath(targetPath);
        var targetDirectory = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var backupPath = CreateBackupPath(fullHubRoot, fullTargetPath);
        var backupDirectory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        if (File.Exists(fullTargetPath))
        {
            File.Copy(fullTargetPath, backupPath, overwrite: true);
        }

        var tempPath = fullTargetPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content, Utf8NoBom);
            File.Move(tempPath, fullTargetPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, fullTargetPath, overwrite: true);
            }

            throw;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static void DeleteFileWithBackup(string hubRoot, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullTargetPath))
        {
            return;
        }

        var backupPath = CreateBackupPath(Path.GetFullPath(hubRoot), fullTargetPath);
        var backupDirectory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        File.Copy(fullTargetPath, backupPath, overwrite: true);
        File.Delete(fullTargetPath);
    }

    private static string CreateBackupPath(string hubRoot, string targetPath)
    {
        var relativePath = IsSubPathOf(targetPath, hubRoot)
            ? Path.GetRelativePath(hubRoot, targetPath)
            : Path.GetFileName(targetPath);

        return Path.Combine(
            hubRoot,
            "backups",
            "state-writes",
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"),
            relativePath);
    }

    private static bool IsSubPathOf(string candidatePath, string parentPath)
    {
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedParent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}