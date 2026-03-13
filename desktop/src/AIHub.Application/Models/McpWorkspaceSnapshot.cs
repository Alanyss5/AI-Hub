using AIHub.Contracts;

namespace AIHub.Application.Models;

public sealed record McpWorkspaceSnapshot(
    HubRootResolution Resolution,
    IReadOnlyList<McpProfileRecord> Profiles,
    IReadOnlyList<McpRuntimeRecord> ManagedProcesses,
    McpRuntimeSummary RuntimeSummary);
