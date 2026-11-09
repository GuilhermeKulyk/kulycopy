using KulyCopy.Core;
using Xunit;

namespace KulyCopy.Core.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Shift+V", Hotkey.ModControl | Hotkey.ModShift, 0x56u)]
    [InlineData("ctrl+alt+v", Hotkey.ModControl | Hotkey.ModAlt, 0x56u)]
    [InlineData("Win+Insert", Hotkey.ModWin, 0x2Du)]
    [InlineData("F12", 0u, 0x7Bu)]
    [InlineData("Alt+F9", Hotkey.ModAlt, 0x78u)]
    [InlineData("Ctrl+1", Hotkey.ModControl, 0x31u)]
    [InlineData("Ctrl++", Hotkey.ModControl, 0xBBu)]
    [InlineData("Meta+Space", Hotkey.ModWin, 0x20u)]
    [InlineData("Ctrl+Num5", Hotkey.ModControl, 0x65u)]
    public void Parses_valid_shortcuts(string text, uint modifiers, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var hk));
        Assert.Equal(modifiers, hk.Modifiers);
        Assert.Equal(vk, hk.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Foo")]
    [InlineData("A+B")]
    public void Rejects_invalid_shortcuts(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+Shift+V", "Ctrl+Shift+V")]
    [InlineData("shift+ctrl+v", "Ctrl+Shift+V")]
    [InlineData("Alt+F9", "Alt+F9")]
    [InlineData("Win+Return", "Win+ENTER")]
    public void Format_is_stable(string input, string expected)
    {
        Assert.True(HotkeyParser.TryParse(input, out var hk));
        Assert.Equal(expected, HotkeyParser.Format(hk));
        // E o texto formatado volta para o mesmo atalho.
        Assert.True(HotkeyParser.TryParse(HotkeyParser.Format(hk), out var again));
        Assert.Equal(hk, again);
    }
}
