namespace AIHub.Application.Models;

public sealed record MaintenanceAlertRecord(
    string Key,
    string Title,
    string Message,
    string? Details = null);
