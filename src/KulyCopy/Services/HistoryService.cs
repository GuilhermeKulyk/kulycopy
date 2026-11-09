using System.Collections.ObjectModel;
using KulyCopy.Core;
using KulyCopy.Models;

namespace KulyCopy.Services;

/// <summary>
/// Coordena as abas e itens em memória (coleções observáveis para a UI) com a persistência em SQLite.
/// Toda mutação do histórico passa por aqui, tanto da interface quanto dos scripts.
/// </summary>
public sealed class HistoryService
{
    private readonly ItemStore _store;
    private readonly AppSettings _settings;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    /// <summary>Disparado quando um item realmente novo entra em uma aba.</summary>
    public event Action<TabViewModel, ClipboardItem>? ItemAdded;

    /// <summary>Qualquer alteração estrutural (para atualizar bandeja etc.).</summary>
    public event Action? Changed;

    public HistoryService(ItemStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    public void Load()
    {
        Tabs.Clear();
        foreach (var rec in _store.GetTabs())
        {
            var tab = new TabViewModel(rec);
            foreach (var item in _store.LoadItems(rec.Id))
                tab.Items.Add(new ClipboardItem(item));
            Tabs.Add(tab);
        }
        GetOrCreateTab(_settings.ClipboardTab);
    }

    // ---------------- Abas ----------------

    public TabViewModel ClipboardTab => GetOrCreateTab(_settings.ClipboardTab);

    public TabViewModel? FindTab(string name)
    {
        name = name.Trim();
        return Tabs.FirstOrDefault(t => t.Name == name)
               ?? Tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public TabViewModel GetOrCreateTab(string name)
    {
        name = name.Trim();
        if (name.Length == 0) name = _settings.ClipboardTab;
        return FindTab(name) ?? CreateTab(name);
    }

    public TabViewModel CreateTab(string name)
    {
        var rec = _store.CreateTab(name.Trim());
        var tab = new TabViewModel(rec);
        Tabs.Add(tab);
        Changed?.Invoke();
        return tab;
    }

    public bool RenameTab(TabViewModel tab, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return false;
        var existing = FindTab(newName);
        if (existing is not null && existing != tab) return false;
        _store.RenameTab(tab.Id, newName);
        tab.Name = newName;
        Changed?.Invoke();
        return true;
    }

    public void RemoveTab(TabViewModel tab)
    {
        _store.RemoveTab(tab.Id);
        Tabs.Remove(tab);
        Changed?.Invoke();
    }

    public void MoveTab(TabViewModel tab, int newIndex)
    {
        var old = Tabs.IndexOf(tab);
        if (old < 0 || newIndex < 0 || newIndex >= Tabs.Count || old == newIndex) return;
        Tabs.Move(old, newIndex);
        _store.SetTabOrder(Tabs.Select(t => t.Id).ToList());
        Changed?.Invoke();
    }

    public void SetTabIcon(TabViewModel tab, string? icon)
    {
        tab.Icon = string.IsNullOrWhiteSpace(icon) ? null : icon;
        _store.SetTabIcon(tab.Id, tab.Icon);
    }

    public void SetTabMaxItems(TabViewModel tab, int? max)
    {
        tab.MaxItems = max is > 0 ? max : null;
        _store.SetTabMaxItems(tab.Id, tab.MaxItems);
        Trim(tab);
    }

    // ---------------- Itens ----------------

    /// <summary>
    /// Adiciona um item. <paramref name="row"/> negativo insere no topo (logo após os itens fixados).
    /// Com <paramref name="dedupe"/>, um item de mesmo conteúdo na aba é reaproveitado (e movido para o topo).
    /// </summary>
    public ClipboardItem? AddItem(TabViewModel tab, Dictionary<string, byte[]> data, int row = -1, bool dedupe = true,
        string? notes = null, bool pinned = false, DateTimeOffset? created = null)
    {
        if (data.Remove(Mime.Notes, out var embeddedNotes) && notes is null)
            notes = DataUtil.Text(embeddedNotes);
        data.Remove(Mime.Owner);
        data.Remove(Mime.Hidden);
        foreach (var empty in data.Where(kv => kv.Value.Length == 0).Select(kv => kv.Key).ToList())
            data.Remove(empty);
        if (!data.Keys.Any(k => !Mime.IsInternal(k))) return null;

        var hash = DataUtil.ComputeHash(data);

        if (dedupe)
        {
            var existing = tab.Items.FirstOrDefault(i => i.Hash == hash);
            if (existing is not null)
            {
                if (_settings.MoveItemToTop) MoveToTop(tab, existing);
                existing.Touch(DateTimeOffset.Now);
                _store.SetCreated(existing.Id, existing.Created);
                if (data.TryGetValue(Mime.WindowTitle, out _) && !existing.Data.ContainsKey(Mime.WindowTitle))
                {
                    var merged = DataUtil.Clone(existing.Data);
                    merged[Mime.WindowTitle] = data[Mime.WindowTitle];
                    _store.ReplaceData(existing.Id, merged, hash);
                    existing.ReplaceData(merged, hash);
                }
                Changed?.Invoke();
                return existing;
            }
        }

        var index = row < 0 ? tab.FirstUnpinnedIndex() : Math.Clamp(row, 0, tab.Items.Count);
        var position = PositionFor(tab, index);
        var rec = _store.InsertItem(tab.Id, position, data, hash, pinned, notes, created ?? DateTimeOffset.Now);
        var item = new ClipboardItem(rec);
        tab.Items.Insert(index, item);
        Trim(tab);
        ItemAdded?.Invoke(tab, item);
        Changed?.Invoke();
        return item;
    }

    private double PositionFor(TabViewModel tab, int index)
    {
        var items = tab.Items;
        if (items.Count == 0) return 0;
        if (index <= 0) return items[0].Position - 1;
        if (index >= items.Count) return items[^1].Position + 1;
        var prev = items[index - 1].Position;
        var next = items[index].Position;
        var mid = (prev + next) / 2;
        if (mid <= prev || mid >= next)
        {
            Renumber(tab);
            return index - 0.5;
        }
        return mid;
    }

    private void Renumber(TabViewModel tab)
    {
        for (var i = 0; i < tab.Items.Count; i++) tab.Items[i].Position = i;
        _store.SetPositions(tab.Items.Select(i => (i.Id, i.Position)));
    }

    public void Trim(TabViewModel tab)
    {
        var max = tab.MaxItems ?? _settings.MaxItems;
        if (max < 1) max = 1;
        if (tab.Items.Count <= max) return;

        var removable = new List<ClipboardItem>();
        for (var i = tab.Items.Count - 1; i >= 0 && tab.Items.Count - removable.Count > max; i--)
            if (!tab.Items[i].Pinned) removable.Add(tab.Items[i]);
        if (removable.Count > 0) RemoveItems(tab, removable);
    }

    public void RemoveItems(TabViewModel tab, IEnumerable<ClipboardItem> items)
    {
        var list = items.Distinct().ToList();
        if (list.Count == 0) return;
        _store.RemoveItems(list.Select(i => i.Id));
        foreach (var item in list) tab.Items.Remove(item);
        Changed?.Invoke();
    }

    public void MoveItem(TabViewModel tab, ClipboardItem item, int newIndex)
    {
        var old = tab.Items.IndexOf(item);
        if (old < 0) return;
        newIndex = Math.Clamp(newIndex, 0, tab.Items.Count - 1);
        if (old == newIndex) return;

        tab.Items.Move(old, newIndex);
        item.Position = PositionBetweenNeighbors(tab, newIndex);
        _store.SetPosition(item.Id, item.Position);
        Changed?.Invoke();
    }

    private double PositionBetweenNeighbors(TabViewModel tab, int index)
    {
        var items = tab.Items;
        double? prev = index > 0 ? items[index - 1].Position : null;
        double? next = index < items.Count - 1 ? items[index + 1].Position : null;
        if (prev is null && next is null) return 0;
        if (prev is null) return next!.Value - 1;
        if (next is null) return prev.Value + 1;
        var mid = (prev.Value + next.Value) / 2;
        if (mid <= prev.Value || mid >= next.Value)
        {
            Renumber(tab);
            return items[index].Position;
        }
        return mid;
    }

    public void MoveToTop(TabViewModel tab, ClipboardItem item)
    {
        if (item.Pinned) return;
        var target = tab.FirstUnpinnedIndex();
        var old = tab.Items.IndexOf(item);
        if (old < 0 || old == target) return;
        MoveItem(tab, item, target);
    }

    public void MoveToBottom(TabViewModel tab, ClipboardItem item)
    {
        if (item.Pinned) return;
        MoveItem(tab, item, tab.Items.Count - 1);
    }

    public void SetPinned(TabViewModel tab, ClipboardItem item, bool pinned)
    {
        if (item.Pinned == pinned) return;
        item.Pinned = pinned;
        _store.SetPinned(item.Id, pinned);
        // Fixados ficam em bloco no topo: o alvo é o número de outros itens fixados.
        var target = tab.Items.Count(i => i.Pinned && i != item);
        MoveItem(tab, item, target);
        Changed?.Invoke();
    }

    public void SetNotes(ClipboardItem item, string? notes)
    {
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        item.Notes = notes;
        _store.SetNotes(item.Id, notes);
        Changed?.Invoke();
    }

    public void UpdateData(ClipboardItem item, Dictionary<string, byte[]> data)
    {
        if (data.Remove(Mime.Notes, out var notes)) SetNotes(item, DataUtil.Text(notes));
        data.Remove(Mime.Owner);
        foreach (var empty in data.Where(kv => kv.Value.Length == 0).Select(kv => kv.Key).ToList())
            data.Remove(empty);
        var hash = DataUtil.ComputeHash(data);
        _store.ReplaceData(item.Id, data, hash);
        item.ReplaceData(data, hash);
        Changed?.Invoke();
    }

    /// <summary>Ordena (ou inverte) os itens dados mantendo-os nas mesmas posições que ocupavam.</summary>
    public void SortItems(TabViewModel tab, IEnumerable<ClipboardItem> items, bool reverse)
    {
        var list = items.Distinct().Where(i => tab.Items.Contains(i)).ToList();
        if (list.Count < 2) return;

        var slots = list.Select(tab.Items.IndexOf).OrderBy(i => i).ToList();
        var byRow = list.OrderBy(tab.Items.IndexOf).ToList();
        var ordered = reverse
            ? Enumerable.Reverse(byRow).ToList()
            : byRow.OrderBy(i => i.Text ?? i.Preview, StringComparer.CurrentCultureIgnoreCase).ToList();

        var desired = tab.Items.ToList();
        for (var k = 0; k < slots.Count; k++) desired[slots[k]] = ordered[k];

        for (var i = 0; i < desired.Count; i++)
        {
            if (ReferenceEquals(tab.Items[i], desired[i])) continue;
            var from = tab.Items.IndexOf(desired[i]);
            tab.Items.Move(from, i);
        }
        Renumber(tab);
        Changed?.Invoke();
    }

    public void MoveItemsToTab(TabViewModel from, TabViewModel to, IEnumerable<ClipboardItem> items)
    {
        if (from == to) return;
        var ordered = items.Distinct().Where(i => from.Items.Contains(i)).OrderBy(from.Items.IndexOf).ToList();
        if (ordered.Count == 0) return;

        var index = to.FirstUnpinnedIndex();
        foreach (var item in Enumerable.Reverse(ordered))
        {
            from.Items.Remove(item);
            var duplicate = to.Items.FirstOrDefault(i => i.Hash == item.Hash);
            if (duplicate is not null)
            {
                _store.RemoveItems(new[] { item.Id });
                continue;
            }
            var pos = PositionFor(to, index);
            item.Position = pos;
            item.TabId = to.Id;
            _store.MoveToTab(item.Id, to.Id, pos);
            if (item.Pinned)
            {
                item.Pinned = false;
                _store.SetPinned(item.Id, false);
            }
            to.Items.Insert(index, item);
        }
        Trim(to);
        Changed?.Invoke();
    }

    public ClipboardItem? CopyItemToTab(ClipboardItem item, TabViewModel to) =>
        AddItem(to, item.DataWithNotes(), dedupe: true);

    // ---------------- Import / Export ----------------

    public ExportedTab Export(TabViewModel tab) => new()
    {
        Name = tab.Name,
        Items = tab.Items.Select(i => TabExport.ToExported(new ItemRecord
        {
            Created = i.Created,
            Pinned = i.Pinned,
            Notes = i.Notes,
            Data = i.Data.ToDictionary(kv => kv.Key, kv => kv.Value),
        })).ToList(),
    };

    public TabViewModel Import(ExportedTab exported, string? tabName = null)
    {
        var tab = GetOrCreateTab(string.IsNullOrWhiteSpace(tabName) ? exported.Name : tabName);
        var pinnedRow = 0;
        var row = tab.FirstUnpinnedIndex();
        foreach (var e in exported.Items)
        {
            var (data, pinned, notes, created) = TabExport.FromExported(e);
            var added = AddItem(tab, data, row: pinned ? pinnedRow : row, dedupe: false, notes: notes, pinned: pinned, created: created);
            if (added is null) continue;
            if (pinned) pinnedRow++;
            row++;
        }
        return tab;
    }
}
