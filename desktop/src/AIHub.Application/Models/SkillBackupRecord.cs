namespace AIHub.Application.Models;

public sealed record SkillBackupRecord
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public DateTimeOffset? CreatedAt { get; init; }

    public string DisplayName => CreatedAt.HasValue
        ? $"{Name} ({CreatedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss})"
        : Name;

    public string PathDisplay => string.IsNullOrWhiteSpace(Path) ? "未记录路径" : Path;
}