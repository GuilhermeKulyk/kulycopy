using System.Text.Json;
using System.Text.Json.Serialization;

namespace KulyCopy.Core;

/// <summary>Nomes das ações com atalho global configurável.</summary>
public static class HotkeyActions
{
    public const string ShowHide = "ShowHide";
    public const string ShowTrayMenu = "ShowTrayMenu";
    public const string PasteNext = "PasteNext";
    public const string PastePrevious = "PastePrevious";
    public const string ToggleMonitoring = "ToggleMonitoring";
    public const string EditClipboard = "EditClipboard";
    public const string PasteAsPlainText = "PasteAsPlainText";
    public const string ShowClipboardContent = "ShowClipboardContent";
    public const string NewItem = "NewItem";

    public static readonly (string Key, string Label)[] All =
    {
        (ShowHide, "Show/hide main window"),
        (ShowTrayMenu, "Show tray menu"),
        (PasteNext, "Paste next item"),
        (PastePrevious, "Paste previous item"),
        (ToggleMonitoring, "Enable/disable clipboard storing"),
        (EditClipboard, "Edit clipboard"),
        (PasteAsPlainText, "Paste as plain text"),
        (ShowClipboardContent, "Show clipboard content"),
        (NewItem, "Create new item"),
    };
}

/// <summary>Configurações do usuário (settings.json). Propriedades públicas para o binding do WPF.</summary>
public sealed class AppSettings
{
    // --- Geral ---
    public bool Autostart { get; set; }
    public bool ConfirmExit { get; set; }
    public bool HideOnUnfocus { get; set; } = true;
    public bool PasteAfterActivate { get; set; } = true;
    public bool HideAfterActivate { get; set; } = true;
    public bool MoveItemToTop { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool ShowAtStartup { get; set; }
    public string ClipboardTab { get; set; } = "clipboard";
    public string Language { get; set; } = "en";

    // --- Histórico ---
    public int MaxItems { get; set; } = 200;
    public bool StoreText { get; set; } = true;
    public bool StoreHtml { get; set; } = true;
    public bool StoreRtf { get; set; }
    public bool StoreImages { get; set; } = true;
    public bool StoreFiles { get; set; } = true;
    public int MaxImageSizeKb { get; set; } = 20480;
    public bool Notifications { get; set; }
    public int NotificationSeconds { get; set; } = 3;

    // --- Aparência ---
    public string Theme { get; set; } = "System";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 13;
    public string EditorFontFamily { get; set; } = "Consolas";
    public bool ShowRowNumbers { get; set; } = true;
    public bool TextWrap { get; set; } = true;
    public int MaxPreviewLines { get; set; } = 6;
    public bool ShowNotes { get; set; } = true;

    // --- Bandeja ---
    public bool ShowTray { get; set; } = true;
    public int TrayItems { get; set; } = 10;
    public string TrayTab { get; set; } = "";
    public bool TrayPaste { get; set; } = true;

    // --- Scripts ---
    public int ScriptTimeoutSeconds { get; set; } = 300;

    // --- Atalhos globais ---
    public Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        [HotkeyActions.ShowHide] = "Ctrl+Shift+V",
        [HotkeyActions.ShowTrayMenu] = "",
        [HotkeyActions.PasteNext] = "",
        [HotkeyActions.PastePrevious] = "",
        [HotkeyActions.ToggleMonitoring] = "",
        [HotkeyActions.EditClipboard] = "",
        [HotkeyActions.PasteAsPlainText] = "",
        [HotkeyActions.ShowClipboardContent] = "",
        [HotkeyActions.NewItem] = "",
    };

    // --- Janela ---
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 560;
    public double WindowHeight { get; set; } = 700;

    // --- Valores livres, usados pela função settings() dos scripts ---
    public Dictionary<string, string> Custom { get; set; } = new();

    public string Hotkey(string action) =>
        Hotkeys.TryGetValue(action, out var v) ? v : "";

    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, SettingsStore.JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions) ?? new AppSettings();
    }

    /// <summary>Garante que todas as ações conhecidas tenham entrada no dicionário (para o binding).</summary>
    public void Normalize()
    {
        foreach (var (key, _) in HotkeyActions.All)
            Hotkeys.TryAdd(key, "");
        if (MaxItems < 1) MaxItems = 1;
        if (TrayItems < 0) TrayItems = 0;
        if (MaxPreviewLines < 1) MaxPreviewLines = 1;
        if (FontSize < 6) FontSize = 6;
        if (string.IsNullOrWhiteSpace(ClipboardTab)) ClipboardTab = "clipboard";
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Segoe UI";
        if (string.IsNullOrWhiteSpace(EditorFontFamily)) EditorFontFamily = "Consolas";
        if (ScriptTimeoutSeconds < 1) ScriptTimeoutSeconds = 1;
    }
}

public static class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read settings, using defaults", ex);
                settings = new AppSettings();
            }
        }
        else settings = new AppSettings();

        settings.Normalize();
        return settings;
    }

    public static void Save(string path, AppSettings settings)
    {
        settings.Normalize();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
