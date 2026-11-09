using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using KulyCopy.Core;

namespace KulyCopy.Clip;

public enum ClipboardWriteMode
{
    /// <summary>Sem marcador: o monitor trata como cópia vinda de fora.</summary>
    Normal,
    /// <summary>Marcado como nosso: guarda no histórico (dedupe/move para o topo) sem rodar comandos automáticos.</summary>
    Own,
    /// <summary>Marcado para o monitor ignorar completamente.</summary>
    Skip,
}

/// <summary>Escreve o dicionário mime → bytes na área de transferência do Windows.</summary>
public static class ClipboardWriter
{
    public static bool Write(IReadOnlyDictionary<string, byte[]> data, ClipboardWriteMode mode = ClipboardWriteMode.Own)
    {
        var obj = new DataObject();
        var any = false;
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (data.TryGetValue(Mime.Text, out var text))
        {
            obj.SetData(DataFormats.UnicodeText, DataUtil.Text(text), true);
            handled.Add(Mime.Text);
            any = true;
        }

        if (data.TryGetValue(Mime.Html, out var html))
        {
            var s = DataUtil.Text(html);
            obj.SetData(DataFormats.Html, CfHtml.LooksLikeCfHtml(s) ? s : CfHtml.Build(s));
            handled.Add(Mime.Html);
            any = true;
        }

        if (data.TryGetValue(Mime.Rtf, out var rtf))
        {
            obj.SetData(DataFormats.Rtf, DataUtil.Text(rtf));
            handled.Add(Mime.Rtf);
            any = true;
        }

        if (data.TryGetValue(Mime.Png, out var png))
        {
            try
            {
                var img = DecodeImage(png);
                if (img is not null) obj.SetImage(img);
                obj.SetData(ClipboardReader.PngFormat, new MemoryStream(png));
                any = true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not decode image for clipboard: " + ex.Message);
            }
            handled.Add(Mime.Png);
        }

        if (data.TryGetValue(Mime.UriList, out var uris))
        {
            var paths = DataUtil.UriListToPaths(DataUtil.Text(uris))
                .Where(p => File.Exists(p) || Directory.Exists(p))
                .ToArray();
            if (paths.Length > 0)
            {
                var sc = new StringCollection();
                sc.AddRange(paths);
                obj.SetFileDropList(sc);
                any = true;
            }
            handled.Add(Mime.UriList);
        }

        // Formatos extras (ex.: definidos por scripts) viajam como bytes crus sob o próprio nome.
        foreach (var kv in data)
        {
            if (handled.Contains(kv.Key) || Mime.IsInternal(kv.Key) || kv.Key == Mime.Notes) continue;
            try
            {
                obj.SetData(kv.Key, new MemoryStream(kv.Value));
                any = true;
            }
            catch { /* nome de formato inválido para o Windows; ignora */ }
        }

        if (!any) return false;

        if (mode != ClipboardWriteMode.Normal)
            obj.SetData(ClipboardReader.OwnerFormat, new MemoryStream(new byte[] { 1 }));
        if (mode == ClipboardWriteMode.Skip)
            obj.SetData(ClipboardReader.SkipFormat, new MemoryStream(new byte[] { 1 }));

        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(obj, true);
                return true;
            }
            catch (COMException) { Thread.Sleep(40); }
            catch (ExternalException) { Thread.Sleep(40); }
        }

        Log.Error("Could not open the clipboard for writing (another application is holding it)");
        return false;
    }

    public static bool WriteText(string text, ClipboardWriteMode mode = ClipboardWriteMode.Own) =>
        Write(DataUtil.FromText(text), mode);

    public static void Clear()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                System.Windows.Clipboard.Clear();
                return;
            }
            catch (COMException) { Thread.Sleep(40); }
            catch (ExternalException) { Thread.Sleep(40); }
        }
    }

    public static BitmapSource? DecodeImage(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) return null;
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
