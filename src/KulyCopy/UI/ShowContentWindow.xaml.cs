using System.Text;
using System.Windows;
using System.Windows.Controls;
using KulyCopy.Clip;
using KulyCopy.Core;
using KulyCopy.Services;
using Microsoft.Win32;

namespace KulyCopy.UI;

/// <summary>Mostra todos os formatos de um item (ou da área de transferência), um por vez.</summary>
public partial class ShowContentWindow : Window
{
    private readonly IReadOnlyDictionary<string, byte[]> _data;

    public ShowContentWindow(string title, IReadOnlyDictionary<string, byte[]> data, string? notes = null)
    {
        InitializeComponent();
        Title = title;
        ThemeManager.ApplyTitleBar(this);

        var all = new Dictionary<string, byte[]>(data);
        if (!string.IsNullOrEmpty(notes)) all[Mime.Notes] = DataUtil.Bytes(notes);
        _data = all;

        foreach (var mime in _data.Keys.OrderBy(Rank).ThenBy(k => k, StringComparer.Ordinal))
            FormatBox.Items.Add(mime);
        if (FormatBox.Items.Count > 0) FormatBox.SelectedIndex = 0;
    }

    private static int Rank(string mime) => mime switch
    {
        Mime.Text => 0,
        Mime.Png => 1,
        Mime.Html => 2,
        Mime.UriList => 3,
        Mime.Rtf => 4,
        Mime.Notes => 5,
        _ => Mime.IsInternal(mime) ? 20 : 10,
    };

    private string? CurrentMime => FormatBox.SelectedItem as string;

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        var mime = CurrentMime;
        if (mime is null || !_data.TryGetValue(mime, out var bytes)) return;

        InfoText.Text = DataUtil.FormatSize(bytes.Length);

        if (mime == Mime.Png)
        {
            try
            {
                var img = ClipboardWriter.DecodeImage(bytes);
                ImageView.Source = img;
                if (img is not null) InfoText.Text += $" · {img.PixelWidth}x{img.PixelHeight}";
                TextView.Visibility = Visibility.Collapsed;
                ImageHost.Visibility = Visibility.Visible;
                return;
            }
            catch
            {
                // cai para a visão hexadecimal
            }
        }

        ImageHost.Visibility = Visibility.Collapsed;
        TextView.Visibility = Visibility.Visible;
        TextView.Text = DataUtil.IsTextMime(mime) ? DataUtil.Text(bytes) : HexDump(bytes, 4096);
    }

    private static string HexDump(byte[] bytes, int max)
    {
        var sb = new StringBuilder();
        var n = Math.Min(bytes.Length, max);
        for (var i = 0; i < n; i += 16)
        {
            sb.Append(i.ToString("X8")).Append("  ");
            for (var j = 0; j < 16; j++)
                sb.Append(i + j < n ? bytes[i + j].ToString("X2") + ' ' : "   ");
            sb.Append(' ');
            for (var j = 0; j < 16 && i + j < n; j++)
            {
                var c = (char)bytes[i + j];
                sb.Append(c is >= ' ' and <= '~' ? c : '.');
            }
            sb.AppendLine();
        }
        if (bytes.Length > max) sb.AppendLine($"... ({bytes.Length - max} more bytes)");
        return sb.ToString();
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var mime = CurrentMime;
        if (mime is null || !_data.TryGetValue(mime, out var bytes)) return;

        var ext = mime switch
        {
            Mime.Text => ".txt",
            Mime.Html => ".html",
            Mime.Rtf => ".rtf",
            Mime.Png => ".png",
            Mime.UriList => ".txt",
            _ => DataUtil.IsTextMime(mime) ? ".txt" : ".bin",
        };
        var dialog = new SaveFileDialog
        {
            FileName = "clipboard" + ext,
            Filter = "All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, bytes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyFormat(object sender, RoutedEventArgs e)
    {
        var mime = CurrentMime;
        if (mime is null || !_data.TryGetValue(mime, out var bytes)) return;
        var single = new Dictionary<string, byte[]> { [mime == Mime.Notes ? Mime.Text : mime] = bytes };
        ClipboardWriter.Write(single, ClipboardWriteMode.Own);
    }
}
