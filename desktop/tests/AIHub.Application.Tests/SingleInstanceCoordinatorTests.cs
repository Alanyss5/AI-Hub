using AIHub.Platform.Windows;

namespace AIHub.Application.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task First_Instance_Is_Primary_And_Second_Instance_Signals_Activation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var applicationId = $"AIHub.Test.{Guid.NewGuid():N}";
        using var primary = new WindowsSingleInstanceCoordinator(applicationId);
        using var secondary = new WindowsSingleInstanceCoordinator(applicationId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var activationReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(primary.IsPrimaryInstance);
        Assert.False(secondary.IsPrimaryInstance);

        var waitTask = Task.Run(() => primary.WaitForActivationAsync(() =>
        {
            activationReceived.TrySetResult();
            return Task.CompletedTask;
        }, cts.Token));

        Assert.True(secondary.TrySignalPrimaryInstance());
        await activationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Activation_Signaled_Before_Listener_Starts_Is_Not_Lost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var applicationId = $"AIHub.Test.{Guid.NewGuid():N}";
        using var primary = new WindowsSingleInstanceCoordinator(applicationId);
        using var secondary = new WindowsSingleInstanceCoordinator(applicationId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var activationReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(secondary.TrySignalPrimaryInstance());

        var waitTask = Task.Run(() => primary.WaitForActivationAsync(() =>
        {
            activationReceived.TrySetResult();
            return Task.CompletedTask;
        }, cts.Token));

        await activationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}