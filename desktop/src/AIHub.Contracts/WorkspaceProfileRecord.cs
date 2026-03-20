namespace AIHub.Contracts;

public sealed record WorkspaceProfileRecord
{
    public string Id { get; init; } = WorkspaceProfiles.GlobalId;

    public string DisplayName { get; init; } = WorkspaceProfiles.GlobalDisplayName;

    public bool IsBuiltin { get; init; } = true;

    public bool IsDeletable { get; init; }

    public int SortOrder { get; init; }
}
