using KulyCopy.Core;

namespace KulyCopy.Scripting;

/// <summary>
/// Dados binários expostos aos scripts (equivalente ao ByteArray do CopyQ).
/// Evita que o Jint converta byte[] em um array JavaScript de números.
/// </summary>
public sealed class ByteArray
{
    public byte[] Bytes { get; }

    public ByteArray(byte[] bytes) => Bytes = bytes;

    // Nomes em minúsculas para parecerem naturais no JavaScript.
    public int length => Bytes.Length;

    public string toString() => DataUtil.Text(Bytes);

    public string toBase64() => Convert.ToBase64String(Bytes);

    public override string ToString() => DataUtil.Text(Bytes);
}

/// <summary>Lançada por abort(): encerra o script sem erro.</summary>
public sealed class ScriptAbortException : Exception
{
    public ScriptAbortException() : base("Script aborted") { }
}
