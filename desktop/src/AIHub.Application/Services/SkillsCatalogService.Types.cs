using AIHub.Contracts;

namespace AIHub.Application.Services;

public sealed partial class SkillsCatalogService
{
    private sealed record ResolvedSkillSource(
        string WorkingRootPath,
        string CatalogRootPath,
        string ResolvedReference);

    private sealed record DiscoveredSkill(
        string Name,
        string RelativePath,
        string SkillDirectory);

    private sealed record SkillInstallContext(
        string HubRoot,
        SkillInstallRecord Install,
        SkillInstallStateRecord State,
        SkillSourceRecord Source,
        string InstalledSkillDirectory,
        IReadOnlyList<SkillFileFingerprintRecord> InstalledFingerprints,
        bool IsDirty,
        ResolvedSkillSource ResolvedSource,
        string SourceSkillDirectory,
        IReadOnlyList<SkillFileFingerprintRecord> SourceFingerprints);

    private sealed record SkillContextResult(
        bool Success,
        SkillInstallContext? Context,
        OperationResult Result)
    {
        public static SkillContextResult Ok(SkillInstallContext context)
        {
            return new SkillContextResult(true, context, OperationResult.Ok("上下文已解析。"));
        }

        public static SkillContextResult Fail(OperationResult result)
        {
            return new SkillContextResult(false, null, result);
        }
    }

    private sealed record ProcessExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
