using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class PowerShellMcpAutomationService : IMcpAutomationService
{
    private readonly IDiagnosticLogService? _diagnosticLogService;

    public PowerShellMcpAutomationService(IDiagnosticLogService? diagnosticLogService = null)
    {
        _diagnosticLogService = diagnosticLogService;
    }

    public Task<OperationResult> GenerateConfigsAsync(string hubRoot, CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(hubRoot, "scripts", "sync-mcp.ps1");

        return PowerShellScriptRunner.RunAsync(
            scriptPath,
            ["-HubRoot", hubRoot],
            "已执行 MCP 配置生成脚本。",
            "执行 MCP 配置生成脚本失败。",
            cancellationToken,
            _diagnosticLogService);
    }
}