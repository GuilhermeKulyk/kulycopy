using System.IO.Pipes;

namespace KulyCopy.Ipc;

/// <summary>Cliente de linha de comando: conecta no pipe da instância em execução e espera a resposta.</summary>
public static class IpcClient
{
    /// <summary>Envia o pedido; retorna null se não houver servidor escutando.</summary>
    public static IpcResponse? TrySend(IpcRequest request, int connectTimeoutMs = 700)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(connectTimeoutMs);
            client.ReadMode = PipeTransmissionMode.Byte;
            IpcProtocol.Write(client, request);
            return IpcProtocol.Read<IpcResponse>(client) ?? new IpcResponse { ExitCode = 1, Stderr = "Empty response from server" };
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
