namespace KulyCopy.Core;

/// <summary>Registro de aba, como guardado no banco.</summary>
public sealed class TabRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public string? Icon { get; set; }
    public int? MaxItems { get; set; }
}

/// <summary>Registro de item, como guardado no banco. Position é REAL para inserções O(1).</summary>
public sealed class ItemRecord
{
    public long Id { get; set; }
    public long TabId { get; set; }
    public double Position { get; set; }
    public DateTimeOffset Created { get; set; }
    public string Hash { get; set; } = "";
    public bool Pinned { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, byte[]> Data { get; set; } = new();
}
