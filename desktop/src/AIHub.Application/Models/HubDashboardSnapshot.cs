namespace AIHub.Application.Models;
public sealed record HubDashboardSnapshot(
    string AppTitle,
    string? HubRoot,
    string HubStatus,
    int ProjectCount,
    IReadOnlyList<string> EnabledClients,
    IReadOnlyList<string> Modules,
    IReadOnlyList<HubReadinessItem> ReadinessItems,
    IReadOnlyList<HubReadinessItem> RemainingGates,
    IReadOnlyList<string> ValidationErrors);