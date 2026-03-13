using System.Text;
using System.Text.RegularExpressions;

namespace AIHub.Application.Tests;

public sealed class EncodingGuardTests
{
    private static readonly string RepoRoot = "C:\\AI-Hub";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Regex CSharpStringLiteralRegex = new("(?<!@)\\$?\\\"((?:[^\\\"\\\\]|\\\\.)*)\\\"", RegexOptions.Compiled);
    private static readonly string[] ForbiddenFragments =
    [
        "閸掗攱鏌",
        "濮掑倽顫",
        "姒涙顓",
        "閸氬海鐢",
        "瑜版挸澧",
        "妞ゅ湱娲",
        "閺夈儲绨",
        "閼存碍婀",
        "閹垫顓告潻娑氣柤",
        "鐠佸墽鐤",
        "鎵樼杩涚▼"
    ];

    [Fact]
    public void Guarded_Text_Files_Are_Valid_Utf8_Without_Known_Mojibake()
    {
        foreach (var path in EnumerateGuardedFiles())
        {
            var text = ReadUtf8(path);
            Assert.DoesNotContain('\uFFFD', text);

            foreach (var fragment in ForbiddenFragments)
            {
                Assert.DoesNotContain(fragment, text);
            }
        }
    }

    [Fact]
    public void MainWindowViewModel_Source_Files_Do_Not_Contain_Uncatalogued_User_Visible_Literals()
    {
        var allowedLiterals = new HashSet<string>(StringComparer.Ordinal)
        {
            "{\r\n  \"mcpServers\": {}\r\n}",
            "*.json",
            "aihub-config-package.json",
            "main",
            "Claude",
            "Codex",
            "Antigravity",
            "异常",
            "失败",
            "错误",
            "yyyy-MM-dd HH:mm:ss"
        };

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "ViewModels"),
                     "MainWindowViewModel*.cs",
                     SearchOption.TopDirectoryOnly))
        {
            if (ShouldSkip(path))
            {
                continue;
            }

            var text = ReadUtf8(path);
            foreach (var literal in ExtractCSharpStringLiterals(text))
            {
                if (!LooksLikeUserVisibleLiteral(literal) || allowedLiterals.Contains(literal))
                {
                    continue;
                }

                Assert.Fail($"Unexpected user-visible string literal in {path}: {literal}");
            }
        }
    }

    [Fact]
    public void MainWindow_Shell_Uses_Tab_Views_And_No_Click_Handlers()
    {
        var mainWindow = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "MainWindow.axaml"));
        Assert.Contains("<tabs:OverviewTabView", mainWindow);
        Assert.Contains("<tabs:ProjectsTabView", mainWindow);
        Assert.Contains("<tabs:SkillsTabView", mainWindow);
        Assert.Contains("<tabs:ScriptsTabView", mainWindow);
        Assert.Contains("<tabs:McpTabView", mainWindow);
        Assert.Contains("<tabs:SettingsTabView", mainWindow);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop"), "*.axaml", SearchOption.AllDirectories))
        {
            if (ShouldSkip(path))
            {
                continue;
            }

            Assert.DoesNotContain("Click=\"", ReadUtf8(path));
        }
    }

    private static IEnumerable<string> EnumerateGuardedFiles()
    {
        yield return Path.Combine(RepoRoot, "README.md");

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, "docs"), "*.md", SearchOption.AllDirectories))
        {
            if (!ShouldSkip(path))
            {
                yield return path;
            }
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, "desktop", "apps"), "*.axaml", SearchOption.AllDirectories))
        {
            if (!ShouldSkip(path))
            {
                yield return path;
            }
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, "desktop"), "*.cs", SearchOption.AllDirectories))
        {
            if (!ShouldSkip(path))
            {
                yield return path;
            }
        }
    }

    private static bool ShouldSkip(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}.verify{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith($"{Path.DirectorySeparatorChar}EncodingGuardTests.cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadUtf8(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return StrictUtf8.GetString(bytes);
    }

    private static IEnumerable<string> ExtractCSharpStringLiterals(string source)
    {
        foreach (Match match in CSharpStringLiteralRegex.Matches(source))
        {
            yield return Regex.Unescape(match.Groups[1].Value);
        }
    }

    private static bool LooksLikeUserVisibleLiteral(string literal)
    {
        if (string.IsNullOrWhiteSpace(literal))
        {
            return false;
        }

        if (literal.Contains('{') || literal.Contains('\\'))
        {
            return false;
        }

        if (literal is "异常" or "失败" or "错误" or "健康"
            || literal.Contains("寮傚父", StringComparison.Ordinal)
            || literal.Contains("澶辫触", StringComparison.Ordinal)
            || literal.Contains("閿欒", StringComparison.Ordinal))
        {
            return false;
        }

        return literal.Any(IsCjk) || (literal.Any(char.IsLetter) && literal.Contains(' '));
    }

    private static bool IsCjk(char character)
    {
        return character is >= '\u4E00' and <= '\u9FFF';
    }
}