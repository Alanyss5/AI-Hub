using AIHub.Application.Abstractions;
using AIHub.Contracts;

namespace AIHub.Infrastructure;

public sealed class LayeredMcpEffectiveConfigReader : IMcpEffectiveConfigReader
{
    private readonly Func<string> _userHomeResolver;

    public LayeredMcpEffectiveConfigReader(Func<string>? userHomeResolver = null)
    {
        _userHomeResolver = userHomeResolver ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public Task<IReadOnlyDictionary<string, McpServerDefinitionRecord>> GetEffectiveServersAsync(
        string hubRoot,
        string profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var personalRoot = LayeredWorkspaceMaterializer.GetPersonalRoot(_userHomeResolver());
        return Task.FromResult(LayeredWorkspaceMaterializer.BuildEffectiveServerMap(hubRoot, personalRoot, profile));
    }
}
