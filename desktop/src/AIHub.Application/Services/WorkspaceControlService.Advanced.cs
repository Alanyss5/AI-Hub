using System.Text.Json;
using System.Text.Json.Serialization;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class WorkspaceControlService
{
    private const string SupportedConfigurationPackageVersion = "1.0";
    private static readonly JsonSerializerOptions PackageSerializerOptions = CreatePackageSerializerOptions();

    public async Task<OperationResult> SaveAutomationSettingsAsync(
        bool autoStartManagedMcpOnLoad,
        bool autoCheckSkillUpdatesOnLoad,
        bool autoSyncSafeSkillsOnLoad,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法保存设置。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        settings = settings with
        {
            HubRoot = resolution.RootPath,
            AutoStartManagedMcpOnLoad = autoStartManagedMcpOnLoad,
            AutoCheckSkillUpdatesOnLoad = autoCheckSkillUpdatesOnLoad,
            AutoSyncSafeSkillsOnLoad = autoSyncSafeSkillsOnLoad
        };

        await settingsStore.SaveAsync(settings, cancellationToken);
        return OperationResult.Ok(
            "控制台设置已保存。",
            string.Join(Environment.NewLine, new[]
            {
                autoStartManagedMcpOnLoad ? "已启用托管型 MCP 自动启动。" : "已关闭托管型 MCP 自动启动。",
                autoCheckSkillUpdatesOnLoad ? "已启用启动时自动检查 Skills 更新。" : "已关闭启动时自动检查 Skills 更新。",
                autoSyncSafeSkillsOnLoad ? "已启用启动时自动同步安全的托管 / 覆盖层 Skill。" : "已关闭启动时自动同步安全 Skill。"
            }));
    }

    public async Task<OperationResult> ExportConfigurationPackageAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return OperationResult.Fail("请先选择导出文件路径。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法导出配置包。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var rootPath = resolution.RootPath;
        var packageDirectory = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            Directory.CreateDirectory(packageDirectory);
        }

        var package = new WorkspacePackageRecord
        {
            ExportedAt = DateTimeOffset.Now,
            HubRoot = rootPath,
            Settings = await _hubSettingsStoreFactory(rootPath).LoadAsync(cancellationToken),
            Projects = (await _projectRegistryFactory(rootPath).GetAllAsync(cancellationToken)).ToList(),
            SkillsSourcesJson = await ReadTextFileAsync(Path.Combine(rootPath, "skills", "sources.json"), cancellationToken),
            SkillsInstallsJson = await ReadTextFileAsync(Path.Combine(rootPath, "config", "skills-installs.json"), cancellationToken),
            SkillsStatesJson = await ReadTextFileAsync(Path.Combine(rootPath, "config", "skills-state.json"), cancellationToken),
            McpRuntimeJson = await ReadTextFileAsync(Path.Combine(rootPath, "mcp", "runtime.json"), cancellationToken),
            McpManifestJson = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["global"] = await ReadTextFileAsync(Path.Combine(rootPath, "mcp", "manifest", "global.json"), cancellationToken) ?? string.Empty,
                ["frontend"] = await ReadTextFileAsync(Path.Combine(rootPath, "mcp", "manifest", "frontend.json"), cancellationToken) ?? string.Empty,
                ["backend"] = await ReadTextFileAsync(Path.Combine(rootPath, "mcp", "manifest", "backend.json"), cancellationToken) ?? string.Empty
            },
            SkillOverrides = ReadDirectoryFilesAsBase64(Path.Combine(rootPath, "skills-overrides"))
        };

        var json = JsonSerializer.Serialize(package, PackageSerializerOptions);
        await File.WriteAllTextAsync(packagePath, json, cancellationToken);
        return OperationResult.Ok("AI-Hub 配置包已导出。", packagePath);
    }

    public async Task<ConfigurationPackageImportPreviewResult> PreviewConfigurationPackageImportAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return new ConfigurationPackageImportPreviewResult(false, "请先选择要导入的配置包。", string.Empty, null);
        }

        if (!File.Exists(packagePath))
        {
            return new ConfigurationPackageImportPreviewResult(false, "配置包文件不存在。", packagePath, null);
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return new ConfigurationPackageImportPreviewResult(
                false,
                "AI-Hub 根目录无效，无法预检配置包。",
                string.Join(Environment.NewLine, resolution.Errors),
                null);
        }

        var packageResult = await TryReadWorkspacePackageAsync(packagePath, cancellationToken);
        if (!packageResult.Success || packageResult.Package is null)
        {
            return new ConfigurationPackageImportPreviewResult(false, packageResult.Message, packageResult.Details ?? string.Empty, null);
        }

        var package = packageResult.Package;
        if (!string.Equals(package.Version, SupportedConfigurationPackageVersion, StringComparison.Ordinal))
        {
            return new ConfigurationPackageImportPreviewResult(
                false,
                "配置包版本不受支持。",
                string.Join(Environment.NewLine, new[]
                {
                    "当前仅支持版本：" + SupportedConfigurationPackageVersion,
                    "检测到版本：" + (string.IsNullOrWhiteSpace(package.Version) ? "未声明" : package.Version),
                    "请使用相同版本导出的配置包。"
                }),
                null);
        }

        var includedSections = BuildIncludedSections(package);
        var replaceTargets = BuildReplaceTargets();
        var plannedBackupPath = BuildConfigurationBackupPath(resolution.RootPath, DateTimeOffset.Now);
        var preview = new ConfigurationPackageImportPreview
        {
            PackagePath = Path.GetFullPath(packagePath),
            Version = package.Version,
            ExportedAt = package.ExportedAt,
            PlannedBackupPath = plannedBackupPath,
            IncludedSections = includedSections,
            ReplaceTargets = replaceTargets,
            Summary = $"版本 {package.Version}，包含 {includedSections.Count} 个配置分组，将覆盖 {replaceTargets.Count} 类本地配置。",
            Details = BuildImportPreviewDetails(packagePath, package, includedSections, replaceTargets, plannedBackupPath)
        };

        return new ConfigurationPackageImportPreviewResult(true, "配置包预检通过。", preview.Details, preview);
    }

    public Task<OperationResult> ImportConfigurationPackageAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        return ImportConfigurationPackageAsync(packagePath, null, cancellationToken);
    }

    public async Task<OperationResult> ImportConfigurationPackageAsync(
        string packagePath,
        string? plannedBackupPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return OperationResult.Fail("请先选择要导入的配置包。");
        }

        if (!File.Exists(packagePath))
        {
            return OperationResult.Fail("配置包文件不存在。", packagePath);
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法导入配置包。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var packageResult = await TryReadWorkspacePackageAsync(packagePath, cancellationToken);
        if (!packageResult.Success || packageResult.Package is null)
        {
            return OperationResult.Fail(packageResult.Message, packageResult.Details);
        }

        var package = packageResult.Package;
        if (!string.Equals(package.Version, SupportedConfigurationPackageVersion, StringComparison.Ordinal))
        {
            return OperationResult.Fail(
                "配置包版本不受支持。",
                string.Join(Environment.NewLine, new[]
                {
                    "当前仅支持版本：" + SupportedConfigurationPackageVersion,
                    "检测到版本：" + (string.IsNullOrWhiteSpace(package.Version) ? "未声明" : package.Version)
                }));
        }

        var rootPath = resolution.RootPath;
        var backupPath = CreateConfigurationBackup(rootPath, plannedBackupPath);

        await _hubSettingsStoreFactory(rootPath).SaveAsync((package.Settings ?? new HubSettingsRecord()) with { HubRoot = rootPath }, cancellationToken);
        await _projectRegistryFactory(rootPath).SaveAllAsync(SortProjects(package.Projects ?? []), cancellationToken);

        await WriteOptionalTextFileAsync(Path.Combine(rootPath, "skills", "sources.json"), package.SkillsSourcesJson, cancellationToken);
        await WriteOptionalTextFileAsync(Path.Combine(rootPath, "config", "skills-installs.json"), package.SkillsInstallsJson, cancellationToken);
        await WriteOptionalTextFileAsync(Path.Combine(rootPath, "config", "skills-state.json"), package.SkillsStatesJson, cancellationToken);
        await WriteOptionalTextFileAsync(Path.Combine(rootPath, "mcp", "runtime.json"), package.McpRuntimeJson, cancellationToken);

        var manifestRoot = Path.Combine(rootPath, "mcp", "manifest");
        Directory.CreateDirectory(manifestRoot);
        foreach (var entry in package.McpManifestJson ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            await WriteOptionalTextFileAsync(Path.Combine(manifestRoot, entry.Key + ".json"), entry.Value, cancellationToken);
        }

        RestoreDirectoryFilesFromBase64(
            Path.Combine(rootPath, "skills-overrides"),
            package.SkillOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        return OperationResult.Ok(
            "AI-Hub 配置包已导入。",
            string.Join(Environment.NewLine, new[]
            {
                "导入包：" + Path.GetFullPath(packagePath),
                "导出时间：" + package.ExportedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                "当前配置备份：" + backupPath,
                "如需回退，请从备份目录恢复相关文件。"
            }));
    }

    private static JsonSerializerOptions CreatePackageSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static async Task<string?> ReadTextFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static async Task WriteOptionalTextFileAsync(string path, string? content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    private static string CreateConfigurationBackup(string rootPath, string? backupPath = null)
    {
        var normalizedBackupPath = string.IsNullOrWhiteSpace(backupPath)
            ? BuildConfigurationBackupPath(rootPath, DateTimeOffset.Now)
            : Path.GetFullPath(backupPath);

        Directory.CreateDirectory(normalizedBackupPath);

        CopyIfExists(Path.Combine(rootPath, "config", "hub-settings.json"), Path.Combine(normalizedBackupPath, "config", "hub-settings.json"));
        CopyIfExists(Path.Combine(rootPath, "projects", "projects.json"), Path.Combine(normalizedBackupPath, "projects", "projects.json"));
        CopyIfExists(Path.Combine(rootPath, "skills", "sources.json"), Path.Combine(normalizedBackupPath, "skills", "sources.json"));
        CopyIfExists(Path.Combine(rootPath, "config", "skills-installs.json"), Path.Combine(normalizedBackupPath, "config", "skills-installs.json"));
        CopyIfExists(Path.Combine(rootPath, "config", "skills-state.json"), Path.Combine(normalizedBackupPath, "config", "skills-state.json"));
        CopyIfExists(Path.Combine(rootPath, "mcp", "runtime.json"), Path.Combine(normalizedBackupPath, "mcp", "runtime.json"));
        CopyDirectoryIfExists(Path.Combine(rootPath, "mcp", "manifest"), Path.Combine(normalizedBackupPath, "mcp", "manifest"));
        CopyDirectoryIfExists(Path.Combine(rootPath, "skills-overrides"), Path.Combine(normalizedBackupPath, "skills-overrides"));

        return normalizedBackupPath;
    }

    private static string BuildConfigurationBackupPath(string rootPath, DateTimeOffset timestamp)
    {
        return Path.Combine(rootPath, "backups", "config-packages", timestamp.ToString("yyyyMMdd-HHmmss"));
    }

    private static IReadOnlyList<string> BuildIncludedSections(WorkspacePackageRecord package)
    {
        var sections = new List<string>();

        if (package.Settings is not null)
        {
            sections.Add("控制台设置");
        }

        sections.Add("项目清单（" + (package.Projects?.Count ?? 0) + " 个）");

        if (!string.IsNullOrWhiteSpace(package.SkillsSourcesJson))
        {
            sections.Add("Skills 来源");
        }

        if (!string.IsNullOrWhiteSpace(package.SkillsInstallsJson))
        {
            sections.Add("Skills 安装登记");
        }

        if (!string.IsNullOrWhiteSpace(package.SkillsStatesJson))
        {
            sections.Add("Skills 同步状态");
        }

        if (!string.IsNullOrWhiteSpace(package.McpRuntimeJson))
        {
            sections.Add("MCP 运行时定义");
        }

        if (package.McpManifestJson?.Count > 0)
        {
            sections.Add("MCP 清单（" + package.McpManifestJson.Count + " 个作用域）");
        }

        if (package.SkillOverrides?.Count > 0)
        {
            sections.Add("Skills 覆盖层（" + package.SkillOverrides.Count + " 个文件）");
        }

        return sections;
    }

    private static IReadOnlyList<string> BuildReplaceTargets()
    {
        return new[]
        {
            "控制台设置",
            "项目清单",
            "Skills 来源",
            "Skills 安装登记",
            "Skills 同步状态",
            "MCP 运行时定义",
            "MCP 清单",
            "Skills 覆盖层"
        };
    }

    private static string BuildImportPreviewDetails(
        string packagePath,
        WorkspacePackageRecord package,
        IReadOnlyList<string> includedSections,
        IReadOnlyList<string> replaceTargets,
        string plannedBackupPath)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "导入包：" + Path.GetFullPath(packagePath),
            "配置包版本：" + package.Version,
            "导出时间：" + package.ExportedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            "包含分组：" + (includedSections.Count == 0 ? "无" : string.Join("、", includedSections)),
            "将覆盖：" + string.Join("、", replaceTargets),
            "当前配置备份：" + plannedBackupPath
        });
    }

    private static async Task<WorkspacePackageLoadResult> TryReadWorkspacePackageAsync(string packagePath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(packagePath, cancellationToken);

        WorkspacePackageRecord? package;
        try
        {
            package = JsonSerializer.Deserialize<WorkspacePackageRecord>(json, PackageSerializerOptions);
        }
        catch (Exception exception)
        {
            return WorkspacePackageLoadResult.Fail("配置包格式无效。", exception.Message);
        }

        if (package is null)
        {
            return WorkspacePackageLoadResult.Fail("配置包内容为空。", packagePath);
        }

        return WorkspacePackageLoadResult.Ok(package);
    }

    private static Dictionary<string, string> ReadDirectoryFilesAsBase64(string rootDirectory)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(rootDirectory))
        {
            return files;
        }

        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            files[relativePath] = Convert.ToBase64String(File.ReadAllBytes(file));
        }

        return files;
    }

    private static void RestoreDirectoryFilesFromBase64(string rootDirectory, IReadOnlyDictionary<string, string> files)
    {
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }

        Directory.CreateDirectory(rootDirectory);
        foreach (var entry in files)
        {
            var targetPath = Path.Combine(rootDirectory, entry.Key.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(targetPath, Convert.FromBase64String(entry.Value));
        }
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static void CopyDirectoryIfExists(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDirectory, destinationDirectory));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var targetPath = file.Replace(sourceDirectory, destinationDirectory);
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private sealed record WorkspacePackageRecord
    {
        public string Version { get; init; } = SupportedConfigurationPackageVersion;

        public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.Now;

        public string? HubRoot { get; init; }

        public HubSettingsRecord? Settings { get; init; }

        public List<ProjectRecord> Projects { get; init; } = new();

        public string? SkillsSourcesJson { get; init; }

        public string? SkillsInstallsJson { get; init; }

        public string? SkillsStatesJson { get; init; }

        public string? McpRuntimeJson { get; init; }

        public Dictionary<string, string> McpManifestJson { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> SkillOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record WorkspacePackageLoadResult(bool Success, string Message, string? Details, WorkspacePackageRecord? Package)
    {
        public static WorkspacePackageLoadResult Ok(WorkspacePackageRecord package)
        {
            return new WorkspacePackageLoadResult(true, string.Empty, null, package);
        }

        public static WorkspacePackageLoadResult Fail(string message, string? details = null)
        {
            return new WorkspacePackageLoadResult(false, message, details, null);
        }
    }
}