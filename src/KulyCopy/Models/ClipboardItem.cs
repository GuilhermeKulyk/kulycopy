using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KulyCopy.Core;

namespace KulyCopy.Models;

/// <summary>Item do histórico como visto pela interface (observável, com prévia e miniatura).</summary>
public sealed class ClipboardItem : INotifyPropertyChanged
{
    /// <summary>Linhas máximas da prévia; ajustado a partir das configurações.</summary>
    public static int MaxPreviewLines { get; set; } = 6;

    private const int MaxPreviewChars = 700;
    private const int ThumbnailWidth = 320;

    private Dictionary<string, byte[]> _data;
    private int _row;
    private string? _text;
    private string? _preview;
    private bool _pinned;
    private string? _notes;
    private ImageSource? _thumbnail;
    private bool _thumbnailTried;
    private (int w, int h)? _imageSize;

    public long Id { get; }
    public long TabId { get; set; }
    public double Position { get; set; }
    public DateTimeOffset Created { get; private set; }
    public string Hash { get; private set; }

    public ClipboardItem(ItemRecord record)
    {
        Id = record.Id;
        TabId = record.TabId;
        Position = record.Position;
        Created = record.Created;
        Hash = record.Hash;
        _data = record.Data;
        _pinned = record.Pinned;
        _notes = record.Notes;
    }

    public IReadOnlyDictionary<string, byte[]> Data => _data;

    /// <summary>Posição (0 = topo) dentro da aba, mantida por TabViewModel; exibida na lista.</summary>
    public int Row
    {
        get => _row;
        internal set
        {
            if (_row == value) return;
            _row = value;
            OnPropertyChanged();
        }
    }

    public string? Text => _text ??= DataUtil.GetText(_data);

    public bool HasText => _data.ContainsKey(Mime.Text);
    public bool HasImage => _data.ContainsKey(Mime.Png);
    public bool HasFiles => _data.ContainsKey(Mime.UriList);
    public bool HasHtml => _data.ContainsKey(Mime.Html);
    public bool HasNotes => !string.IsNullOrEmpty(_notes);

    /// <summary>Mostra a miniatura quando há imagem e não há texto (imagem com texto mostra só o texto).</summary>
    public bool ShowImage => HasImage && !HasText;
    public bool ShowText => !ShowImage;

    public string? WindowTitle =>
        _data.TryGetValue(Mime.WindowTitle, out var t) ? DataUtil.Text(t) : null;

    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value) return;
            _pinned = value;
            OnPropertyChanged();
        }
    }

    public string? Notes
    {
        get => _notes;
        set
        {
            if (_notes == value) return;
            _notes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasNotes));
        }
    }

    /// <summary>Texto curto para a lista: primeiras linhas, sem espaços excessivos.</summary>
    public string Preview
    {
        get
        {
            if (_preview is not null) return _preview;
            var text = Text;
            if (!string.IsNullOrEmpty(text))
            {
                var lines = text.Replace("\r\n", "\n").Split('\n');
                var taken = lines.Take(MaxPreviewLines).Select(l => l.TrimEnd()).ToList();
                var joined = string.Join("\n", taken).TrimEnd();
                if (joined.Length > MaxPreviewChars) joined = joined[..MaxPreviewChars] + "…";
                else if (lines.Length > taken.Count) joined += "\n…";
                _preview = joined.Length == 0 ? "(whitespace)" : joined;
            }
            else if (HasFiles)
            {
                var paths = DataUtil.UriListToPaths(DataUtil.Text(_data[Mime.UriList])).ToList();
                _preview = string.Join("\n", paths.Take(MaxPreviewLines).Select(Path.GetFileName));
                if (paths.Count > MaxPreviewLines) _preview += "\n…";
            }
            else if (HasImage)
            {
                _preview = "";
            }
            else
            {
                _preview = "<" + string.Join(", ", _data.Keys.Where(k => !Mime.IsInternal(k))) + ">";
            }
            return _preview;
        }
    }

    /// <summary>Linha única para menus (bandeja) e títulos.</summary>
    public string SingleLine(int maxChars = 60)
    {
        string s;
        var text = Text;
        if (!string.IsNullOrEmpty(text))
        {
            s = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
        }
        else if (HasImage)
        {
            var size = ImageSize;
            s = size is null ? "[image]" : $"[image {size.Value.w}x{size.Value.h}]";
        }
        else if (HasFiles) s = Preview.Replace("\n", ", ");
        else s = Preview;

        if (s.Length > maxChars) s = s[..(maxChars - 1)] + "…";
        return s.Length == 0 ? "(empty)" : s;
    }

    /// <summary>Resumo do conteúdo (tipo e tamanho) usado em janelas de detalhes.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (HasText) parts.Add($"text ({Text!.Length} chars)");
            if (HasImage)
            {
                var size = ImageSize;
                parts.Add(size is null ? "image" : $"image {size.Value.w}x{size.Value.h}");
            }
            if (HasFiles) parts.Add($"{DataUtil.UriListToPaths(DataUtil.Text(_data[Mime.UriList])).Count()} file(s)");
            if (HasHtml) parts.Add("html");
            var total = _data.Where(kv => !Mime.IsInternal(kv.Key)).Sum(kv => (long)kv.Value.Length);
            parts.Add(DataUtil.FormatSize(total));
            return string.Join(" · ", parts);
        }
    }

    public (int w, int h)? ImageSize
    {
        get
        {
            if (_imageSize is not null || !HasImage) return _imageSize;
            try
            {
                using var ms = new MemoryStream(_data[Mime.Png]);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var f = decoder.Frames[0];
                _imageSize = (f.PixelWidth, f.PixelHeight);
            }
            catch
            {
                _imageSize = null;
            }
            return _imageSize;
        }
    }

    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnailTried || !HasImage) return _thumbnail;
            _thumbnailTried = true;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(_data[Mime.Png]);
                var size = ImageSize;
                if (size is { w: > ThumbnailWidth }) bmp.DecodePixelWidth = ThumbnailWidth;
                bmp.EndInit();
                bmp.Freeze();
                _thumbnail = bmp;
            }
            catch (Exception ex)
            {
                Log.Warn("Thumbnail decode failed: " + ex.Message);
            }
            return _thumbnail;
        }
    }

    public BitmapSource? FullImage()
    {
        if (!HasImage) return null;
        try { return Clip.ClipboardWriter.DecodeImage(_data[Mime.Png]); }
        catch { return null; }
    }

    public string CreatedText => Created.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Substitui os dados (edição); invalida caches e notifica a UI.</summary>
    internal void ReplaceData(Dictionary<string, byte[]> data, string hash)
    {
        _data = data;
        Hash = hash;
        _text = null;
        _preview = null;
        _thumbnail = null;
        _thumbnailTried = false;
        _imageSize = null;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Recalcula a prévia (ex.: após mudar o número de linhas nas preferências).</summary>
    public void InvalidatePreview()
    {
        _preview = null;
        OnPropertyChanged(nameof(Preview));
    }

    internal void Touch(DateTimeOffset created)
    {
        Created = created;
        OnPropertyChanged(nameof(Created));
        OnPropertyChanged(nameof(CreatedText));
    }

    /// <summary>Dicionário completo incluindo notas, como exposto aos scripts.</summary>
    public Dictionary<string, byte[]> DataWithNotes()
    {
        var d = DataUtil.Clone(_data);
        if (HasNotes) d[Mime.Notes] = DataUtil.Bytes(_notes!);
        return d;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
