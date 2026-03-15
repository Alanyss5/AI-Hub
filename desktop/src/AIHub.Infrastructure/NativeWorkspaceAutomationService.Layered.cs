using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed partial class NativeWorkspaceAutomationService
{
    private Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingCoreAsync(string hubRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedHubRoot = NormalizePath(hubRoot);
        var userHome = NormalizePath(_userHomeResolver());
        var personalRoot = LayeredWorkspaceMaterializer.GetPersonalRoot(userHome);
        var candidates = SortCandidates(ScanGlobalCandidates(normalizedHubRoot, userHome, personalRoot));

        return Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "全局接管扫描已完成。",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Global,
                ProfileKind.Global,
                null,
                false,
                false,
                candidates.Select(item => item.Candidate).ToArray(),
                BuildPreviewSummary(candidates))));
    }

    private Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingCoreAsync(
        string hubRoot,
        string projectPath,
        ProfileKind profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedProjectPath = NormalizePath(projectPath);
        if (!Directory.Exists(normalizedProjectPath))
        {
            return Task.FromResult(WorkspaceOnboardingPreviewResult.Fail("项目目录不存在。", normalizedProjectPath));
        }

        var normalizedHubRoot = NormalizePath(hubRoot);
        var userHome = NormalizePath(_userHomeResolver());
        var personalRoot = LayeredWorkspaceMaterializer.GetPersonalRoot(userHome);
        var candidates = SortCandidates(ScanProjectCandidates(normalizedHubRoot, userHome, personalRoot, normalizedProjectPath, profile));

        return Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "项目接管扫描已完成。",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Project,
                profile,
                normalizedProjectPath,
                false,
                false,
                candidates.Select(item => item.Candidate).ToArray(),
                BuildPreviewSummary(candidates))));
    }

    private Task<OperationResult> ApplyGlobalLinksCoreAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capabilityResult = ValidateJunctionSupport();
        if (capabilityResult is not null)
        {
            return Task.FromResult(capabilityResult);
        }

        var normalizedHubRoot = NormalizePath(hubRoot);
        var userHome = NormalizePath(_userHomeResolver());
        var personalRoot = LayeredWorkspaceMaterializer.GetPersonalRoot(userHome);
        LayeredWorkspaceMaterializer.EnsurePrivateLayerStructure(personalRoot);

        if (importDecisions is { Count: > 0 })
        {
            var importResult = ExecuteImportDecisions(
                importDecisions,
                ScanGlobalCandidates(normalizedHubRoot, userHome, personalRoot),
                cancellationToken);
            if (!importResult.Success)
            {
                return Task.FromResult(importResult);
            }
        }

        var generateResult = RefreshEffectiveOutputs(
            normalizedHubRoot,
            personalRoot,
            allowPartialSuccess: true,
            ProfileKind.Global,
            ProfileKind.Frontend,
            ProfileKind.Backend);
        if (!generateResult.Success)
        {
            return Task.FromResult(generateResult);
        }

        LinkGlobalEntrypoints(normalizedHubRoot, userHome);

        _diagnosticLogService?.RecordInfo("workspace-automation", "已完成四层全局链接应用。", normalizedHubRoot + Environment.NewLine + userHome);
        return Task.FromResult(OperationResult.Ok(
            "全局链接已应用。",
            string.Join(Environment.NewLine, new[]
            {
                "用户目录：" + userHome,
                "全局有效输出：" + LayeredWorkspaceMaterializer.GetEffectiveProfileRoot(normalizedHubRoot, ProfileKind.Global),
                "Claude 设置：" + Path.Combine(userHome, ".claude", "settings.json"),
                "Claude MCP：" + Path.Combine(userHome, ".claude.json"),
                generateResult.Details
            }.Where(value => !string.IsNullOrWhiteSpace(value)))));
    }

    private Task<OperationResult> ApplyProjectProfileCoreAsync(
        string hubRoot,
        string projectPath,
        ProfileKind profile,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capabilityResult = ValidateJunctionSupport();
        if (capabilityResult is not null)
        {
            return Task.FromResult(capabilityResult);
        }

        var normalizedHubRoot = NormalizePath(hubRoot);
        var normalizedProjectPath = NormalizePath(projectPath);
        if (!Directory.Exists(normalizedProjectPath))
        {
            return Task.FromResult(OperationResult.Fail("项目目录不存在。", normalizedProjectPath));
        }

        var userHome = NormalizePath(_userHomeResolver());
        var personalRoot = LayeredWorkspaceMaterializer.GetPersonalRoot(userHome);
        LayeredWorkspaceMaterializer.EnsurePrivateLayerStructure(personalRoot);

        if (importDecisions is { Count: > 0 })
        {
            var importResult = ExecuteImportDecisions(
                importDecisions,
                ScanProjectCandidates(normalizedHubRoot, userHome, personalRoot, normalizedProjectPath, profile),
                cancellationToken);
            if (!importResult.Success)
            {
                return Task.FromResult(importResult);
            }
        }

        var generateResult = RefreshEffectiveOutputs(normalizedHubRoot, personalRoot, allowPartialSuccess: false, profile);
        if (!generateResult.Success)
        {
            return Task.FromResult(generateResult);
        }

        LinkProjectEntrypoints(normalizedHubRoot, normalizedProjectPath, profile);
        var effectiveRoot = LayeredWorkspaceMaterializer.GetEffectiveProfileRoot(normalizedHubRoot, profile);

        _diagnosticLogService?.RecordInfo("workspace-automation", "已完成四层项目 Profile 应用。", normalizedProjectPath + Environment.NewLine + profile.ToStorageValue());
        return Task.FromResult(OperationResult.Ok(
            "项目 Profile 已应用。",
            string.Join(Environment.NewLine, new[]
            {
                "项目目录：" + normalizedProjectPath,
                "Profile：" + profile.ToDisplayName(),
                "有效输出根目录：" + effectiveRoot,
                ".claude\\skills -> " + Path.Combine(effectiveRoot, "skills"),
                ".agents\\skills -> " + Path.Combine(effectiveRoot, "skills"),
                ".agent\\skills -> " + Path.Combine(effectiveRoot, "skills"),
                "Claude MCP：" + Path.Combine(normalizedProjectPath, ".mcp.json"),
                "Codex 配置：" + Path.Combine(normalizedProjectPath, ".codex", "config.toml"),
                generateResult.Details
            }.Where(value => !string.IsNullOrWhiteSpace(value)))));
    }

    private OperationResult? ValidateJunctionSupport()
    {
        var capability = _platformCapabilitiesService.Describe();
        return capability.SupportsJunctionLinks ? null : OperationResult.Fail(capability.Summary);
    }

    private static OperationResult RefreshEffectiveOutputs(string hubRoot, string personalRoot, bool allowPartialSuccess, params ProfileKind[] profiles)
    {
        var selectedProfiles = profiles
            .Distinct()
            .ToArray();
        var details = new List<string>();

        foreach (var profile in selectedProfiles)
        {
            try
            {
                var result = LayeredWorkspaceMaterializer.GenerateLegacyMcpOutputs(hubRoot, personalRoot, [profile]);
                if (!string.IsNullOrWhiteSpace(result.Details))
                {
                    details.Add(result.Details);
                }
            }
            catch (Exception ex)
            {
                if (!allowPartialSuccess || profile == ProfileKind.Global)
                {
                    return OperationResult.Fail($"刷新 {profile.ToDisplayName()} 有效输出失败。", ex.Message);
                }

                details.Add($"跳过 {profile.ToDisplayName()}：{ex.Message}");
            }
        }

        return OperationResult.Ok("有效输出已刷新。", string.Join(Environment.NewLine, details));
    }
}
