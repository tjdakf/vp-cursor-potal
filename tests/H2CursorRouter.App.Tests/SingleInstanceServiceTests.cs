using System.IO.Pipes;
using System.Threading;
using H2CursorRouter.App;
using Xunit;

namespace H2CursorRouter.App.Tests;

// Keep mutex acquisition/disposal on one thread. Worker tasks do not use the test synchronization context.
#pragma warning disable xUnit1031
public sealed class SingleInstanceServiceTests
{
    [Fact]
    public void DuplicateIsRejectedAndOwnershipIsReleasedOnExit()
    {
        var name = NewInstanceName();
        using (var primary = new SingleInstanceService(name))
        {
            Assert.True(primary.IsPrimaryInstance);
            primary.StartListening(() => { });
            // A different thread represents another process; mutexes are reentrant on one thread.
            Assert.False(RunOnDedicatedThread(() =>
            {
                using var duplicate = new SingleInstanceService(name);
                return duplicate.IsPrimaryInstance;
            }).GetAwaiter().GetResult());
        }

        using var restarted = new SingleInstanceService(name);
        Assert.True(restarted.IsPrimaryInstance);
        restarted.StartListening(() => { });
    }

    [Fact]
    public void AbandonedOwnershipCanBeRecovered()
    {
        var name = NewInstanceName();
        using var abandoned = new Mutex(false, @"Local\" + name);
        var thread = new Thread(() => abandoned.WaitOne());
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

        using var restarted = new SingleInstanceService(name);
        Assert.True(restarted.IsPrimaryInstance);
    }

    [Fact]
    public void DuplicateRequestsActivationOfPrimary()
    {
        var name = NewInstanceName();
        using var primary = new SingleInstanceService(name);
        using var activated = new ManualResetEventSlim();
        primary.StartListening(activated.Set);

        var notified = Task.Run(async () =>
        {
            // A duplicate owns no mutex, so disposal after an await is safe.
            using var duplicate = new SingleInstanceService(name);
            Assert.False(duplicate.IsPrimaryInstance);
            return await duplicate.NotifyPrimaryAsync(TimeSpan.FromSeconds(5));
        }).GetAwaiter().GetResult();

        Assert.True(notified);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ActivationWaitsForPrimaryStartup()
    {
        var name = NewInstanceName();
        using var primary = new SingleInstanceService(name);
        using var connecting = new ManualResetEventSlim();
        using var activated = new ManualResetEventSlim();
        var notification = Task.Run(async () =>
        {
            using var duplicate = new SingleInstanceService(name);
            var result = duplicate.NotifyPrimaryAsync(TimeSpan.FromSeconds(5));
            connecting.Set();
            return await result;
        });

        Assert.True(connecting.Wait(TimeSpan.FromSeconds(5)));
        primary.StartListening(activated.Set);

        Assert.True(notification.GetAwaiter().GetResult());
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DisconnectedClientDoesNotStopLaterActivationRequests()
    {
        var name = NewInstanceName();
        using var primary = new SingleInstanceService(name);
        var activations = 0;
        primary.StartListening(() => Interlocked.Increment(ref activations));

        Task.Run(async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using (var interrupted = new NamedPipeClientStream(
                ".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await interrupted.ConnectAsync(cancellation.Token);
                await interrupted.ReadExactlyAsync(new byte[sizeof(int)], cancellation.Token);
                // Close before sending the activation command.
            }

            await RequestActivationAsync(name, cancellation.Token);
            await RequestActivationAsync(name, cancellation.Token);
        }).GetAwaiter().GetResult();

        Assert.Equal(2, Volatile.Read(ref activations));
    }

    [Fact]
    public void UnresponsivePrimaryTimesOutWithoutGrantingOwnership()
    {
        var name = NewInstanceName();
        using var primary = new SingleInstanceService(name);
        Task.Run(async () =>
        {
            using var duplicate = new SingleInstanceService(name);
            Assert.False(duplicate.IsPrimaryInstance);
            Assert.False(await duplicate.NotifyPrimaryAsync(TimeSpan.FromMilliseconds(100)));
            Assert.False(duplicate.IsPrimaryInstance);
        }).GetAwaiter().GetResult();
    }

    private static string NewInstanceName() => $"vpc-test-{Guid.NewGuid():N}";

    private static Task<T> RunOnDedicatedThread<T>(Func<T> action)
    {
        // Synchronously waiting on Task.Run can inline its work on the mutex-owning thread.
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }) { IsBackground = true };
        thread.Start();
        return completion.Task;
    }

    private static async Task RequestActivationAsync(string name, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);
        var processId = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(processId, cancellationToken);
        Assert.Equal(Environment.ProcessId, BitConverter.ToInt32(processId));
        await pipe.WriteAsync(new byte[] { 1 }, cancellationToken);
        var acknowledgement = new byte[1];
        await pipe.ReadExactlyAsync(acknowledgement, cancellationToken);
        Assert.Equal(1, acknowledgement[0]);
    }
}
#pragma warning restore xUnit1031
