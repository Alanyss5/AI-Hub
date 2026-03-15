using System.IO;
using AIHub.Application.Abstractions;
using AIHub.Application.Models;
using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class WorkspaceControlService
{
    private readonly IHubRootLocator _hubRootLocator;
    private readonly Func<string?, IProjectRegistry> _projectRegistryFactory;
    private readonly Func<string?, IHubSettingsStore> _hubSettingsStoreFactory;
    private readonly IWorkspaceAutomationService _workspaceAutomationService;
    private readonly HubDashboardService _hubDashboardService;

    public WorkspaceControlService(
        IHubRootLocator hubRootLocator,
        IProjectRegistry projectRegistry,
        IHubSettingsStore hubSettingsStore,
        IWorkspaceAutomationService workspaceAutomationService,
        HubDashboardService hubDashboardService)
        : this(
            hubRootLocator,
            _ => projectRegistry,
            _ => hubSettingsStore,
            workspaceAutomationService,
            hubDashboardService)
    {
    }

    public WorkspaceControlService(
        IHubRootLocator hubRootLocator,
        Func<string?, IProjectRegistry> projectRegistryFactory,
        Func<string?, IHubSettingsStore> hubSettingsStoreFactory,
        IWorkspaceAutomationService workspaceAutomationService,
        HubDashboardService hubDashboardService)
    {
        _hubRootLocator = hubRootLocator;
        _projectRegistryFactory = projectRegistryFactory;
        _hubSettingsStoreFactory = hubSettingsStoreFactory;
        _workspaceAutomationService = workspaceAutomationService;
        _hubDashboardService = hubDashboardService;
    }

    public async Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        IReadOnlyList<ProjectRecord> projects = Array.Empty<ProjectRecord>();

        if (resolution.IsValid && !string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            projects = await _projectRegistryFactory(resolution.RootPath).GetAllAsync(cancellationToken);
            settings = settings with { HubRoot = resolution.RootPath };
        }

        var dashboard = _hubDashboardService.CreateSnapshot(resolution, projects);
        return new WorkspaceSnapshot(resolution, dashboard, settings, SortProjects(projects));
    }

    public async Task<OperationResult> SetHubRootAsync(string candidatePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return OperationResult.Fail("AI-Hub 根目录不能为空。");
        }

        var resolution = await _hubRootLocator.EvaluateAsync(candidatePath, cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("指定目录不是有效的 AI-Hub 根目录。", string.Join(Environment.NewLine, resolution.Errors));
        }

        _hubRootLocator.SetPreferredRoot(resolution.RootPath);
        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        settings = settings with { HubRoot = resolution.RootPath };
        await settingsStore.SaveAsync(settings, cancellationToken);

        return OperationResult.Ok("AI-Hub 工作区已切换。", resolution.RootPath);
    }

    public async Task<OperationResult> SaveAutomationSettingsAsync(bool autoStartManagedMcpOnLoad, CancellationToken cancellationToken = default)
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
            AutoStartManagedMcpOnLoad = autoStartManagedMcpOnLoad
        };

        await settingsStore.SaveAsync(settings, cancellationToken);
        return OperationResult.Ok(
            "控制台设置已保存。",
            autoStartManagedMcpOnLoad
                ? "启动控制台时会自动启动已勾选自启动的托管型 MCP。"
                : "已关闭托管型 MCP 自动启动。");
    }

    public async Task<OperationResult> SwitchToGlobalScopeAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法切换作用域。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        settings = settings with
        {
            HubRoot = resolution.RootPath,
            ActiveScope = WorkspaceScope.Global,
            DefaultProfile = ProfileKind.Global
        };
        await settingsStore.SaveAsync(settings, cancellationToken);

        return OperationResult.Ok("当前作用域已切换为全局级。", resolution.RootPath);
    }

    public async Task<OperationResult> SwitchToProjectScopeAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        var saveResult = await SaveProjectAsync(project, cancellationToken);
        if (!saveResult.Success)
        {
            return saveResult;
        }

        return await SetCurrentProjectAsync(project, cancellationToken);
    }

    public async Task<OperationResult> SaveProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        return await SaveProjectAsync(project, originalProjectPath: null, cancellationToken);
    }

    public async Task<OperationResult> SaveProjectAsync(ProjectRecord project, string? originalProjectPath, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateProject(project);
        if (validationError is not null)
        {
            return OperationResult.Fail(validationError);
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法保存项目。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedProject = project with { Path = NormalizePath(project.Path) };
        var normalizedOriginalPath = string.IsNullOrWhiteSpace(originalProjectPath)
            ? null
            : NormalizePath(originalProjectPath);
        var projectRegistry = _projectRegistryFactory(resolution.RootPath);
        var existingProjects = await projectRegistry.GetAllAsync(cancellationToken);
        var updatedProjects = existingProjects
            .Where(existing =>
                !PathsMatch(existing.Path, normalizedProject.Path)
                && (string.IsNullOrWhiteSpace(normalizedOriginalPath) || !PathsMatch(existing.Path, normalizedOriginalPath)))
            .ToList();

        updatedProjects.Add(normalizedProject);
        await projectRegistry.SaveAllAsync(SortProjects(updatedProjects), cancellationToken);

        if (!string.IsNullOrWhiteSpace(normalizedOriginalPath)
            && !PathsMatch(normalizedOriginalPath, normalizedProject.Path))
        {
            var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
            var settings = await settingsStore.LoadAsync(cancellationToken);
            var updatedLastOpenedProject = !string.IsNullOrWhiteSpace(settings.LastOpenedProject) && PathsMatch(settings.LastOpenedProject, normalizedOriginalPath)
                ? normalizedProject.Path
                : settings.LastOpenedProject;
            var updatedOnboardedPaths = settings.OnboardedProjectPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => PathsMatch(path, normalizedOriginalPath) ? normalizedProject.Path : NormalizePath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (!string.Equals(updatedLastOpenedProject, settings.LastOpenedProject, StringComparison.OrdinalIgnoreCase)
                || !updatedOnboardedPaths.SequenceEqual(settings.OnboardedProjectPaths, StringComparer.OrdinalIgnoreCase))
            {
                await settingsStore.SaveAsync(settings with
                {
                    LastOpenedProject = updatedLastOpenedProject,
                    OnboardedProjectPaths = updatedOnboardedPaths
                }, cancellationToken);
            }
        }

        return OperationResult.Ok("项目已保存到注册表。", normalizedProject.Path);
    }

    public async Task<OperationResult> DeleteProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return OperationResult.Fail("请先选择要删除的项目。");
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法删除项目。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedPath = NormalizePath(projectPath);
        var projectRegistry = _projectRegistryFactory(resolution.RootPath);
        var existingProjects = await projectRegistry.GetAllAsync(cancellationToken);
        var updatedProjects = existingProjects
            .Where(existing => !PathsMatch(existing.Path, normalizedPath))
            .ToList();

        if (updatedProjects.Count == existingProjects.Count)
        {
            return OperationResult.Fail("项目未在注册表中找到。", normalizedPath);
        }

        await projectRegistry.SaveAllAsync(SortProjects(updatedProjects), cancellationToken);

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var updatedOnboardedPaths = settings.OnboardedProjectPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(path => !PathsMatch(path, normalizedPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var shouldSaveSettings = updatedOnboardedPaths.Length != settings.OnboardedProjectPaths.Length;
        if (!string.IsNullOrWhiteSpace(settings.LastOpenedProject) && PathsMatch(settings.LastOpenedProject, normalizedPath))
        {
            settings = settings with
            {
                ActiveScope = WorkspaceScope.Global,
                DefaultProfile = ProfileKind.Global,
                LastOpenedProject = null,
                OnboardedProjectPaths = updatedOnboardedPaths
            };
            shouldSaveSettings = true;
        }
        else
        {
            settings = settings with
            {
                OnboardedProjectPaths = updatedOnboardedPaths
            };
        }

        if (shouldSaveSettings)
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
        }

        return OperationResult.Ok("项目已从注册表删除。", normalizedPath);
    }

    public async Task<OperationResult> SetCurrentProjectAsync(ProjectRecord project, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateProject(project);
        if (validationError is not null)
        {
            return OperationResult.Fail(validationError);
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法设置当前项目。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        settings = settings with
        {
            HubRoot = resolution.RootPath,
            ActiveScope = WorkspaceScope.Project,
            DefaultProfile = project.Profile,
            LastOpenedProject = NormalizePath(project.Path)
        };

        await settingsStore.SaveAsync(settings, cancellationToken);
        return OperationResult.Ok("当前项目已切换。", settings.LastOpenedProject);
    }

    public async Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(
        bool forceRescan = false,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return WorkspaceOnboardingPreviewResult.Fail("AI-Hub 根目录无效，无法扫描全局接管项。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var previewResult = await _workspaceAutomationService.PreviewGlobalOnboardingAsync(resolution.RootPath, cancellationToken);
        if (!previewResult.Success || previewResult.Preview is null)
        {
            return previewResult;
        }

        var settings = await _hubSettingsStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
        var isFirstRun = !settings.GlobalOnboardingCompleted;
        var preview = previewResult.Preview with
        {
            IsFirstRun = isFirstRun,
            RequiresDecision = previewResult.Preview.Candidates.Count > 0 && (forceRescan || isFirstRun)
        };

        return previewResult with { Preview = preview };
    }

    public async Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
        ProjectRecord project,
        bool forceRescan = false,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateProject(project);
        if (validationError is not null)
        {
            return WorkspaceOnboardingPreviewResult.Fail(validationError);
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return WorkspaceOnboardingPreviewResult.Fail("AI-Hub 根目录无效，无法扫描项目接管项。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedProject = project with { Path = NormalizePath(project.Path) };
        var previewResult = await _workspaceAutomationService.PreviewProjectOnboardingAsync(
            resolution.RootPath,
            normalizedProject.Path,
            normalizedProject.Profile,
            cancellationToken);
        if (!previewResult.Success || previewResult.Preview is null)
        {
            return previewResult;
        }

        var settings = await _hubSettingsStoreFactory(resolution.RootPath).LoadAsync(cancellationToken);
        var isFirstRun = !settings.OnboardedProjectPaths.Any(path => PathsMatch(path, normalizedProject.Path));
        var preview = previewResult.Preview with
        {
            IsFirstRun = isFirstRun,
            RequiresDecision = previewResult.Preview.Candidates.Count > 0 && (forceRescan || isFirstRun)
        };

        return previewResult with { Preview = preview };
    }

    public async Task<OperationResult> ApplyGlobalLinksAsync(
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法应用全局链接。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var operation = await _workspaceAutomationService.ApplyGlobalLinksAsync(resolution.RootPath, importDecisions, cancellationToken);
        if (!operation.Success)
        {
            return operation;
        }

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        settings = settings with
        {
            HubRoot = resolution.RootPath,
            ActiveScope = WorkspaceScope.Global,
            DefaultProfile = ProfileKind.Global,
            GlobalOnboardingCompleted = true,
            GlobalOnboardingCompletedAt = DateTimeOffset.Now
        };

        await settingsStore.SaveAsync(settings, cancellationToken);
        return OperationResult.Ok("全局链接已应用。", operation.Details);
    }

    public async Task<OperationResult> ApplyProjectProfileAsync(
        ProjectRecord project,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateProject(project);
        if (validationError is not null)
        {
            return OperationResult.Fail(validationError);
        }

        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法应用项目 Profile。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var normalizedProject = project with { Path = NormalizePath(project.Path) };
        var operation = await _workspaceAutomationService.ApplyProjectProfileAsync(
            resolution.RootPath,
            normalizedProject.Path,
            normalizedProject.Profile,
            importDecisions,
            cancellationToken);

        if (!operation.Success)
        {
            return operation;
        }

        var saveProjectResult = await SaveProjectAsync(normalizedProject, cancellationToken);
        if (!saveProjectResult.Success)
        {
            return saveProjectResult;
        }

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        settings = settings with
        {
            HubRoot = resolution.RootPath,
            ActiveScope = WorkspaceScope.Project,
            DefaultProfile = normalizedProject.Profile,
            LastOpenedProject = normalizedProject.Path,
            OnboardedProjectPaths = AddOnboardedProjectPath(settings.OnboardedProjectPaths, normalizedProject.Path)
        };

        await settingsStore.SaveAsync(settings, cancellationToken);
        return OperationResult.Ok("项目 Profile 已应用。", operation.Details);
    }

    private static string? ValidateProject(ProjectRecord project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            return "项目名称不能为空。";
        }

        if (string.IsNullOrWhiteSpace(project.Path))
        {
            return "项目目录不能为空。";
        }

        if (!Directory.Exists(project.Path))
        {
            return "项目目录不存在：" + project.Path;
        }

        return null;
    }

    private static IReadOnlyList<ProjectRecord> SortProjects(IEnumerable<ProjectRecord> projects)
    {
        return projects
            .OrderByDescending(project => project.IsPinned)
            .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool PathsMatch(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string[] AddOnboardedProjectPath(IEnumerable<string> existingPaths, string projectPath)
    {
        var normalizedProjectPath = NormalizePath(projectPath);
        return existingPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Append(normalizedProjectPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
