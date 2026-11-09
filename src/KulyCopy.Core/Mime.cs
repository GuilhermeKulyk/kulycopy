namespace KulyCopy.Core;

/// <summary>
/// Nomes de formato (estilo MIME) usados para guardar os dados de cada item.
/// Os nomes "text/*" e "image/png" espelham o CopyQ; os internos usam o prefixo x-kulycopy.
/// </summary>
public static class Mime
{
    public const string Text = "text/plain";
    public const string Html = "text/html";
    public const string Rtf = "text/rtf";
    public const string UriList = "text/uri-list";
    public const string Png = "image/png";

    public const string Notes = "application/x-kulycopy-item-notes";
    public const string Owner = "application/x-kulycopy-owner";
    public const string WindowTitle = "application/x-kulycopy-owner-window-title";
    public const string Hidden = "application/x-kulycopy-hidden";

    /// <summary>Formatos internos que não contam como conteúdo (não entram no hash nem na prévia).</summary>
    public static bool IsInternal(string mime) =>
        mime == Owner || mime == WindowTitle || mime == Hidden;
}
