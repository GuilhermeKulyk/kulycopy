using System.Security.Cryptography;
using System.Text;

namespace KulyCopy.Core;

/// <summary>Funções utilitárias para o dicionário de dados (mime → bytes) de um item.</summary>
public static class DataUtil
{
    public static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static byte[] Bytes(string s) => Utf8.GetBytes(s);

    public static string Text(byte[] b) => Utf8.GetString(b);

    public static string? GetText(IReadOnlyDictionary<string, byte[]> data) =>
        data.TryGetValue(Mime.Text, out var b) ? Text(b) : null;

    public static Dictionary<string, byte[]> FromText(string text) =>
        new() { [Mime.Text] = Bytes(text) };

    public static bool IsTextMime(string mime)
    {
        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;
        if (mime == Mime.Notes || mime == Mime.WindowTitle || mime == Mime.Owner) return true;
        return mime.EndsWith("json", StringComparison.OrdinalIgnoreCase)
            || mime.EndsWith("xml", StringComparison.OrdinalIgnoreCase)
            || mime.EndsWith("javascript", StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, byte[]> Clone(IReadOnlyDictionary<string, byte[]> data)
    {
        var copy = new Dictionary<string, byte[]>(data.Count);
        foreach (var kv in data) copy[kv.Key] = (byte[])kv.Value.Clone();
        return copy;
    }

    /// <summary>
    /// Hash de conteúdo usado para detectar duplicatas. Prioriza o texto, depois imagem, depois arquivos;
    /// se nada disso existir, usa todos os formatos de conteúdo em ordem estável.
    /// </summary>
    public static string ComputeHash(IReadOnlyDictionary<string, byte[]> data)
    {
        if (data.TryGetValue(Mime.Text, out var t)) return "t:" + Hex(SHA256.HashData(t));
        if (data.TryGetValue(Mime.Png, out var p)) return "i:" + Hex(SHA256.HashData(p));
        if (data.TryGetValue(Mime.UriList, out var u)) return "u:" + Hex(SHA256.HashData(u));

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var key in data.Keys.Where(k => !Mime.IsInternal(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            sha.AppendData(Bytes(key));
            sha.AppendData(new byte[] { 0 });
            sha.AppendData(data[key]);
            sha.AppendData(new byte[] { 0 });
        }
        return "a:" + Hex(sha.GetHashAndReset());
    }

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>Converte uma lista "text/uri-list" (file:///...) em caminhos locais.</summary>
    public static IEnumerable<string> UriListToPaths(string uriList)
    {
        foreach (var raw in uriList.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile)
                yield return uri.LocalPath;
            else if (!line.Contains("://"))
                yield return line;
        }
    }

    public static string PathsToUriList(IEnumerable<string> paths) =>
        string.Join("\n", paths.Select(p => new Uri(p).AbsoluteUri));

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0.##") + " MB";
    }
}
