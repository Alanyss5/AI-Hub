using System.Text;
namespace AIHub.Application.Tests;
public sealed class EncodingGuardTests
{
    private static readonly string RepoRoot = "C:\\AI-Hub";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] ForbiddenFragments =
    [
        "褰撳墠",
        "鍏ㄥ眬",
        "椤圭洰",
        "闂佸",
        "閻熸",
        "缂備",
        "婵°",
        "濠㈣",
        "鏃у紡",
        "鏈"
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
    public void MainWindow_Shell_Uses_Tab_Views_And_No_Click_Handlers()
    {
        var mainWindow = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "MainWindow.axaml"));
        Assert.Contains("<tabs:OverviewTabView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<tabs:ProjectsTabView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<tabs:WorkspaceTabView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<tabs:SkillsTabView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<tabs:ScriptsTabView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<tabs:McpTabView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<tabs:SettingsTabView", mainWindow, StringComparison.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop"), "*.axaml", SearchOption.AllDirectories))
        {
            if (ShouldSkip(path))
            {
                continue;
            }
            Assert.DoesNotContain("Click=\"", ReadUtf8(path), StringComparison.Ordinal);
        }
    }
    [Fact]
    public void Desktop_Tab_Copy_Is_Readable()
    {
        var overview = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Views", "Tabs", "OverviewTabView.axaml"));
        var projects = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Views", "Tabs", "ProjectsTabView.axaml"));
        var workspace = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Views", "Tabs", "WorkspaceTabView.axaml"));
        var skills = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Views", "Tabs", "SkillsTabView.axaml"));
        var scripts = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Views", "Tabs", "ScriptsTabView.axaml"));
        var mcp = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Views", "Tabs", "McpTabView.axaml"));
        var settings = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Views", "Tabs", "SettingsTabView.axaml"));
        var textCatalog = ReadUtf8(Path.Combine(RepoRoot, "desktop", "apps", "AIHub.Desktop", "Text", "DesktopTextCatalog.cs"));

        Assert.Contains("WorkspaceTabHeader => \"工作区\"", textCatalog, StringComparison.Ordinal);
        Assert.Contains("已收口能力", overview, StringComparison.Ordinal);
        Assert.Contains("剩余正式使用门槛", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("宸叉敹鍙ｈ兘鍔", overview, StringComparison.Ordinal);

        Assert.Contains("项目列表", projects, StringComparison.Ordinal);
        Assert.Contains("项目资料", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("Project Details", projects, StringComparison.Ordinal);

        Assert.Contains("ProjectSectionTitle => \"项目接管\"", textCatalog, StringComparison.Ordinal);
        Assert.Contains("GlobalSectionTitle => \"全局工作区\"", textCatalog, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsSectionTitle => \"技术诊断\"", textCatalog, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Vm.Text.Workspace.ProjectSectionTitle}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Vm.Text.Workspace.GlobalSectionTitle}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Vm.Text.Workspace.DiagnosticsSectionTitle}\"", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Project Onboarding", workspace, StringComparison.Ordinal);
        Assert.Contains("当前操作对象", skills, StringComparison.Ordinal);
        Assert.Contains("绑定", skills, StringComparison.Ordinal);
        Assert.Contains("来源", skills, StringComparison.Ordinal);
        Assert.Contains("维护", skills, StringComparison.Ordinal);
        Assert.Contains("ReferenceLabel => \"引用\"", textCatalog, StringComparison.Ordinal);
        Assert.Contains("引用路径", skills, StringComparison.Ordinal);
        Assert.DoesNotContain("Current Context", skills, StringComparison.Ordinal);

        Assert.Contains("专家模式：这里仅保留 Hook 模板与外部诊断脚本", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("涓撳妯″紡", scripts, StringComparison.Ordinal);

        Assert.Contains("当前范围", mcp, StringComparison.Ordinal);
        Assert.Contains("Manifest 编辑器", mcp, StringComparison.Ordinal);
        Assert.Contains("校验", mcp, StringComparison.Ordinal);
        Assert.Contains("托管进程", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("Current Scope", mcp, StringComparison.Ordinal);

        Assert.Contains("Profile 目录", settings, StringComparison.Ordinal);
        Assert.Contains("Profile 编辑器", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Profile Catalog", settings, StringComparison.Ordinal);
    }
    private static IEnumerable<string> EnumerateGuardedFiles()
    {
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
}
