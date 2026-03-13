namespace AIHub.Application.Models;
public sealed record HubReadinessItem(
    string Title,
    string Summary,
    bool IsComplete);