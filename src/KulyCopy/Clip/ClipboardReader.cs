using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using KulyCopy.Core;

namespace KulyCopy.Clip;

/// <summary>Conteúdo lido da área de transferência em um dado momento.</summary>
public sealed class ClipboardSnapshot
{
    public Dictionary<string, byte[]> Data { get; } = new();

    /// <summary>Os dados foram colocados pelo próprio KulyCopy.</summary>
    public bool Own { get; set; }

    /// <summary>Marcador para o monitor ignorar completamente (ex.: ciclo "colar próximo").</summary>
    public bool Skip { get; set; }
}

/// <summary>Lê a área de transferência do Windows e converte para o dicionário mime → bytes.</summary>
public static class ClipboardReader
{
    public const string OwnerFormat = "KulyCopy.Owner";
    public const string SkipFormat = "KulyCopy.Skip";
    public const string PngFormat = "PNG";

    /// <summary>
    /// Lê a área de transferência. Com <paramref name="all"/> = true ignora as preferências de
    /// armazenamento e lê todos os formatos suportados (usado pelos scripts).
    /// </summary>
    public static ClipboardSnapshot? Read(AppSettings settings, bool all = false)
    {
        var obj = GetDataObjectWithRetry();
        if (obj is null) return null;

        var snap = new ClipboardSnapshot();
        string[] formats;
        try { formats = obj.GetFormats(false) ?? Array.Empty<string>(); }
        catch { return null; }

        snap.Own = formats.Contains(OwnerFormat);
        snap.Skip = formats.Contains(SkipFormat);

        var storeText = all || settings.StoreText;
        var storeHtml = all || settings.StoreHtml;
        var storeRtf = all || settings.StoreRtf;
        var storeFiles = all || settings.StoreFiles;
        var storeImages = all || settings.StoreImages;

        if (storeText)
        {
            var text = TryGet(obj, DataFormats.UnicodeText) as string ?? TryGet(obj, DataFormats.Text) as string;
            if (!string.IsNullOrEmpty(text)) snap.Data[Mime.Text] = DataUtil.Bytes(text);
        }

        if (storeHtml && Has(obj, DataFormats.Html))
        {
            var html = AsString(TryGet(obj, DataFormats.Html));
            if (!string.IsNullOrEmpty(html)) snap.Data[Mime.Html] = DataUtil.Bytes(CfHtml.ExtractFragment(html));
        }

        if (storeRtf && Has(obj, DataFormats.Rtf))
        {
            var rtf = AsString(TryGet(obj, DataFormats.Rtf));
            if (!string.IsNullOrEmpty(rtf)) snap.Data[Mime.Rtf] = DataUtil.Bytes(rtf);
        }

        if (storeFiles && Has(obj, DataFormats.FileDrop))
        {
            if (TryGet(obj, DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                snap.Data[Mime.UriList] = DataUtil.Bytes(DataUtil.PathsToUriList(files));
                if (!snap.Data.ContainsKey(Mime.Text))
                    snap.Data[Mime.Text] = DataUtil.Bytes(string.Join("\n", files));
            }
        }

        if (storeImages)
        {
            byte[]? png = null;
            if (Has(obj, PngFormat) && TryGet(obj, PngFormat) is MemoryStream pngStream)
                png = pngStream.ToArray();

            if (png is null && (Has(obj, DataFormats.Bitmap) || Has(obj, DataFormats.Dib)))
            {
                try
                {
                    var img = TryGet(obj, DataFormats.Bitmap) as BitmapSource;
                    if (img is { PixelWidth: > 0, PixelHeight: > 0 }) png = EncodePng(img);
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not read clipboard image: " + ex.Message);
                }
            }

            if (png is { Length: > 0 } && (all || png.Length <= (long)settings.MaxImageSizeKb * 1024))
                snap.Data[Mime.Png] = png;
        }

        // Formatos internos próprios (bytes crus), para scripts que escrevem formatos arbitrários.
        if (all)
        {
            foreach (var f in formats)
            {
                if (f.Contains('/') && !snap.Data.ContainsKey(f) && TryGet(obj, f) is MemoryStream ms)
                    snap.Data[f] = ms.ToArray();
            }
        }

        return snap;
    }

    public static IDataObject? GetDataObjectWithRetry()
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try { return System.Windows.Clipboard.GetDataObject(); }
            catch (COMException) { Thread.Sleep(25); }
            catch (ExternalException) { Thread.Sleep(25); }
        }
        return null;
    }

    private static bool Has(IDataObject obj, string format)
    {
        try { return obj.GetDataPresent(format, false); }
        catch { return false; }
    }

    private static object? TryGet(IDataObject obj, string format)
    {
        try { return obj.GetData(format, true); }
        catch { return null; }
    }

    private static string? AsString(object? value) => value switch
    {
        string s => s,
        MemoryStream ms => DataUtil.Utf8.GetString(ms.ToArray()).TrimEnd('\0'),
        _ => null,
    };

    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
