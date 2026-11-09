using KulyCopy.Core;
using Xunit;

namespace KulyCopy.Core.Tests;

public class TabExportTests
{
    [Fact]
    public void Text_formats_stay_readable_and_binary_is_base64()
    {
        var item = new ItemRecord
        {
            Created = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero),
            Pinned = true,
            Notes = "n",
            Data = new Dictionary<string, byte[]>
            {
                [Mime.Text] = DataUtil.Bytes("plain é"),
                [Mime.Png] = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                [Mime.WindowTitle] = DataUtil.Bytes("ignored internal"),
            },
        };

        var exported = TabExport.ToExported(item);
        Assert.Equal("plain é", exported.Data[Mime.Text]);
        Assert.StartsWith("base64:", exported.Data[Mime.Png]);
        Assert.False(exported.Data.ContainsKey(Mime.WindowTitle));

        var (data, pinned, notes, created) = TabExport.FromExported(exported);
        Assert.Equal("plain é", DataUtil.GetText(data));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, data[Mime.Png]);
        Assert.True(pinned);
        Assert.Equal("n", notes);
        Assert.Equal(item.Created, created);
    }

    [Fact]
    public void Text_starting_with_base64_prefix_is_escaped()
    {
        var value = TabExport.EncodeValue(Mime.Text, DataUtil.Bytes("base64:not really"));
        Assert.Equal("base64:not really", DataUtil.Text(TabExport.DecodeValue(value)));
    }

    [Fact]
    public void Serialize_parse_roundtrip()
    {
        var tab = new ExportedTab
        {
            Name = "Notes",
            Items =
            {
                new ExportedItem { Data = { [Mime.Text] = "one" } },
                new ExportedItem { Data = { [Mime.Text] = "two" }, Notes = "second" },
            },
        };

        var parsed = TabExport.Parse(TabExport.Serialize(tab));
        Assert.Equal("Notes", parsed.Name);
        Assert.Equal(2, parsed.Items.Count);
        Assert.Equal("second", parsed.Items[1].Notes);
        Assert.Equal("kulycopy-tab", parsed.Format);
    }
}
