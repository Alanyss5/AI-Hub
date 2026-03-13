namespace AIHub.Core;

public sealed record HubValidationResult(bool IsValid, IReadOnlyList<string> Errors);
