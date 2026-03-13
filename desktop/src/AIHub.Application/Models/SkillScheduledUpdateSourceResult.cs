namespace AIHub.Application.Models;

public sealed record SkillScheduledUpdateSourceResult(
    string SourceDisplayName,
    bool Success,
    bool HadWork,
    string Message,
    string? Details,
    MaintenanceAlertRecord? Alert);
