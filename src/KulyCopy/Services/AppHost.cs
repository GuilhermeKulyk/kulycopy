using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using KulyCopy.Clip;
using KulyCopy.Commands;
using KulyCopy.Core;
using KulyCopy.Hotkeys;
using KulyCopy.Ipc;
using KulyCopy.Models;
using KulyCopy.Native;
using KulyCopy.Scripting;
using KulyCopy.UI;

namespace KulyCopy.Services;

/// <summary>
/// Ponto central do aplicativo: cria e conecta todos os serviços (histórico, monitor, atalhos,
/// scripts, bandeja, janela principal, IPC) e implementa as ações de alto nível.
/// </summary>
public sealed class AppHost
{
    private int _cycleIndex;
    private bool _shutdown;
    private PreferencesWindow? _preferences;

    public static AppHost? Current { get; private set; }

    public AppSettings Settings { get; private set; } = new();
    public ItemStore Store { get; private set; } = null!;
    public HistoryService History { get; private set; } = null!;
    public MessageWindow MessageWindow { get; private set; } = null!;
    public ClipboardMonitor Monitor { get; private set; } = null!;
    public GlobalHotkeyManager Hotkeys { get; private set; } = null!;
    public List<Command> Commands { get; set; } = new();
    public ScriptHost Scripts { get; private set; } = null!;
    public CommandRunner Runner { get; private set; } = null!;
    public TrayIcon? Tray { get; private set; }
    public MainWindow Main { get; private set; } = null!;
    public IpcServer? Ipc { get; private set; }

    public Dispatcher Dispatcher => Application.Current.Dispatcher;

    /// <summary>Janela de outro programa que estava ativa antes de o KulyCopy aparecer (alvo do "colar").</summary>
    public IntPtr LastForegroundWindow { get; set; }

    public bool MonitoringEnabled => Monitor.Enabled;

    public event Action<bool>? MonitoringChanged;

    public AppHost() => Current = this;

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------

    public void Start(bool showWindow)
    {
        Paths.Initialize();
        Log.Init(Paths.LogFile);
        Log.Info($"KulyCopy {AppInfo.Version} starting (config: {Paths.ConfigDir})");

        var firstRun = !File.Exists(Paths.SettingsFile);
        Settings = SettingsStore.Load(Paths.SettingsFile);

        // O instalador pode ter criado a entrada "iniciar com o Windows": adota em vez de apagar.
        if (!Paths.IsPortable && Autostart.IsEnabled()) Settings.Autostart = true;

        Store = new ItemStore(Paths.DatabaseFile);
        History = new HistoryService(Store, Settings);
        History.Load();

        Commands = CommandStore.Load(Paths.CommandsFile);
        if (!File.Exists(Paths.CommandsFile)) SaveCommands();

        ThemeManager.Apply(Settings.Theme);
        ClipboardItem.MaxPreviewLines = Settings.MaxPreviewLines;

        MessageWindow = new MessageWindow();
        Monitor = new ClipboardMonitor(MessageWindow, () => ClipboardReader.Read(Settings));
        Hotkeys = new GlobalHotkeyManager(MessageWindow);
        Scripts = new ScriptHost(this);
        Runner = new CommandRunner(this);

        Main = new MainWindow(this);
        Tray = new TrayIcon(this);

        Monitor.Changed += OnClipboardChanged;
        RegisterHotkeys();
        Autostart.Set(Settings.Autostart);

        Ipc = new IpcServer(Dispatcher, HandleCli);
        Ipc.Start();

        if (firstRun) SaveSettings();
        if (showWindow || firstRun || Settings.ShowAtStartup) Main.ShowAndActivate(null);

        if (firstRun)
        {
            var hotkey = Settings.Hotkey(HotkeyActions.ShowHide);
            var hint = string.IsNullOrWhiteSpace(hotkey) ? "Click the tray icon" : "Press " + hotkey;
            Notify("KulyCopy is running in the system tray", hint + " to open your clipboard history. Everything you copy is saved locally.", 8);
        }

        Log.Info("Ready");
    }

    public void SaveSettings()
    {
        try { SettingsStore.Save(Paths.SettingsFile, Settings); }
        catch (Exception ex) { Log.Error("Failed to save settings", ex); }
    }

    public void SaveCommands()
    {
        try { CommandStore.Save(Paths.CommandsFile, Commands); }
        catch (Exception ex) { Log.Error("Failed to save commands", ex); }
    }

