using KulyCopy.Clip;
using KulyCopy.Core;
using KulyCopy.Native;

namespace KulyCopy.Hotkeys;

/// <summary>Registra atalhos globais (RegisterHotKey) na janela de mensagens e despacha as ações.</summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, (string Name, Hotkey Hotkey, Action Action)> _registered = new();
    private int _nextId = 0x4B00;

    public GlobalHotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.HotkeyPressed += OnHotkeyPressed;
    }

    public IEnumerable<(string Name, Hotkey Hotkey)> Registered =>
        _registered.Values.Select(v => (v.Name, v.Hotkey));

    private void OnHotkeyPressed(int id)
    {
        if (!_registered.TryGetValue(id, out var entry)) return;
        try
        {
            entry.Action();
        }
        catch (Exception ex)
        {
            Log.Error($"Global shortcut '{entry.Name}' failed", ex);
        }
    }

    public bool Register(string name, string shortcut, Action action, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        if (!HotkeyParser.TryParse(shortcut, out var hk))
        {
            error = $"Invalid shortcut \"{shortcut}\" for \"{name}\".";
            return false;
        }

        if (_registered.Values.Any(v => v.Hotkey == hk))
        {
            error = $"Shortcut \"{shortcut}\" is used by more than one action.";
            return false;
        }

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, hk.Modifiers | Hotkey.ModNoRepeat, hk.VirtualKey))
        {
            error = $"Could not register \"{shortcut}\" for \"{name}\" (already used by another application?).";
            return false;
        }

        _registered[id] = (name, hk, action);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.HotkeyPressed -= OnHotkeyPressed;
    }
}
