using AIHub.Application.Abstractions;

namespace AIHub.Platform.Windows;

public sealed class WindowsPlatformLinkService : IPlatformLinkService
{
    public void EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return;
            }

            BackupIfExists(fullPath);
        }
        else if (File.Exists(fullPath))
        {
            BackupIfExists(fullPath);
        }

        Directory.CreateDirectory(fullPath);
    }

    public void EnsureJunction(string linkPath, string targetPath, bool ignoreIfLocked = false)
    {
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!Directory.Exists(fullTargetPath))
        {
            throw new InvalidOperationException("Target does not exist: " + fullTargetPath);
        }

        var parent = Path.GetDirectoryName(linkPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            EnsureDirectory(parent);
        }

        try
        {
            var fullLinkPath = Path.GetFullPath(linkPath);
            if (Directory.Exists(fullLinkPath) || File.Exists(fullLinkPath))
            {
                var currentTarget = TryGetLinkTarget(fullLinkPath);
                if (!string.IsNullOrWhiteSpace(currentTarget)
                    && string.Equals(NormalizePath(currentTarget), NormalizePath(fullTargetPath), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                BackupIfExists(fullLinkPath);
            }

            Directory.CreateSymbolicLink(fullLinkPath, fullTargetPath);
        }
        catch when (ignoreIfLocked)
        {
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? TryGetLinkTarget(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return null;
        }

        FileSystemInfo info = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return null;
        }

        return info.LinkTarget;
    }

    private static void BackupIfExists(string path)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var parent = Path.GetDirectoryName(path) ?? Path.GetPathRoot(path)!;
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var backupPath = Path.Combine(parent, fileName + ".bak." + timestamp);

        if (Directory.Exists(path))
        {
            Directory.Move(path, backupPath);
            return;
        }

        if (File.Exists(path))
        {
            File.Move(path, backupPath);
        }
    }
}