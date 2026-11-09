using System.IO.Pipes;
using System.Windows.Threading;
using KulyCopy.Core;

namespace KulyCopy.Ipc;

/// <summary>
/// Servidor de named pipe da instância principal. Cada pedido é executado na thread de UI
/// (via Dispatcher), porque os scripts mexem nas coleções ligadas à interface.
/// </summary>
public sealed class IpcServer : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<IpcRequest, IpcResponse> _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public IpcServer(Dispatcher dispatcher, Func<IpcRequest, IpcResponse> handler)
    {
        _dispatcher = dispatcher;
        _handler = handler;
    }

    public void Start()
    {
        _loop = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(IpcProtocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var s = server;
                server = null;
                _ = Task.Run(() => Handle(s), ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                Log.Error("IPC server error", ex);
                try { await Task.Delay(250, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    private void Handle(NamedPipeServerStream stream)
    {
        using (stream)
        {
            IpcResponse response;
            try
            {
                var request = IpcProtocol.Read<IpcRequest>(stream) ?? new IpcRequest();
                response = _dispatcher.Invoke(() =>
                {
                    try { return _handler(request); }
                    catch (Exception ex)
                    {
                        Log.Error("IPC handler failed", ex);
                        return new IpcResponse { ExitCode = 1, Stderr = ex.Message + Environment.NewLine };
                    }
                });
            }
            catch (Exception ex)
            {
                response = new IpcResponse { ExitCode = 1, Stderr = ex.Message + Environment.NewLine };
            }

            try
            {
                IpcProtocol.Write(stream, response);
                stream.WaitForPipeDrain();
            }
            catch (Exception ex)
            {
                Log.Warn("IPC client disconnected before reading the response: " + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            // Desbloqueia o WaitForConnection com uma conexão vazia.
            using var client = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut);
            client.Connect(100);
        }
        catch { }
        try { _loop?.Wait(500); } catch { }
        _cts.Dispose();
    }
}
