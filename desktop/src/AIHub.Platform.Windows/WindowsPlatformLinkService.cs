using System.Diagnostics;
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
        if (TryGetPathAttributes(fullPath, out var attributes))
        {
            if ((attributes & FileAttributes.Directory) != 0
                && (attributes & FileAttributes.ReparsePoint) == 0)
            {
                return;
            }

            BackupIfExists(fullPath, attributes);
        }

        Directory.CreateDirectory(fullPath);
    }

    public void EnsureJunction(string linkPath, string targetPath, bool ignoreIfLocked = false)
    {
        var fullLinkPath = Path.GetFullPath(linkPath);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!Directory.Exists(fullTargetPath))
        {
            throw new InvalidOperationException("Windows 链接创建失败。" + Environment.NewLine
                + "入口：" + fullLinkPath + Environment.NewLine
                + "目标：" + fullTargetPath + Environment.NewLine
                + "原因：目标不存在。");
        }

        var parent = Path.GetDirectoryName(fullLinkPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            EnsureDirectory(parent);
        }

        try
        {
            if (TryGetPathAttributes(fullLinkPath, out var attributes))
            {
                var currentTarget = TryGetLinkTarget(fullLinkPath, attributes);
                if (!string.IsNullOrWhiteSpace(currentTarget)
                    && string.Equals(NormalizePath(currentTarget), NormalizePath(fullTargetPath), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                BackupIfExists(fullLinkPath, attributes);
            }

            CreateJunction(fullLinkPath, fullTargetPath);
        }
        catch when (ignoreIfLocked)
        {
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(BuildLinkFailureMessage(fullLinkPath, fullTargetPath, exception), exception);
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool TryGetPathAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        attributes = default;
        return false;
    }

    private static string? TryGetLinkTarget(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return null;
        }

        FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
            ? new DirectoryInfo(path)
            : new FileInfo(path);

        try
        {
            var resolved = info.ResolveLinkTarget(returnFinalTarget: false);
            if (resolved is not null)
            {
                return NormalizePath(resolved.FullName);
            }
        }
        catch
        {
        }

        var target = info.LinkTarget;
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        return Path.IsPathRooted(target)
            ? NormalizePath(target)
            : NormalizePath(Path.Combine(Path.GetDirectoryName(path) ?? Path.GetPathRoot(path)!, target));
    }

    private static void BackupIfExists(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.Move(path, BuildBackupPath(path));
            return;
        }

        File.Move(path, BuildBackupPath(path));
    }

    private static string BuildBackupPath(string path)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var parent = Path.GetDirectoryName(path) ?? Path.GetPathRoot(path)!;
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(parent, fileName + ".bak." + timestamp);
    }

    private static void CreateJunction(string linkPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c mklink /J {Quote(linkPath)} {Quote(targetPath)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(linkPath) ?? Environment.SystemDirectory
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 cmd.exe 来创建 Junction。");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0 && Directory.Exists(linkPath))
        {
            return;
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { standardOutput.Trim(), standardError.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(output)
            ? "mklink /J 执行失败。"
            : output);
    }

    private static string BuildLinkFailureMessage(string linkPath, string targetPath, Exception exception)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "Windows 链接创建失败。",
            "入口：" + linkPath,
            "目标：" + targetPath,
            "原因：" + ClassifyFailureReason(exception),
            "详情：" + exception.Message
        });
    }

    private static string ClassifyFailureReason(Exception exception)
    {
        if (exception is UnauthorizedAccessException
            || exception.Message.Contains("客户端没有所需的特权", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("privilege", StringComparison.OrdinalIgnoreCase))
        {
            return "权限不足，或系统尝试创建 SymbolicLink。";
        }

        if (exception is DirectoryNotFoundException
            || exception.Message.Contains("目标不存在", StringComparison.OrdinalIgnoreCase))
        {
            return "目标不存在。";
        }

        if (exception is IOException)
        {
            return "旧入口残留或入口正被占用。";
        }

        return "系统未能创建或重建 Junction。";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
