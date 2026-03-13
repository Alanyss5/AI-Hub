using AIHub.Contracts;

namespace AIHub.Application.Abstractions;

public interface IHubSettingsStore
{
    Task<HubSettingsRecord> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(HubSettingsRecord settings, CancellationToken cancellationToken = default);
}
