namespace AIHub.Contracts;

public sealed record ProjectRecord(
    string Name,
    string Path,
    ProfileKind Profile,
    bool IsPinned = false);
