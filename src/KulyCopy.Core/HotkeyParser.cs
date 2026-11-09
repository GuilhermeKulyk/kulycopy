using System.Text;

namespace KulyCopy.Core;

/// <summary>Atalho global já resolvido para os valores usados por RegisterHotKey.</summary>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public override string ToString() => HotkeyParser.Format(this);
}

/// <summary>Converte textos como "Ctrl+Shift+V" em modificadores + tecla virtual (e vice-versa).</summary>
public static class HotkeyParser
{
    private static readonly Dictionary<string, uint> KeyNames = BuildKeyNames();

    private static Dictionary<string, uint> BuildKeyNames()
    {
        var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["ENTER"] = 0x0D, ["RETURN"] = 0x0D, ["TAB"] = 0x09, ["SPACE"] = 0x20, ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
            ["BACKSPACE"] = 0x08, ["BACK"] = 0x08, ["INSERT"] = 0x2D, ["INS"] = 0x2D, ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
            ["HOME"] = 0x24, ["END"] = 0x23, ["PAGEUP"] = 0x21, ["PGUP"] = 0x21, ["PRIOR"] = 0x21,
            ["PAGEDOWN"] = 0x22, ["PGDN"] = 0x22, ["NEXT"] = 0x22,
            ["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25, ["RIGHT"] = 0x27,
            ["PAUSE"] = 0x13, ["PRINT"] = 0x2C, ["PRINTSCREEN"] = 0x2C, ["SNAPSHOT"] = 0x2C,
            ["SCROLLLOCK"] = 0x91, ["SCROLL"] = 0x91, ["NUMLOCK"] = 0x90, ["CAPSLOCK"] = 0x14, ["CAPITAL"] = 0x14,
            ["APPS"] = 0x5D, ["MENU"] = 0x5D,
            ["PLUS"] = 0xBB, ["="] = 0xBB, ["+"] = 0xBB, ["MINUS"] = 0xBD, ["-"] = 0xBD, ["COMMA"] = 0xBC, [","] = 0xBC,
            ["PERIOD"] = 0xBE, ["."] = 0xBE, ["SLASH"] = 0xBF, ["/"] = 0xBF, ["BACKSLASH"] = 0xDC, ["\\"] = 0xDC,
            ["SEMICOLON"] = 0xBA, [";"] = 0xBA, ["QUOTE"] = 0xDE, ["'"] = 0xDE, ["LBRACKET"] = 0xDB, ["["] = 0xDB,
            ["RBRACKET"] = 0xDD, ["]"] = 0xDD, ["GRAVE"] = 0xC0, ["`"] = 0xC0, ["TILDE"] = 0xC0,
            ["MULTIPLY"] = 0x6A, ["ADD"] = 0x6B, ["SUBTRACT"] = 0x6D, ["DECIMAL"] = 0x6E, ["DIVIDE"] = 0x6F,
            ["MEDIAPLAY"] = 0xB3, ["MEDIASTOP"] = 0xB2, ["MEDIANEXT"] = 0xB0, ["MEDIAPREV"] = 0xB1,
            ["VOLUMEUP"] = 0xAF, ["VOLUMEDOWN"] = 0xAE, ["VOLUMEMUTE"] = 0xAD,
        };
        for (var i = 1; i <= 24; i++) d["F" + i] = (uint)(0x6F + i);
        for (var i = 0; i <= 9; i++) d["NUM" + i] = (uint)(0x60 + i);
        for (var i = 0; i <= 9; i++) d["NUMPAD" + i] = (uint)(0x60 + i);
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = (uint)c;
        for (var c = '0'; c <= '9'; c++) d[c.ToString()] = (uint)c;
        return d;
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0;
        uint vk = 0;

        // Divide em '+' mas preserva um '+' literal no final ("Ctrl++").
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '+' && sb.Length > 0 && i < text.Length - 1)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());

        foreach (var raw in parts)
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            switch (p.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": case "STRG": mods |= Hotkey.ModControl; continue;
                case "SHIFT": mods |= Hotkey.ModShift; continue;
                case "ALT": mods |= Hotkey.ModAlt; continue;
                case "WIN": case "META": case "SUPER": case "WINDOWS": mods |= Hotkey.ModWin; continue;
            }
            if (vk != 0) return false; // duas teclas principais no mesmo atalho
            if (!KeyNames.TryGetValue(p, out vk)) return false;
        }

        if (vk == 0) return false;
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    public static string Format(Hotkey hk)
    {
        var sb = new StringBuilder();
        if ((hk.Modifiers & Hotkey.ModControl) != 0) sb.Append("Ctrl+");
        if ((hk.Modifiers & Hotkey.ModShift) != 0) sb.Append("Shift+");
        if ((hk.Modifiers & Hotkey.ModAlt) != 0) sb.Append("Alt+");
        if ((hk.Modifiers & Hotkey.ModWin) != 0) sb.Append("Win+");
        sb.Append(KeyName(hk.VirtualKey) ?? ("0x" + hk.VirtualKey.ToString("X2")));
        return sb.ToString();
    }

    /// <summary>Nome legível de uma tecla virtual (ou null se desconhecida).</summary>
    public static string? KeyName(uint vk) =>
        KeyNames.Where(kv => kv.Value == vk)
            .Select(kv => kv.Key)
            .OrderByDescending(k => k.Length > 1 ? 1 : 0) // prefere nomes como "PLUS" a símbolos soltos
            .ThenBy(k => k, StringComparer.Ordinal)
            .FirstOrDefault();
}
