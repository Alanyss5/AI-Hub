namespace AIHub.Contracts;

public sealed record WorkspaceOnboardingPreviewResult(
    bool Success,
    string Message,
    string? Details = null,
    WorkspaceOnboardingPreview? Preview = null)
{
    public static WorkspaceOnboardingPreviewResult Ok(
        string message,
        WorkspaceOnboardingPreview preview,
        string? details = null)
    {
        return new WorkspaceOnboardingPreviewResult(true, message, details, preview);
    }

    public static WorkspaceOnboardingPreviewResult Fail(string message, string? details = null)
    {
        return new WorkspaceOnboardingPreviewResult(false, message, details);
    }
}
