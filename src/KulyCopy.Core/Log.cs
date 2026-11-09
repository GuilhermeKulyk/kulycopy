using System.Text;

namespace KulyCopy.Core;

/// <summary>Log simples em arquivo + buffer em memória (para a janela "Show log" e a função logs()).</summary>
public static class Log
{
    private const int MaxLines = 2000;
    private static readonly object Lock = new();
    private static readonly LinkedList<string> Lines = new();
    private static string? _file;

    public static event Action<string>? LineAdded;

    public static void Init(string file)
    {
        _file = file;
        try
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Rotaciona quando passa de ~2 MB.
            if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024)
                File.Move(file, file + ".1", overwrite: true);
        }
        catch
        {
            // Sem log em disco; mantém só o buffer em memória.
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : message + ": " + ex);

    public static void Script(string message) => Write("SCRIPT", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Lock)
        {
            Lines.AddLast(line);
            while (Lines.Count > MaxLines) Lines.RemoveFirst();
            if (_file is not null)
            {
                try { File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8); }
                catch { /* disco indisponível; segue só em memória */ }
            }
        }
        LineAdded?.Invoke(line);
    }

    public static string[] Snapshot()
    {
        lock (Lock) return Lines.ToArray();
    }
}
