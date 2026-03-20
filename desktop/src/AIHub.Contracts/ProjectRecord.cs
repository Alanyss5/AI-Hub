namespace AIHub.Contracts;

public sealed record ProjectRecord(
    string Name,
    string Path,
    string Profile,
    bool IsPinned = false);
