using AIHub.Application.Models;

namespace AIHub.Application.Abstractions;

public interface INotificationService
{
    Task NotifyAsync(MaintenanceAlertRecord alert, CancellationToken cancellationToken = default);
}
