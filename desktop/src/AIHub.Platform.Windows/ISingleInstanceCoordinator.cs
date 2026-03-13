namespace AIHub.Platform.Windows;

public interface ISingleInstanceCoordinator : IDisposable
{
    bool IsPrimaryInstance { get; }

    bool TrySignalPrimaryInstance();

    Task WaitForActivationAsync(Func<Task> onActivation, CancellationToken cancellationToken);
}