namespace AIHub.Application.Models;

public sealed record ConfigurationPackageImportPreviewResult(
    bool Success,
    string Message,
    string Details,
    ConfigurationPackageImportPreview? Preview);