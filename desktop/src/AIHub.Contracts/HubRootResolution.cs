namespace AIHub.Contracts;

public sealed record HubRootResolution(
    string? RootPath,
    bool IsValid,
    string Source,
    IReadOnlyList<string> Errors);
