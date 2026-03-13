using System.Text.RegularExpressions;

namespace AIHub.Application.Tests;

public sealed class ProductRepositoryGuardTests
{
    private static readonly string RepoRoot = "C:\\AI-Hub";
    private static readonly Regex TodoPattern = new(BuildTodoPattern(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] TextExtensions = [".cs", ".axaml", ".md", ".json", ".toml", ".ps1", ".csproj", ".sln"];
    private static readonly string[] BackupExtensions = [".bak", ".old", ".orig", ".tmp"];

    [Fact]
    public void Product_Directories_Do_Not_Contain_Backlog_Markers()
    {
        foreach (var file in EnumerateProductFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (TodoPattern.IsMatch(lines[index]))
                {
                    Assert.Fail($"Unexpected backlog marker in {file}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }
    }

    [Fact]
    public void Product_Directories_Do_Not_Contain_Backup_Files()
    {
        var backupFiles = EnumerateProductDirectoryFiles()
            .Where(path => BackupExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(backupFiles.Length == 0, "Unexpected backup files: " + string.Join(", ", backupFiles));
    }

    private static string BuildTodoPattern()
    {
        var markers = new[] { "TO" + "DO", "TO" + "DU", "FIX" + "ME", "HA" + "CK", "XX" + "X" };
        return @"\b(" + string.Join("|", markers) + @")\b";
    }

    private static IEnumerable<string> EnumerateProductFiles()
    {
        return EnumerateProductDirectoryFiles()
            .Where(path => TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateProductDirectoryFiles()
    {
        yield return Path.Combine(RepoRoot, "README.md");

        foreach (var directory in new[] { "desktop", "docs", "scripts", "config" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, directory), "*", SearchOption.AllDirectories))
            {
                if (!ShouldSkip(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static bool ShouldSkip(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}.verify{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}