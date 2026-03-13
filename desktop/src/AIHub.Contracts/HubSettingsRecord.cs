namespace AIHub.Contracts;

public sealed record HubSettingsRecord
{
    public string? HubRoot { get; init; } = ".";

    public ProfileKind DefaultProfile { get; init; } = ProfileKind.Global;

    public WorkspaceScope ActiveScope { get; init; } = WorkspaceScope.Global;

    public string[] PreferredClients { get; init; } = ["claude", "codex", "antigravity"];

    public bool AutoStartManagedMcpOnLoad { get; init; } = true;

    public bool AutoCheckSkillUpdatesOnLoad { get; init; } = true;

    public bool AutoSyncSafeSkillsOnLoad { get; init; }

    public string? LastOpenedProject { get; init; }

    public bool ScriptExecutionRiskAccepted { get; init; }

    public DateTimeOffset? ScriptExecutionRiskAcceptedAt { get; init; }

    public bool ManagedMcpRiskAccepted { get; init; }

    public DateTimeOffset? ManagedMcpRiskAcceptedAt { get; init; }

    public bool ExternalMcpImportRiskAccepted { get; init; }

    public DateTimeOffset? ExternalMcpImportRiskAcceptedAt { get; init; }
}