    /// <summary>Copia os valores de outra instância para a instância compartilhada de Settings.</summary>
    public void ReplaceSettings(AppSettings source)
    {
        foreach (var prop in typeof(AppSettings).GetProperties().Where(p => p.CanRead && p.CanWrite))
            prop.SetValue(Settings, prop.GetValue(source));
    }

    /// <summary>Aplica tudo que depende das configurações (tema, atalhos, bandeja, autostart, aparência).</summary>
    public void ApplySettings()
    {
        Settings.Normalize();
        SaveSettings();
        ThemeManager.Apply(Settings.Theme);
        ClipboardItem.MaxPreviewLines = Settings.MaxPreviewLines;
        Main.ApplyAppearance();
        Tray?.SetVisible(Settings.ShowTray);
        Tray?.ApplyTheme();
        Autostart.Set(Settings.Autostart);
        RegisterHotkeys();
        foreach (var tab in History.Tabs.ToList()) History.Trim(tab);
    }

    public void RegisterHotkeys()
    {
        Hotkeys.UnregisterAll();
        var errors = new List<string>();

        void Register(string name, string shortcut, Action action)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return;
            if (!Hotkeys.Register(name, shortcut, action, out var error) && error is not null) errors.Add(error);
        }

        Register("Show/hide main window", Settings.Hotkey(HotkeyActions.ShowHide), () =>
        {
            RememberForegroundWindow();
            Main.ToggleWindow();
        });
        Register("Show tray menu", Settings.Hotkey(HotkeyActions.ShowTrayMenu), () => Tray?.ShowMenu());
        Register("Paste next item", Settings.Hotkey(HotkeyActions.PasteNext), () => PasteCycle(+1));
        Register("Paste previous item", Settings.Hotkey(HotkeyActions.PastePrevious), () => PasteCycle(-1));
        Register("Toggle clipboard storing", Settings.Hotkey(HotkeyActions.ToggleMonitoring), ToggleMonitoring);
        Register("Edit clipboard", Settings.Hotkey(HotkeyActions.EditClipboard), EditClipboard);
        Register("Paste as plain text", Settings.Hotkey(HotkeyActions.PasteAsPlainText), PasteAsPlainText);
        Register("Show clipboard content", Settings.Hotkey(HotkeyActions.ShowClipboardContent), ShowClipboardContent);
        Register("New item", Settings.Hotkey(HotkeyActions.NewItem), () => OpenEditorForItem(History.ClipboardTab, null));

        foreach (var cmd in Commands.Where(c => c.Enabled && c.IsGlobalShortcut && !string.IsNullOrWhiteSpace(c.GlobalShortcut)).ToList())
        {
            var captured = cmd;
            Register(cmd.Name, cmd.GlobalShortcut, () => Runner.RunGlobalShortcutCommand(captured));
        }

