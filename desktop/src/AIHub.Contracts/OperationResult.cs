namespace AIHub.Contracts;

public sealed record OperationResult(bool Success, string Message, string? Details = null)
{
    public static OperationResult Ok(string message, string? details = null)
    {
        return new OperationResult(true, message, details);
    }

    public static OperationResult Fail(string message, string? details = null)
    {
        return new OperationResult(false, message, details);
    }
}
