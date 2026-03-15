using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class NativeMcpAutomationService : IMcpAutomationService
{
    private readonly Func<string> _userHomeResolver;

    public NativeMcpAutomationService(Func<string>? userHomeResolver = null)
    {
        _userHomeResolver = userHomeResolver ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public Task<OperationResult> GenerateConfigsAsync(string hubRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var personalRoot = LayeredWorkspaceMaterializer.GetPersonalRoot(_userHomeResolver());
        return Task.FromResult(LayeredWorkspaceMaterializer.GenerateLegacyMcpOutputs(hubRoot, personalRoot));
    }
}
