using System.Threading;

namespace AIHub.Platform.Windows;

public sealed class WindowsSingleInstanceCoordinator : ISingleInstanceCoordinator
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private bool _disposed;

    public WindowsSingleInstanceCoordinator(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        var mutexName = $"Local\\{applicationId}.SingleInstance.Mutex";
        var activationEventName = $"Local\\{applicationId}.SingleInstance.Activation";

        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        _activationEvent = new EventWaitHandle(false, EventResetMode.ManualReset, activationEventName);
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public bool TrySignalPrimaryInstance()
    {
        ThrowIfDisposed();
        return _activationEvent.Set();
    }

    public async Task WaitForActivationAsync(Func<Task> onActivation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(onActivation);

        if (!IsPrimaryInstance)
        {
            return;
        }

        var waitHandles = new WaitHandle[]
        {
            _activationEvent,
            cancellationToken.WaitHandle
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaledIndex = WaitHandle.WaitAny(waitHandles);
            if (signaledIndex != 0)
            {
                break;
            }

            _activationEvent.Reset();
            await onActivation().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _activationEvent.Dispose();
        _mutex.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}