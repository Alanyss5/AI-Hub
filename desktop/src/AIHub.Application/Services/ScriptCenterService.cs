using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed class ScriptCenterService
{
    private static readonly HashSet<string> HiddenFromDefaultList = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup-global.ps1",
        "sync-mcp.ps1",
        "use-profile.ps1"
    };

    private static readonly IReadOnlyDictionary<string, ScriptDefinitionRecord> KnownScripts =
        new Dictionary<string, ScriptDefinitionRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["setup-global.ps1"] = new ScriptDefinitionRecord(
                RelativePath: "setup-global.ps1",
                DisplayName: "全局初始化兼容入口",
                Category: "专家模式",
                Description: "兼容入口：全局初始化能力已内化到程序内部；此脚本保留给专家模式或外部手工调用。",
                UsesHubRoot: true,
                UsesProjectPath: false,
                UsesProfile: false,
                UsesUserHome: true,
                SupportsRawArguments: true),
            ["sync-mcp.ps1"] = new ScriptDefinitionRecord(
                RelativePath: "sync-mcp.ps1",
                DisplayName: "生成 MCP 配置兼容入口",
                Category: "专家模式",
                Description: "兼容入口：generated 配置生成已内化到程序内部；此脚本保留给专家模式或外部手工调用。",
                UsesHubRoot: true,
                UsesProjectPath: false,
                UsesProfile: false,
                UsesUserHome: false,
                SupportsRawArguments: true),
            ["use-profile.ps1"] = new ScriptDefinitionRecord(
                RelativePath: "use-profile.ps1",
                DisplayName: "项目 Profile 兼容入口",
                Category: "专家模式",
                Description: "兼容入口：项目 Profile 应用已内化到程序内部；此脚本保留给专家模式或外部手工调用。",
                UsesHubRoot: true,
                UsesProjectPath: true,
                UsesProfile: true,
                UsesUserHome: false,
                SupportsRawArguments: true),
            ["hooks/pre-tool-check.ps1"] = new ScriptDefinitionRecord(
                RelativePath: "hooks/pre-tool-check.ps1",
                DisplayName: "Claude PreTool Hook",
                Category: "Hook",
                Description: "Claude PreToolUse Hook 模板，可扩展为安全检查或规则校验。",
                UsesHubRoot: false,
                UsesProjectPath: false,
                UsesProfile: false,
                UsesUserHome: false,
                SupportsRawArguments: true),
            ["hooks/session-start.ps1"] = new ScriptDefinitionRecord(
                RelativePath: "hooks/session-start.ps1",
                DisplayName: "Claude SessionStart Hook",
                Category: "Hook",
                Description: "Claude SessionStart Hook 模板，可扩展为会话初始化动作。",
                UsesHubRoot: false,
                UsesProjectPath: false,
                UsesProfile: false,
                UsesUserHome: false,
                SupportsRawArguments: true)
        };

    private readonly IHubRootLocator _hubRootLocator;
    private readonly IScriptExecutionService _scriptExecutionService;

    public ScriptCenterService(IHubRootLocator hubRootLocator, IScriptExecutionService scriptExecutionService)
    {
        _hubRootLocator = hubRootLocator;
        _scriptExecutionService = scriptExecutionService;
    }

    public async Task<ScriptCenterSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return new ScriptCenterSnapshot(resolution, Array.Empty<ScriptDefinitionRecord>());
        }

        var scriptsRoot = Path.Combine(resolution.RootPath, "scripts");
        if (!Directory.Exists(scriptsRoot))
        {
            return new ScriptCenterSnapshot(resolution, Array.Empty<ScriptDefinitionRecord>());
        }

        var scripts = Directory
            .EnumerateFiles(scriptsRoot, "*.ps1", SearchOption.AllDirectories)
            .Select(path => CreateDefinition(scriptsRoot, path))
            .Where(script => !HiddenFromDefaultList.Contains(script.RelativePath))
            .OrderBy(script => script.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(script => script.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ScriptCenterSnapshot(resolution, scripts);
    }

    public async Task<OperationResult> ExecuteAsync(
        string relativePath,
        string? userHome,
        string? projectPath,
        string? profile,
        string? rawArguments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return OperationResult.Fail("请先选择要运行的脚本。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法执行脚本。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var scriptsRoot = Path.Combine(resolution.RootPath, "scripts");
        var normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
        var scriptPath = Path.Combine(scriptsRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(scriptPath))
        {
            return OperationResult.Fail("脚本不存在。", scriptPath);
        }

        var definition = CreateDefinition(scriptsRoot, scriptPath);
        var arguments = new List<string>();

        if (definition.UsesHubRoot)
        {
            arguments.Add("-HubRoot");
            arguments.Add(resolution.RootPath);
        }

        if (definition.UsesProjectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return OperationResult.Fail("该脚本需要项目目录。");
            }

            string normalizedProjectPath;
            try
            {
                normalizedProjectPath = Path.GetFullPath(projectPath.Trim());
            }
            catch (Exception exception)
            {
                return OperationResult.Fail("项目目录格式无效。", exception.Message);
            }

            if (!Directory.Exists(normalizedProjectPath))
            {
                return OperationResult.Fail("项目目录不存在。", normalizedProjectPath);
            }

            arguments.Add("-ProjectPath");
            arguments.Add(normalizedProjectPath);
        }

        if (definition.UsesProfile)
        {
            arguments.Add("-Profile");
            arguments.Add(WorkspaceProfiles.NormalizeId(profile));
        }

        if (definition.UsesUserHome)
        {
            var normalizedUserHome = string.IsNullOrWhiteSpace(userHome)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : userHome.Trim();

            try
            {
                normalizedUserHome = Path.GetFullPath(normalizedUserHome);
            }
            catch (Exception exception)
            {
                return OperationResult.Fail("用户目录格式无效。", exception.Message);
            }

            if (!Directory.Exists(normalizedUserHome))
            {
                return OperationResult.Fail("用户目录不存在。", normalizedUserHome);
            }

            arguments.Add("-UserHome");
            arguments.Add(normalizedUserHome);
        }

        if (definition.SupportsRawArguments)
        {
            try
            {
                arguments.AddRange(ParseArguments(rawArguments));
            }
            catch (Exception exception)
            {
                return OperationResult.Fail("自定义参数格式无效。", exception.Message);
            }
        }

        return await _scriptExecutionService.RunAsync(
            scriptPath,
            arguments,
            definition.DisplayName + " 已完成。",
            definition.DisplayName + " 执行失败。",
            cancellationToken);
    }

    private static ScriptDefinitionRecord CreateDefinition(string scriptsRoot, string scriptPath)
    {
        var relativePath = Path.GetRelativePath(scriptsRoot, scriptPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (KnownScripts.TryGetValue(relativePath, out var definition))
        {
            return definition;
        }

        var displayName = Path.GetFileNameWithoutExtension(scriptPath).Replace('-', ' ');
        var category = relativePath.Contains('/') ? "扩展脚本" : "通用脚本";
        return new ScriptDefinitionRecord(
            RelativePath: relativePath,
            DisplayName: displayName,
            Category: category,
            Description: "当前未提供内置说明，可直接附加自定义参数运行。",
            UsesHubRoot: false,
            UsesProjectPath: false,
            UsesProfile: false,
            UsesUserHome: false,
            SupportsRawArguments: true);
    }

    private static IReadOnlyList<string> ParseArguments(string? rawArguments)
    {
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var character in rawArguments)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    arguments.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("存在未闭合的引号。");
        }

        if (builder.Length > 0)
        {
            arguments.Add(builder.ToString());
        }

        return arguments;
    }
}
