using System.Text;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class NativeWorkspaceAutomationService : IWorkspaceAutomationService
{
    private readonly IPlatformLinkService _platformLinkService;
    private readonly IPlatformCapabilitiesService _platformCapabilitiesService;
    private readonly IDiagnosticLogService? _diagnosticLogService;
    private readonly Func<string> _userHomeResolver;

    public NativeWorkspaceAutomationService(
        IPlatformLinkService platformLinkService,
        IPlatformCapabilitiesService platformCapabilitiesService,
        IDiagnosticLogService? diagnosticLogService = null,
        Func<string>? userHomeResolver = null)
    {
        _platformLinkService = platformLinkService;
        _platformCapabilitiesService = platformCapabilitiesService;
        _diagnosticLogService = diagnosticLogService;
        _userHomeResolver = userHomeResolver ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public Task<OperationResult> ApplyGlobalLinksAsync(string hubRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capability = _platformCapabilitiesService.Describe();
        if (!capability.SupportsJunctionLinks)
        {
            return Task.FromResult(OperationResult.Fail(capability.Summary));
        }

        var userHome = Path.GetFullPath(_userHomeResolver());
        var personalRoot = Path.Combine(userHome, "AI-Personal");
        var sharedAgents = Path.Combine(hubRoot, "agents", "global");
        var companySkills = Path.Combine(hubRoot, "skills", "global");
        var personalSkills = Path.Combine(personalRoot, "skills", "global");

        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".claude"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".agents"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".codex"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".gemini"));
        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".gemini", "antigravity"));
        _platformLinkService.EnsureDirectory(personalRoot);
        _platformLinkService.EnsureDirectory(Path.Combine(personalRoot, "skills"));
        _platformLinkService.EnsureDirectory(personalSkills);

        EnsureSkillsOverlay(Path.Combine(userHome, ".claude", "skills"), companySkills, personalSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".claude", "commands"), Path.Combine(hubRoot, "claude", "commands", "global"));
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".claude", "agents"), sharedAgents);
        EnsureSkillsOverlay(Path.Combine(userHome, ".agents", "skills"), companySkills, personalSkills);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".agents", "agents"), sharedAgents);
        EnsureSkillsOverlay(Path.Combine(userHome, ".gemini", "antigravity", "skills"), companySkills, personalSkills);

        _platformLinkService.EnsureDirectory(Path.Combine(userHome, ".codex", "skills"));
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".codex", "skills", "ai-hub"), companySkills, ignoreIfLocked: true);
        _platformLinkService.EnsureJunction(Path.Combine(userHome, ".codex", "skills", "personal"), personalSkills, ignoreIfLocked: true);

        RenderTemplateIfChanged(
            Path.Combine(hubRoot, "claude", "settings", "global.settings.json"),
            Path.Combine(userHome, ".claude", "settings.json"),
            hubRoot);

        _diagnosticLogService?.RecordInfo("workspace-automation", "已完成内部全局初始化。", hubRoot + Environment.NewLine + userHome);
        return Task.FromResult(OperationResult.Ok(
            "全局初始化已完成。",
            string.Join(Environment.NewLine, new[]
            {
                "用户目录：" + userHome,
                "公司 Skills：" + companySkills,
                "个人 Skills：" + personalSkills,
                "共享 Agents：" + sharedAgents,
                "Claude 设置：" + Path.Combine(userHome, ".claude", "settings.json")
            })));
    }

    public Task<OperationResult> ApplyProjectProfileAsync(string hubRoot, string projectPath, ProfileKind profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capability = _platformCapabilitiesService.Describe();
        if (!capability.SupportsJunctionLinks)
        {
            return Task.FromResult(OperationResult.Fail(capability.Summary));
        }

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        if (!Directory.Exists(normalizedProjectPath))
        {
            return Task.FromResult(OperationResult.Fail("项目目录不存在。", normalizedProjectPath));
        }

        _platformLinkService.EnsureDirectory(Path.Combine(normalizedProjectPath, ".claude"));
        _platformLinkService.EnsureDirectory(Path.Combine(normalizedProjectPath, ".agents"));
        _platformLinkService.EnsureDirectory(Path.Combine(normalizedProjectPath, ".agent"));
        _platformLinkService.EnsureDirectory(Path.Combine(normalizedProjectPath, ".codex"));

        var profileValue = profile.ToStorageValue();
        var agentTarget = Path.Combine(hubRoot, "agents", profileValue);
        var skillTarget = Path.Combine(hubRoot, "skills", profileValue);
        _platformLinkService.EnsureJunction(Path.Combine(normalizedProjectPath, ".claude", "skills"), skillTarget);
        _platformLinkService.EnsureJunction(Path.Combine(normalizedProjectPath, ".claude", "commands"), Path.Combine(hubRoot, "claude", "commands", profileValue));
        _platformLinkService.EnsureJunction(Path.Combine(normalizedProjectPath, ".claude", "agents"), agentTarget);
        _platformLinkService.EnsureJunction(Path.Combine(normalizedProjectPath, ".agents", "agents"), agentTarget);
        _platformLinkService.EnsureJunction(Path.Combine(normalizedProjectPath, ".agents", "skills"), skillTarget);
        _platformLinkService.EnsureJunction(Path.Combine(normalizedProjectPath, ".agent", "skills"), skillTarget);

        RenderTemplateIfChanged(
            Path.Combine(hubRoot, "claude", "settings", profileValue + ".settings.json"),
            Path.Combine(normalizedProjectPath, ".claude", "settings.json"),
            hubRoot);
        CopyTextIfChanged(
            Path.Combine(hubRoot, "mcp", "generated", "claude", profileValue + ".mcp.json"),
            Path.Combine(normalizedProjectPath, ".mcp.json"));
        CopyTextIfChanged(
            Path.Combine(hubRoot, "mcp", "generated", "codex", profileValue + ".config.toml"),
            Path.Combine(normalizedProjectPath, ".codex", "config.toml"));

        _diagnosticLogService?.RecordInfo("workspace-automation", "已完成内部项目 Profile 应用。", normalizedProjectPath + Environment.NewLine + profileValue);
        return Task.FromResult(OperationResult.Ok(
            "项目 Profile 已应用。",
            string.Join(Environment.NewLine, new[]
            {
                "项目目录：" + normalizedProjectPath,
                "Profile：" + profile.ToDisplayName(),
                "Skills 链接：" + skillTarget,
                "Agents 链接：" + agentTarget,
                "Claude MCP：" + Path.Combine(normalizedProjectPath, ".mcp.json"),
                "Codex 配置：" + Path.Combine(normalizedProjectPath, ".codex", "config.toml")
            })));
    }

    private void EnsureSkillsOverlay(string rootPath, string companyTarget, string personalTarget)
    {
        _platformLinkService.EnsureDirectory(rootPath);
        _platformLinkService.EnsureJunction(Path.Combine(rootPath, "company"), companyTarget);
        _platformLinkService.EnsureJunction(Path.Combine(rootPath, "personal"), personalTarget);
    }

    private static void RenderTemplateIfChanged(string templatePath, string destinationPath, string hubRoot)
    {
        if (!File.Exists(templatePath))
        {
            return;
        }

        var content = File.ReadAllText(templatePath, Encoding.UTF8)
            .Replace("__AI_HUB_ROOT_JSON__", hubRoot.Replace("\\", "\\\\", StringComparison.Ordinal));
        WriteTextIfChanged(destinationPath, content);
    }

    private static void CopyTextIfChanged(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        WriteTextIfChanged(destinationPath, File.ReadAllText(sourcePath, Encoding.UTF8));
    }

    private static void WriteTextIfChanged(string destinationPath, string content)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destinationPath))
        {
            var existing = File.ReadAllText(destinationPath, Encoding.UTF8);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }

            BackupIfExists(destinationPath);
        }

        File.WriteAllText(destinationPath, content, new UTF8Encoding(false));
    }

    private static void BackupIfExists(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var parent = Path.GetDirectoryName(path) ?? Path.GetPathRoot(path)!;
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var backupPath = Path.Combine(parent, fileName + ".bak." + timestamp);

        if (Directory.Exists(path))
        {
            Directory.Move(path, backupPath);
            return;
        }

        File.Move(path, backupPath);
    }
}
