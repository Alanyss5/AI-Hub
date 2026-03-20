using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class PowerShellWorkspaceAutomationService : IWorkspaceAutomationService
{
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public PowerShellWorkspaceAutomationService(IDiagnosticLogService? diagnosticLogService = null)
    {
        _diagnosticLogService = diagnosticLogService;
    }

    public Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(string hubRoot, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "当前 PowerShell 工作流不支持接管预检，已跳过。",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Global,
                ProfileKind.Global,
                null,
                false,
                false,
                Array.Empty<WorkspaceOnboardingCandidate>(),
                "当前自动化实现不提供接管向导。")));
    }

    public Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
        string hubRoot,
        string projectPath,
        ProfileKind profile,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "当前 PowerShell 工作流不支持接管预检，已跳过。",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Project,
                profile,
                projectPath,
                false,
                false,
                Array.Empty<WorkspaceOnboardingCandidate>(),
                "当前自动化实现不提供接管向导。")));
    }

    public Task<OperationResult> ApplyGlobalLinksAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(hubRoot, "scripts", "setup-global.ps1");
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return PowerShellScriptRunner.RunAsync(
            scriptPath,
            ["-HubRoot", hubRoot, "-UserHome", userHome],
            "已执行全局链接脚本。",
            "执行全局链接脚本失败。",
            cancellationToken,
            _diagnosticLogService);
    }

    public Task<OperationResult> ApplyProjectProfileAsync(
        string hubRoot,
        string projectPath,
        ProfileKind profile,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(hubRoot, "scripts", "use-profile.ps1");

        return PowerShellScriptRunner.RunAsync(
            scriptPath,
            ["-HubRoot", hubRoot, "-ProjectPath", projectPath, "-Profile", profile switch
            {
                ProfileKind.Global => WorkspaceProfiles.Global,
                ProfileKind.Frontend => WorkspaceProfiles.Frontend,
                ProfileKind.Backend => WorkspaceProfiles.Backend,
                _ => WorkspaceProfiles.Global
            }],
            "已执行项目 Profile 脚本。",
            "执行项目 Profile 脚本失败。",
            cancellationToken,
            _diagnosticLogService);
    }
}
