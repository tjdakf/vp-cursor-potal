using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace H2CursorRouter.App;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    private bool _disposed;

    // Acquire and dispose on the application thread: mutex ownership is thread-affine.
    public SingleInstanceService(string instanceName)
    {
        _pipeName = instanceName;
        _mutex = new Mutex(false, @"Local\" + instanceName);
        try
        {
            IsPrimaryInstance = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The previous process exited unexpectedly; we now own the mutex.
            IsPrimaryInstance = true;
        }
    }

    public bool IsPrimaryInstance { get; }

    public static string GetInstanceName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return $"vp-cursor-portal.{identity.User!.Value}.{process.SessionId}";
    }

    public void StartListening(Action activationRequested)
    {
        if (!IsPrimaryInstance || _listener is not null)
        {
            throw new InvalidOperationException("Only the primary instance can start the activation listener once.");
        }

        // Create the first server synchronously so startup cannot silently lose its listener.
        _listener = ListenAsync(CreateServer(), activationRequested);
    }

    public async Task<bool> NotifyPrimaryAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cancellation.Token).ConfigureAwait(false);
            var processId = new byte[sizeof(int)];
            await pipe.ReadExactlyAsync(processId, cancellation.Token).ConfigureAwait(false);
            AllowSetForegroundWindow(BitConverter.ToInt32(processId));
            await pipe.WriteAsync(new byte[] { 1 }, cancellation.Token).ConfigureAwait(false);
            var acknowledgement = new byte[1];
            await pipe.ReadExactlyAsync(acknowledgement, cancellation.Token).ConfigureAwait(false);
            return acknowledgement[0] == 1;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private NamedPipeServerStream CreateServer() => new(
        _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync(NamedPipeServerStream pipe, Action activationRequested)
    {
        using (pipe)
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var connected = false;
                try
                {
                    await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                    connected = true;
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    request.CancelAfter(TimeSpan.FromSeconds(3));
                    await pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), request.Token).ConfigureAwait(false);
                    var command = new byte[1];
                    await pipe.ReadExactlyAsync(command, request.Token).ConfigureAwait(false);
                    if (command[0] == 1)
                    {
                        activationRequested();
                        await pipe.WriteAsync(new byte[] { 1 }, request.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException)
                {
                    // A cancelled/closed duplicate launch must not stop later activation requests.
                }
                finally
                {
                    if (connected)
                    {
                        pipe.Disconnect();
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _shutdown.Cancel();
            _listener?.GetAwaiter().GetResult();
        }
        finally
        {
            _shutdown.Dispose();
            if (IsPrimaryInstance)
            {
                _mutex.ReleaseMutex();
            }

            _mutex.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
