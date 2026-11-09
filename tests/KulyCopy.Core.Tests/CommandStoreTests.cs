using KulyCopy.Core;
using Xunit;

namespace KulyCopy.Core.Tests;

public class CommandStoreTests
{
    [Fact]
    public void Defaults_are_not_empty_and_have_names()
    {
        var defaults = CommandStore.Defaults();
        Assert.NotEmpty(defaults);
        Assert.All(defaults, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
    }

    [Fact]
    public void Serialize_and_parse_roundtrip()
    {
        var original = new List<Command>
        {
            new()
            {
                Name = "Open", InMenu = true, Match = @"^https?://", Input = Mime.Text,
                Cmd = "open(str(data(mimeText)))", Shortcut = "Ctrl+Shift+O",
            },
            new() { Name = "Trash", InMenu = true, Tab = "(trash)", Remove = true, Enabled = false },
        };

        var json = CommandStore.Serialize(original);
        var parsed = CommandStore.Parse(json);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Open", parsed[0].Name);
        Assert.True(parsed[0].InMenu);
        Assert.Equal(@"^https?://", parsed[0].Match);
        Assert.Equal("Ctrl+Shift+O", parsed[0].Shortcut);
        Assert.Equal("(trash)", parsed[1].Tab);
        Assert.True(parsed[1].Remove);
        Assert.False(parsed[1].Enabled);
    }

    [Fact]
    public void Parse_accepts_single_object_and_empty_input()
    {
        var one = CommandStore.Parse("{ \"name\": \"Solo\", \"automatic\": true }");
        Assert.Single(one);
        Assert.Equal("Solo", one[0].Name);
        Assert.True(one[0].Automatic);

        Assert.Empty(CommandStore.Parse("   "));
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_missing_and_save_creates_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kulycopy-cmd-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "commands.json");
        try
        {
            var loaded = CommandStore.Load(file);
            Assert.Equal(CommandStore.Defaults().Count, loaded.Count);

            CommandStore.Save(file, loaded);
            Assert.True(File.Exists(file));
            Assert.Equal(loaded.Count, CommandStore.Load(file).Count);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Clone_copies_every_field()
    {
        var c = new Command
        {
            Name = "n", Enabled = false, Automatic = true, InMenu = true, IsGlobalShortcut = true, IsScript = true,
            Match = "m", Window = "w", Input = "i", Output = "o", Tab = "t", OutputTab = "ot", Remove = true,
            HideWindow = true, Shortcut = "s", GlobalShortcut = "g", Icon = "ic", Cmd = "cmd",
        };
        var clone = c.Clone();
        Assert.Equal(CommandStore.Serialize(new[] { c }), CommandStore.Serialize(new[] { clone }));
        Assert.NotSame(c, clone);
    }
}
