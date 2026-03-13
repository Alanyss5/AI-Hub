namespace AIHub.Contracts;

public sealed record SkillInstallStateRecord
{
    public ProfileKind Profile { get; init; } = ProfileKind.Global;

    public string InstalledRelativePath { get; init; } = string.Empty;

    public DateTimeOffset BaselineCapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public List<SkillFileFingerprintRecord> BaselineFiles { get; init; } = new();

    public List<SkillFileFingerprintRecord> SourceBaselineFiles { get; init; } = new();

    public List<string> OverlayDeletedFiles { get; init; } = new();

    public DateTimeOffset? LastSyncAt { get; init; }

    public DateTimeOffset? LastCheckedAt { get; init; }

    public string? LastAppliedReference { get; init; }

    public string? LastBackupPath { get; init; }
}
