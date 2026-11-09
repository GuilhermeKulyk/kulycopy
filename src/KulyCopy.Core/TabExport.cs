using System.Text.Json;
using System.Text.Json.Serialization;

namespace KulyCopy.Core;

/// <summary>Item em formato de exportação: formatos de texto ficam legíveis, binários em base64.</summary>
public sealed class ExportedItem
{
    public DateTimeOffset Created { get; set; }
    public bool Pinned { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, string> Data { get; set; } = new();
}

public sealed class ExportedTab
{
    public string Format { get; set; } = "kulycopy-tab";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<ExportedItem> Items { get; set; } = new();
}

/// <summary>Importa/exporta abas em JSON (exportTab / importTab e menu Arquivo).</summary>
public static class TabExport
{
    private const string Base64Prefix = "base64:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string EncodeValue(string mime, byte[] data) =>
        DataUtil.IsTextMime(mime) ? EscapeText(DataUtil.Text(data)) : Base64Prefix + Convert.ToBase64String(data);

    public static byte[] DecodeValue(string value)
    {
        if (value.StartsWith(Base64Prefix, StringComparison.Ordinal))
        {
            try { return Convert.FromBase64String(value[Base64Prefix.Length..]); }
            catch (FormatException) { return DataUtil.Bytes(value); }
        }
        return DataUtil.Bytes(UnescapeText(value));
    }

    // Texto que comece com o prefixo literal "base64:" recebe um escape para não ser confundido.
    private static string EscapeText(string s) => s.StartsWith(Base64Prefix, StringComparison.Ordinal) ? "text:" + s : s;
    private static string UnescapeText(string s) => s.StartsWith("text:base64:", StringComparison.Ordinal) ? s[5..] : s;

    public static ExportedItem ToExported(ItemRecord item) => new()
    {
        Created = item.Created,
        Pinned = item.Pinned,
        Notes = item.Notes,
        Data = item.Data.Where(kv => !Mime.IsInternal(kv.Key))
            .ToDictionary(kv => kv.Key, kv => EncodeValue(kv.Key, kv.Value)),
    };

    public static (Dictionary<string, byte[]> data, bool pinned, string? notes, DateTimeOffset created) FromExported(ExportedItem e) =>
        (e.Data.ToDictionary(kv => kv.Key, kv => DecodeValue(kv.Value)), e.Pinned, e.Notes,
            e.Created == default ? DateTimeOffset.Now : e.Created);

    public static string Serialize(ExportedTab tab) => JsonSerializer.Serialize(tab, JsonOptions);

    public static ExportedTab Parse(string json) =>
        JsonSerializer.Deserialize<ExportedTab>(json, JsonOptions) ?? throw new InvalidDataException("Invalid tab file");

    public static void WriteFile(string path, ExportedTab tab) => File.WriteAllText(path, Serialize(tab));

    public static ExportedTab ReadFile(string path) => Parse(File.ReadAllText(path));
}
