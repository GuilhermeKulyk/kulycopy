using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Data;
using KulyCopy.Core;

namespace KulyCopy.Models;

/// <summary>Aba do histórico: coleção observável de itens + view filtrada para a lista.</summary>
public sealed class TabViewModel : INotifyPropertyChanged
{
    private string _name;
    private string? _icon;
    private int? _maxItems;
    private string _filterText = "";
    private Regex? _filterRegex;

    public long Id { get; }

    public ObservableCollection<ClipboardItem> Items { get; } = new();

    /// <summary>View ligada à lista; aplica o filtro de busca sem alterar a coleção.</summary>
    public ICollectionView View { get; }

    public TabViewModel(TabRecord record)
    {
        Id = record.Id;
        _name = record.Name;
        _icon = record.Icon;
        _maxItems = record.MaxItems;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = FilterItem;
        Items.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Count));

        // Mantém ClipboardItem.Row sincronizado só a partir do ponto que mudou.
        var from = e.Action switch
        {
            System.Collections.Specialized.NotifyCollectionChangedAction.Add => e.NewStartingIndex,
            System.Collections.Specialized.NotifyCollectionChangedAction.Remove => e.OldStartingIndex,
            System.Collections.Specialized.NotifyCollectionChangedAction.Move => Math.Min(e.OldStartingIndex, e.NewStartingIndex),
            _ => 0,
        };
        if (from < 0) from = 0;
        for (var i = from; i < Items.Count; i++) Items[i].Row = i;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public string? Icon
    {
        get => _icon;
        set
        {
            if (_icon == value) return;
            _icon = value;
            OnPropertyChanged();
        }
    }

    public int? MaxItems
    {
        get => _maxItems;
        set
        {
            if (_maxItems == value) return;
            _maxItems = value;
            OnPropertyChanged();
        }
    }

    public int Count => Items.Count;

    public string FilterText
    {
        get => _filterText;
        set
        {
            value ??= "";
            if (_filterText == value) return;
            _filterText = value;
            _filterRegex = BuildRegex(value);
            View.Refresh();
            OnPropertyChanged();
        }
    }

    private static Regex? BuildRegex(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return new Regex(Regex.Escape(text), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
    }

    private bool FilterItem(object obj)
    {
        if (_filterRegex is null) return true;
        if (obj is not ClipboardItem item) return false;
        try
        {
            if (item.Text is { } text && _filterRegex.IsMatch(text)) return true;
            if (item.Notes is { } notes && _filterRegex.IsMatch(notes)) return true;
            if (!item.HasText && _filterRegex.IsMatch(item.Preview)) return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        return false;
    }

    /// <summary>Itens na ordem da view (após filtro).</summary>
    public IEnumerable<ClipboardItem> VisibleItems => View.Cast<ClipboardItem>();

    public int IndexOf(ClipboardItem item) => Items.IndexOf(item);

    /// <summary>Índice do primeiro item não fixado (novos itens entram aqui).</summary>
    public int FirstUnpinnedIndex()
    {
        var i = 0;
        while (i < Items.Count && Items[i].Pinned) i++;
        return i;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => Name;
}