        if (errors.Count > 0)
        {
            foreach (var e in errors) Log.Warn(e);
            Notify("Shortcut problem", errors[0]);
        }
    }

    public void Exit(bool force = false)
    {
        if (_shutdown) return;
        if (!force && Settings.ConfirmExit)
        {
            var answer = MessageBox.Show("Exit KulyCopy? Clipboard history will stop being recorded.", "KulyCopy",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        Main.AllowClose = true;
        SaveSettings();
        Shutdown();
        Application.Current.Shutdown();
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        Log.Info("Shutting down");
        try { Ipc?.Dispose(); } catch { }
        try { Hotkeys?.Dispose(); } catch { }
        try { Monitor?.Dispose(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { MessageWindow?.Dispose(); } catch { }
        try { Store?.Dispose(); } catch { }
    }

    // ------------------------------------------------------------------
    // Área de transferência
    // ------------------------------------------------------------------

    private void OnClipboardChanged(ClipboardSnapshot snapshot)
    {
        if (snapshot.Skip) return;
        var data = snapshot.Data;

        if (snapshot.Own)
        {
            // Cópia feita pelo próprio KulyCopy: só garante que está na aba da área de transferência.
            data.Remove(Mime.Owner);
            History.AddItem(History.ClipboardTab, data, dedupe: true);
            _cycleIndex = 0;
            return;
        }

        var title = WindowHelper.GetForegroundWindowTitle();
        data[Mime.WindowTitle] = DataUtil.Bytes(title);

        var decision = Runner.ProcessClipboard(data, title);
        if (decision.Ignored) return;

        ClipboardItem? added = null;
        foreach (var name in decision.Tabs)
        {
            var tab = History.GetOrCreateTab(name);
            var item = History.AddItem(tab, DataUtil.Clone(decision.Data), dedupe: true);
            added ??= item;
        }
        _cycleIndex = 0;

        if (Settings.Notifications && added is not null)
            Notify("New clipboard item", added.SingleLine(80));
    }

    public ClipboardSnapshot? ReadClipboard(bool all = true) => ClipboardReader.Read(Settings, all);

    public bool WriteClipboard(IReadOnlyDictionary<string, byte[]> data, ClipboardWriteMode mode = ClipboardWriteMode.Own) =>
        ClipboardWriter.Write(data, mode);

    public static Dictionary<string, byte[]> MergeItems(IList<ClipboardItem> items)
    {
        if (items.Count == 1) return DataUtil.Clone(items[0].Data);
        var texts = items.Select(i => i.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
        if (texts.Count == 0) return DataUtil.Clone(items[0].Data);
        return DataUtil.FromText(string.Join("\n", texts));
    }

    /// <summary>Enter em um item: copia, move para o topo, esconde e cola conforme as preferências.</summary>
    public void ActivateItems(TabViewModel tab, IList<ClipboardItem> items)
    {
        if (items.Count == 0) return;
        var data = MergeItems(items);
        if (!WriteClipboard(data, ClipboardWriteMode.Own)) return;

        if (Settings.MoveItemToTop)
            foreach (var item in Enumerable.Reverse(items)) History.MoveToTop(tab, item);

        if (Settings.PasteAfterActivate)
        {
            Main.HideWindow();
            PasteToTarget(runAsync: true);
        }
        else if (Settings.HideAfterActivate)
        {
            Main.HideWindow();
        }
    }

    public void CopyItemsToClipboard(TabViewModel tab, IList<ClipboardItem> items)
    {
        if (items.Count == 0) return;
        WriteClipboard(MergeItems(items), ClipboardWriteMode.Own);
        if (Settings.MoveItemToTop)
            foreach (var item in Enumerable.Reverse(items)) History.MoveToTop(tab, item);
    }

    public void PasteToTarget(bool runAsync)
    {
        var target = LastForegroundWindow;
        if (runAsync) Task.Run(() => WindowHelper.PasteInto(target));
        else WindowHelper.PasteInto(target);
    }

    public void RememberForegroundWindow()
    {
        var hwnd = WindowHelper.GetForeignForegroundWindow();
        if (hwnd != IntPtr.Zero) LastForegroundWindow = hwnd;
    }

    public string CurrentWindowTitle()
    {
        var fg = WindowHelper.GetForeignForegroundWindow();
        return WindowHelper.GetWindowTitle(fg != IntPtr.Zero ? fg : LastForegroundWindow);
    }

    private void PasteCycle(int delta)
    {
        var tab = History.ClipboardTab;
        if (tab.Items.Count == 0) return;
        _cycleIndex = Math.Clamp(_cycleIndex + delta, 0, tab.Items.Count - 1);
        var item = tab.Items[_cycleIndex];
        RememberForegroundWindow();
        if (!WriteClipboard(item.Data, ClipboardWriteMode.Skip)) return;
        PasteToTarget(runAsync: true);
    }

    public void EditClipboard()
    {
        var snapshot = ReadClipboard(all: true);
        var text = snapshot is not null ? DataUtil.GetText(snapshot.Data) ?? "" : "";
        var editor = new ItemEditorWindow("Edit clipboard", text, Settings.EditorFontFamily);
        var result = editor.ShowModal(Main.IsVisible ? Main : null);
        if (result is null) return;
        WriteClipboard(DataUtil.FromText(result), ClipboardWriteMode.Own);
    }

    public void PasteAsPlainText()
    {
        RememberForegroundWindow();
        var snapshot = ReadClipboard(all: true);
        var text = snapshot is not null ? DataUtil.GetText(snapshot.Data) : null;
        if (string.IsNullOrEmpty(text)) return;
        if (!WriteClipboard(DataUtil.FromText(text), ClipboardWriteMode.Own)) return;
        PasteToTarget(runAsync: true);
    }

    public void ShowClipboardContent()
    {
        var snapshot = ReadClipboard(all: true);
        var data = snapshot?.Data ?? new Dictionary<string, byte[]>();
        var window = new ShowContentWindow("Clipboard content", data);
        if (Main.IsVisible) window.Owner = Main;
        window.Show();
    }

    public void ProcessClipboardNow() => Monitor.CheckNow();

    // ------------------------------------------------------------------
    // Monitoramento
    // ------------------------------------------------------------------

    public void SetMonitoring(bool enabled)
    {
        if (Monitor.Enabled == enabled) return;
        Monitor.Enabled = enabled;
        Log.Info(enabled ? "Clipboard storing enabled" : "Clipboard storing disabled");
        Tray?.SetMonitoring(enabled);
        MonitoringChanged?.Invoke(enabled);
    }

    public void ToggleMonitoring() => SetMonitoring(!Monitor.Enabled);

    // ------------------------------------------------------------------
    // UI auxiliar
    // ------------------------------------------------------------------

    public void Notify(string title, string message, int? seconds = null)
    {
        var ms = Math.Max(1, seconds ?? Settings.NotificationSeconds) * 1000;
        Tray?.Balloon(title, message, ms);
    }

    /// <summary>Abre o editor para um item existente ou para um item novo (item == null).</summary>
    public void OpenEditorForItem(TabViewModel tab, ClipboardItem? item)
    {
        if (item is not null && !item.HasText)
        {
            MessageBox.Show(item.HasImage ? "Images cannot be edited as text." : "This item has no text to edit.",
                "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new ItemEditorWindow(item is null ? "New item" : "Edit item", item?.Text ?? "", Settings.EditorFontFamily);
        var text = editor.ShowModal(Main.IsVisible ? Main : null);
        if (text is null) return;

        if (item is null)
        {
            if (text.Length == 0) return;
            History.AddItem(tab, DataUtil.FromText(text), row: -1, dedupe: false);
            return;
        }

        if (text == item.Text) return;
        var data = DataUtil.Clone(item.Data);
        data[Mime.Text] = DataUtil.Bytes(text);
        data.Remove(Mime.Html);
        data.Remove(Mime.Rtf);
        History.UpdateData(item, data);
    }

    public void ShowPreferences(int tabIndex = 0)
    {
        if (_preferences is { IsLoaded: true })
        {
            _preferences.SelectTab(tabIndex);
            _preferences.Activate();
            return;
        }
        _preferences = new PreferencesWindow(this, tabIndex);
        if (Main.IsVisible) _preferences.Owner = Main;
        else _preferences.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _preferences.Closed += (_, _) => _preferences = null;
        _preferences.Show();
    }

    // ------------------------------------------------------------------
    // Linha de comando (via IPC)
    // ------------------------------------------------------------------

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public IpcResponse HandleCli(IpcRequest request)
    {
        var args = request.Args.Length == 0 ? new[] { "toggle" } : request.Args;
        string script;
        string[] rest;

        var fn = args[0];
        if (fn is "eval" or "-e" or "--eval")
        {
            if (args.Length < 2) return Error("eval: missing script");
            script = args[1];
            rest = args.Skip(2).ToArray();
        }
        else if (fn == "-")
        {
            script = request.Stdin ?? "";
            rest = args.Skip(1).ToArray();
        }
        else
        {
            if (!Identifier.IsMatch(fn)) return Error($"Unknown command \"{fn}\". Run \"kulycopy --help\" for usage.");
            script = $"(typeof {fn} === 'function') ? {fn}.apply(null, __arguments) : __api.UnknownCommand('{fn}')";
            rest = args.Skip(1).ToArray();
        }

        var ctx = new ScriptContext { Arguments = rest, Stdin = request.Stdin };
        var result = Scripts.Run(script, ctx);

        var response = new IpcResponse { ExitCode = result.ExitCode, Stdout = result.Stdout };
        if (result.Error is not null)
        {
            response.Stderr = "Error: " + result.Error + Environment.NewLine;
            return response;
        }

        switch (result.Value)
        {
            case null:
                break;
            case ByteArray bytes when response.Stdout.Length == 0:
                response.StdoutBase64 = Convert.ToBase64String(bytes.Bytes);
                break;
            case object[] array:
                response.Stdout += string.Join(Environment.NewLine, array.Select(ScriptApi.ToStr));
                if (array.Length > 0) response.Stdout += Environment.NewLine;
                break;
            case bool b:
                response.Stdout += b ? "true" : "false";
                response.Stdout += Environment.NewLine;
                break;
            default:
                response.Stdout += ScriptApi.ToStr(result.Value);
                break;
        }
        return response;

        static IpcResponse Error(string message) => new() { ExitCode = 2, Stderr = message + Environment.NewLine };
    }
}
