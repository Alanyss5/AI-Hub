namespace AIHub.Application.Models;

public sealed record ConfigurationPackageImportPreview
{
    public string PackagePath { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public DateTimeOffset? ExportedAt { get; init; }

    public string PlannedBackupPath { get; init; } = string.Empty;

    public IReadOnlyList<string> IncludedSections { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReplaceTargets { get; init; } = Array.Empty<string>();

    public string Summary { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;
}