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

    public Task<OperationResult> ApplyGlobalLinksAsync(string hubRoot, CancellationToken cancellationToken = default)
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

    public Task<OperationResult> ApplyProjectProfileAsync(string hubRoot, string projectPath, ProfileKind profile, CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(hubRoot, "scripts", "use-profile.ps1");

        return PowerShellScriptRunner.RunAsync(
            scriptPath,
            ["-HubRoot", hubRoot, "-ProjectPath", projectPath, "-Profile", profile.ToStorageValue()],
            "已执行项目 Profile 脚本。",
            "执行项目 Profile 脚本失败。",
            cancellationToken,
            _diagnosticLogService);
    }
}