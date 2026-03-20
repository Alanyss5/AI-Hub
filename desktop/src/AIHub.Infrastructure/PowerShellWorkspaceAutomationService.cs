using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class PowerShellWorkspaceAutomationService : IWorkspaceAutomationService
{
    private readonly IScriptExecutionService _scriptExecutionService;
    private readonly Func<string> _userHomeResolver;

    public PowerShellWorkspaceAutomationService(
        IDiagnosticLogService? diagnosticLogService = null,
        Func<string>? userHomeResolver = null)
        : this(new PowerShellScriptExecutionService(diagnosticLogService), userHomeResolver)
    {
    }

    public PowerShellWorkspaceAutomationService(
        IScriptExecutionService scriptExecutionService,
        Func<string>? userHomeResolver = null)
    {
        _scriptExecutionService = scriptExecutionService;
        _userHomeResolver = userHomeResolver
            ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(string hubRoot, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "Current PowerShell workflow does not support onboarding preview.",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Global,
                WorkspaceProfiles.GlobalId,
                null,
                false,
                false,
                Array.Empty<WorkspaceOnboardingCandidate>(),
                "Current automation implementation does not provide onboarding guidance.")));
    }

    public Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
        string hubRoot,
        string projectPath,
        string profile,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WorkspaceOnboardingPreviewResult.Ok(
            "Current PowerShell workflow does not support onboarding preview.",
            new WorkspaceOnboardingPreview(
                WorkspaceScope.Project,
                WorkspaceProfiles.NormalizeId(profile),
                projectPath,
                false,
                false,
                Array.Empty<WorkspaceOnboardingCandidate>(),
                "Current automation implementation does not provide onboarding guidance.")));
    }

    public async Task<OperationResult> ApplyGlobalLinksAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var userHome = Path.GetFullPath(_userHomeResolver());
        var refreshResult = RefreshEffectiveOutputs(
            normalizedHubRoot,
            allowPartialSuccess: true,
            LayeredWorkspaceMaterializer.GetKnownProfiles(normalizedHubRoot));
        if (!refreshResult.Success)
        {
            return refreshResult;
        }

        var scriptPath = Path.Combine(normalizedHubRoot, "scripts", "setup-global.ps1");
        return await _scriptExecutionService.RunAsync(
            scriptPath,
            ["-HubRoot", normalizedHubRoot, "-UserHome", userHome],
            "Executed global linking script.",
            "Global linking script failed.",
            cancellationToken);
    }

    public async Task<OperationResult> ApplyProjectProfileAsync(
        string hubRoot,
        string projectPath,
        string profile,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var normalizedProfile = WorkspaceProfiles.NormalizeId(profile);
        var refreshResult = RefreshEffectiveOutputs(
            normalizedHubRoot,
            allowPartialSuccess: false,
            [normalizedProfile]);
        if (!refreshResult.Success)
        {
            return refreshResult;
        }

        var scriptPath = Path.Combine(normalizedHubRoot, "scripts", "use-profile.ps1");
        return await _scriptExecutionService.RunAsync(
            scriptPath,
            ["-HubRoot", normalizedHubRoot, "-ProjectPath", normalizedProjectPath, "-Profile", normalizedProfile],
            "Executed project profile script.",
            "Project profile script failed.",
            cancellationToken);
    }

    private OperationResult RefreshEffectiveOutputs(string hubRoot, bool allowPartialSuccess, IEnumerable<string> profiles)
    {
        var normalizedHubRoot = Path.GetFullPath(hubRoot);
        var userHome = Path.GetFullPath(_userHomeResolver());
        var personalRoot = LayeredWorkspaceMaterializer.GetPersonalRoot(userHome);
        LayeredWorkspaceMaterializer.EnsurePrivateLayerStructure(normalizedHubRoot, personalRoot);

        var selectedProfiles = profiles
            .Select(WorkspaceProfiles.NormalizeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var details = new List<string>();

        foreach (var profile in selectedProfiles)
        {
            try
            {
                var result = LayeredWorkspaceMaterializer.GenerateLegacyMcpOutputs(normalizedHubRoot, personalRoot, [profile]);
                if (!string.IsNullOrWhiteSpace(result.Details))
                {
                    details.Add(result.Details);
                }
            }
            catch (Exception ex)
            {
                if (!allowPartialSuccess || WorkspaceProfiles.IsGlobal(profile))
                {
                    return OperationResult.Fail(
                        $"Failed to refresh effective output for {WorkspaceProfiles.ToDisplayName(profile)}.",
                        ex.Message);
                }

                details.Add($"Skipped {WorkspaceProfiles.ToDisplayName(profile)}: {ex.Message}");
            }
        }

        return OperationResult.Ok("Effective outputs refreshed.", string.Join(Environment.NewLine, details));
    }
}
