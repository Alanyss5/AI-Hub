using AIHub.Contracts;

namespace AIHub.Application.Models;

public sealed record WorkspaceSnapshot(
    HubRootResolution Resolution,
    HubDashboardSnapshot Dashboard,
    HubSettingsRecord Settings,
    IReadOnlyList<ProjectRecord> Projects);
