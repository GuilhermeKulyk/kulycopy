using System.Text;

namespace KulyCopy.Native;

/// <summary>
/// Um WinExe não tem console; para o modo CLI nos anexamos ao console do processo pai
/// (o terminal que chamou "kulycopy ..."), como o CopyQ faz no Windows.
/// </summary>
internal static class ConsoleHelper
{
    private static bool _attached;

    public static bool Attach()
    {
        if (_attached) return true;
        if (!NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS)) return false;
        _attached = true;
        try
        {
            var utf8 = new UTF8Encoding(false);
            Console.OutputEncoding = utf8;
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        }
        catch
        {
            // Sem console utilizável; as escritas viram no-op.
        }
        return true;
    }

    public static void WriteStdout(byte[] bytes)
    {
        if (!_attached) return;
        try
        {
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }
        catch { }
    }

    public static void WriteStdout(string text)
    {
        if (!_attached) return;
        try { Console.Out.Write(text); } catch { }
    }

    public static void WriteStderr(string text)
    {
        if (!_attached) return;
        try { Console.Error.Write(text); } catch { }
    }

    public static string? ReadStdinIfRedirected()
    {
        if (!_attached) return null;
        try
        {
            if (!Console.IsInputRedirected) return null;
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
