using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KulyCopy.Ipc;

/// <summary>Pedido enviado pelo cliente de linha de comando ao servidor (instância da GUI).</summary>
public sealed class IpcRequest
{
    public string[] Args { get; set; } = Array.Empty<string>();
    public string? Stdin { get; set; }
    public string? WorkingDirectory { get; set; }
}

public sealed class IpcResponse
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = "";
    /// <summary>Saída binária (ex.: imagem lida com read("image/png")), em base64.</summary>
    public string? StdoutBase64 { get; set; }
    public string Stderr { get; set; } = "";
}

/// <summary>Framing simples: 4 bytes little-endian com o tamanho + JSON em UTF-8.</summary>
internal static class IpcProtocol
{
    private const int MaxMessageBytes = 256 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Nome do pipe, único por usuário/sessão para não colidir com outras contas na mesma máquina.</summary>
    public static string PipeName
    {
        get
        {
            var id = Environment.UserDomainName + "\\" + Environment.UserName + "\\" + Environment.ProcessPath;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..16];
            return "KulyCopy-" + hash;
        }
    }

    public static void Write<T>(Stream stream, T message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var len = BitConverter.GetBytes(json.Length);
        stream.Write(len, 0, 4);
        stream.Write(json, 0, json.Length);
        stream.Flush();
    }

    public static T? Read<T>(Stream stream)
    {
        var lenBuf = ReadExactly(stream, 4);
        if (lenBuf is null) return default;
        var len = BitConverter.ToInt32(lenBuf, 0);
        if (len <= 0 || len > MaxMessageBytes) throw new InvalidDataException("Invalid IPC message length");
        var body = ReadExactly(stream, len);
        if (body is null) return default;
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static byte[]? ReadExactly(Stream stream, int count)
    {
        var buf = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buf, read, count - read);
            if (n <= 0) return null;
            read += n;
        }
        return buf;
    }
}
