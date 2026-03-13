using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class WorkspaceControlService
{
    public async Task<OperationResult> ConfirmRiskAcceptanceAsync(HubRiskConsentKind kind, CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法保存风险确认。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var now = DateTimeOffset.Now;

        settings = kind switch
        {
            HubRiskConsentKind.ScriptExecution => settings with
            {
                ScriptExecutionRiskAccepted = true,
                ScriptExecutionRiskAcceptedAt = now
            },
            HubRiskConsentKind.ManagedMcpExecution => settings with
            {
                ManagedMcpRiskAccepted = true,
                ManagedMcpRiskAcceptedAt = now
            },
            HubRiskConsentKind.ExternalMcpImport => settings with
            {
                ExternalMcpImportRiskAccepted = true,
                ExternalMcpImportRiskAcceptedAt = now
            },
            _ => settings
        };

        await settingsStore.SaveAsync(settings with { HubRoot = resolution.RootPath }, cancellationToken);
        return OperationResult.Ok("风险确认已保存。", DescribeRiskAcceptance(kind, now));
    }

    public async Task<OperationResult> ResetRiskAcceptancesAsync(CancellationToken cancellationToken = default)
    {
        var resolution = await _hubRootLocator.ResolveAsync(cancellationToken);
        if (!resolution.IsValid || string.IsNullOrWhiteSpace(resolution.RootPath))
        {
            return OperationResult.Fail("AI-Hub 根目录无效，无法重置风险确认。", string.Join(Environment.NewLine, resolution.Errors));
        }

        var settingsStore = _hubSettingsStoreFactory(resolution.RootPath);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        settings = settings with
        {
            HubRoot = resolution.RootPath,
            ScriptExecutionRiskAccepted = false,
            ScriptExecutionRiskAcceptedAt = null,
            ManagedMcpRiskAccepted = false,
            ManagedMcpRiskAcceptedAt = null,
            ExternalMcpImportRiskAccepted = false,
            ExternalMcpImportRiskAcceptedAt = null
        };

        await settingsStore.SaveAsync(settings, cancellationToken);
        return OperationResult.Ok("风险确认已重置。", "脚本执行、托管 MCP 和外部 MCP 导入都需要重新确认。");
    }

    private static string DescribeRiskAcceptance(HubRiskConsentKind kind, DateTimeOffset confirmedAt)
    {
        var label = kind switch
        {
            HubRiskConsentKind.ScriptExecution => "脚本执行",
            HubRiskConsentKind.ManagedMcpExecution => "托管 MCP 运行",
            HubRiskConsentKind.ExternalMcpImport => "外部 MCP 导入",
            _ => "风险确认"
        };

        return label + " / 已确认时间：" + confirmedAt.ToString("yyyy-MM-dd HH:mm:ss");
    }
}