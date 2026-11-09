using System.Text.RegularExpressions;

namespace KulyCopy.Core;

/// <summary>
/// Conversão entre o formato "HTML Format" do Windows (CF_HTML, com cabeçalho de offsets em bytes)
/// e o fragmento HTML puro que guardamos como text/html.
/// </summary>
public static class CfHtml
{
    private static readonly Regex HeaderValue = new(@"^(StartHTML|EndHTML|StartFragment|EndFragment):\s*(-?\d+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static bool LooksLikeCfHtml(string s) =>
        s.StartsWith("Version:", StringComparison.OrdinalIgnoreCase)
        && s.Contains("StartHTML:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extrai o fragmento (ou o HTML inteiro) de um CF_HTML. Se não parecer CF_HTML, retorna a entrada.</summary>
    public static string ExtractFragment(string cfHtml)
    {
        if (string.IsNullOrEmpty(cfHtml) || !LooksLikeCfHtml(cfHtml)) return cfHtml;

        var offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in HeaderValue.Matches(cfHtml))
            if (int.TryParse(m.Groups[2].Value, out var v)) offsets[m.Groups[1].Value] = v;

        var bytes = DataUtil.Utf8.GetBytes(cfHtml);

        if (TrySlice(bytes, offsets, "StartFragment", "EndFragment", out var frag) && frag.Contains('<'))
            return frag;
        if (TrySlice(bytes, offsets, "StartHTML", "EndHTML", out var html))
            return html;

        var idx = cfHtml.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = cfHtml.IndexOf("<!--StartFragment", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = cfHtml.IndexOf('<');
        return idx >= 0 ? cfHtml[idx..] : cfHtml;
    }

    private static bool TrySlice(byte[] bytes, Dictionary<string, int> offsets, string startKey, string endKey, out string result)
    {
        result = "";
        if (!offsets.TryGetValue(startKey, out var start) || !offsets.TryGetValue(endKey, out var end)) return false;
        if (start < 0 || end <= start || end > bytes.Length) return false;
        result = DataUtil.Utf8.GetString(bytes, start, end - start);
        return true;
    }

    /// <summary>Monta um CF_HTML válido (offsets em bytes UTF-8) a partir de um fragmento HTML.</summary>
    public static string Build(string fragment)
    {
        const string headerTemplate = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var headerLen = DataUtil.Utf8.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));

        string body;
        int startFragRel, endFragRel;

        const string startMarker = "<!--StartFragment-->";
        const string endMarker = "<!--EndFragment-->";
        var hasMarkers = fragment.Contains(startMarker, StringComparison.OrdinalIgnoreCase)
                         && fragment.Contains(endMarker, StringComparison.OrdinalIgnoreCase);

        if (hasMarkers)
        {
            body = fragment.Contains("<html", StringComparison.OrdinalIgnoreCase)
                ? fragment
                : "<html><body>\r\n" + fragment + "\r\n</body></html>";
            var s = body.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase) + startMarker.Length;
            var e = body.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
            startFragRel = DataUtil.Utf8.GetByteCount(body.AsSpan(0, s));
            endFragRel = DataUtil.Utf8.GetByteCount(body.AsSpan(0, e));
        }
        else
        {
            const string pre = "<html><body>\r\n<!--StartFragment-->";
            const string post = "<!--EndFragment-->\r\n</body></html>";
            body = pre + fragment + post;
            startFragRel = DataUtil.Utf8.GetByteCount(pre);
            endFragRel = startFragRel + DataUtil.Utf8.GetByteCount(fragment);
        }

        var startHtml = headerLen;
        var endHtml = headerLen + DataUtil.Utf8.GetByteCount(body);
        return string.Format(headerTemplate, startHtml, endHtml, headerLen + startFragRel, headerLen + endFragRel) + body;
    }
}
