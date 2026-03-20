using System.Text;
using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed partial class NativeWorkspaceAutomationService : IWorkspaceAutomationService
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

    public Task<WorkspaceOnboardingPreviewResult> PreviewGlobalOnboardingAsync(string hubRoot, CancellationToken cancellationToken = default)
        => PreviewGlobalOnboardingCoreAsync(hubRoot, cancellationToken);

    public Task<WorkspaceOnboardingPreviewResult> PreviewProjectOnboardingAsync(
        string hubRoot,
        string projectPath,
        ProfileKind profile,
        CancellationToken cancellationToken = default)
        => PreviewProjectOnboardingCoreAsync(hubRoot, projectPath, profile switch
        {
            ProfileKind.Global => WorkspaceProfiles.Global,
            ProfileKind.Frontend => WorkspaceProfiles.Frontend,
            ProfileKind.Backend => WorkspaceProfiles.Backend,
            _ => WorkspaceProfiles.Global
        }, cancellationToken);

    public Task<OperationResult> ApplyGlobalLinksAsync(
        string hubRoot,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
        => ApplyGlobalLinksCoreAsync(hubRoot, importDecisions, cancellationToken);

    public Task<OperationResult> ApplyProjectProfileAsync(
        string hubRoot,
        string projectPath,
        ProfileKind profile,
        IReadOnlyList<WorkspaceImportDecisionRecord>? importDecisions = null,
        CancellationToken cancellationToken = default)
        => ApplyProjectProfileCoreAsync(hubRoot, projectPath, profile switch
        {
            ProfileKind.Global => WorkspaceProfiles.Global,
            ProfileKind.Frontend => WorkspaceProfiles.Frontend,
            ProfileKind.Backend => WorkspaceProfiles.Backend,
            _ => WorkspaceProfiles.Global
        }, importDecisions, cancellationToken);

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
