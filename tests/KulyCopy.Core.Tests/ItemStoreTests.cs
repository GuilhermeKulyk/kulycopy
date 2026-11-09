using KulyCopy.Core;
using Xunit;

namespace KulyCopy.Core.Tests;

public sealed class ItemStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kulycopy-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ItemStore _store;

    public ItemStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new ItemStore(Path.Combine(_dir, "history.db"));
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Dictionary<string, byte[]> Text(string s) => DataUtil.FromText(s);

    [Fact]
    public void Creates_tabs_in_order_and_renames()
    {
        var a = _store.CreateTab("clipboard");
        var b = _store.CreateTab("Notes");
        _store.RenameTab(b.Id, "Tasks");

        var tabs = _store.GetTabs();
        Assert.Equal(new[] { "clipboard", "Tasks" }, tabs.Select(t => t.Name));
        Assert.Equal(a.Id, tabs[0].Id);
    }

    [Fact]
    public void Items_are_loaded_ordered_by_position_with_data()
    {
        var tab = _store.CreateTab("t");
        _store.InsertItem(tab.Id, 2, Text("c"), "hc", false, null, DateTimeOffset.Now);
        _store.InsertItem(tab.Id, 0, Text("a"), "ha", true, "note a", DateTimeOffset.Now);
        _store.InsertItem(tab.Id, 1, Text("b"), "hb", false, null, DateTimeOffset.Now);

        var items = _store.LoadItems(tab.Id);
        Assert.Equal(new[] { "a", "b", "c" }, items.Select(i => DataUtil.GetText(i.Data)));
        Assert.True(items[0].Pinned);
        Assert.Equal("note a", items[0].Notes);
        Assert.Equal("ha", items[0].Hash);
    }

    [Fact]
    public void Positions_can_be_fractional_and_renumbered()
    {
        var tab = _store.CreateTab("t");
        var a = _store.InsertItem(tab.Id, 0, Text("a"), "ha", false, null, DateTimeOffset.Now);
        var c = _store.InsertItem(tab.Id, 1, Text("c"), "hc", false, null, DateTimeOffset.Now);
        var b = _store.InsertItem(tab.Id, 0.5, Text("b"), "hb", false, null, DateTimeOffset.Now);

        Assert.Equal(new[] { "a", "b", "c" }, _store.LoadItems(tab.Id).Select(i => DataUtil.GetText(i.Data)));

        _store.SetPositions(new[] { (c.Id, 0.0), (b.Id, 1.0), (a.Id, 2.0) });
        Assert.Equal(new[] { "c", "b", "a" }, _store.LoadItems(tab.Id).Select(i => DataUtil.GetText(i.Data)));
    }

    [Fact]
    public void ReplaceData_swaps_formats_and_hash()
    {
        var tab = _store.CreateTab("t");
        var item = _store.InsertItem(tab.Id, 0, new Dictionary<string, byte[]>
        {
            [Mime.Text] = DataUtil.Bytes("x"),
            [Mime.Html] = DataUtil.Bytes("<b>x</b>"),
        }, "h1", false, null, DateTimeOffset.Now);

        _store.ReplaceData(item.Id, Text("y"), "h2");

        var loaded = _store.LoadItems(tab.Id).Single();
        Assert.Equal("y", DataUtil.GetText(loaded.Data));
        Assert.False(loaded.Data.ContainsKey(Mime.Html));
        Assert.Equal("h2", loaded.Hash);
    }

    [Fact]
    public void Removing_tab_cascades_to_items_and_data()
    {
        var tab = _store.CreateTab("t");
        _store.InsertItem(tab.Id, 0, Text("a"), "ha", false, null, DateTimeOffset.Now);
        _store.InsertItem(tab.Id, 1, Text("b"), "hb", false, null, DateTimeOffset.Now);
        Assert.Equal(2, _store.CountItems(tab.Id));

        _store.RemoveTab(tab.Id);
        Assert.Empty(_store.GetTabs());
        Assert.Equal(0, _store.CountItems(tab.Id));
    }

    [Fact]
    public void MoveToTab_changes_owner_tab()
    {
        var a = _store.CreateTab("a");
        var b = _store.CreateTab("b");
        var item = _store.InsertItem(a.Id, 0, Text("x"), "hx", false, null, DateTimeOffset.Now);

        _store.MoveToTab(item.Id, b.Id, 0);

        Assert.Empty(_store.LoadItems(a.Id));
        Assert.Single(_store.LoadItems(b.Id));
    }

    [Fact]
    public void Pinned_notes_and_created_are_persisted()
    {
        var tab = _store.CreateTab("t");
        var created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var item = _store.InsertItem(tab.Id, 0, Text("x"), "hx", false, null, created);

        _store.SetPinned(item.Id, true);
        _store.SetNotes(item.Id, "hello");

        var loaded = _store.LoadItems(tab.Id).Single();
        Assert.True(loaded.Pinned);
        Assert.Equal("hello", loaded.Notes);
        Assert.Equal(created.ToUnixTimeMilliseconds(), loaded.Created.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Meta_roundtrip()
    {
        Assert.Equal("1", _store.GetMeta("schema_version"));
        _store.SetMeta("k", "v1");
        _store.SetMeta("k", "v2");
        Assert.Equal("v2", _store.GetMeta("k"));
    }
}
