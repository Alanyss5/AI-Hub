namespace AIHub.Contracts;

public sealed record SkillFileFingerprintRecord
{
    public string RelativePath { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long Size { get; init; }
}