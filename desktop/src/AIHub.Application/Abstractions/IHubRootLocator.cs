using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IHubRootLocator
{
    Task<HubRootResolution> ResolveAsync(CancellationToken cancellationToken = default);

    Task<HubRootResolution> EvaluateAsync(string candidatePath, CancellationToken cancellationToken = default);

    void SetPreferredRoot(string? rootPath);

    string? GetPreferredRoot();
}
