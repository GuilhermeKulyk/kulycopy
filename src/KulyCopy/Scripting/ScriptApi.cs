using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Jint;
using KulyCopy.Clip;
using KulyCopy.Core;
using KulyCopy.Models;
using KulyCopy.Native;
using KulyCopy.Services;
using KulyCopy.UI;

namespace KulyCopy.Scripting;

/// <summary>
/// Implementação das funções de script (API no estilo do CopyQ). Cada método recebe os argumentos
/// JavaScript como object[]; o prelúdio em ScriptHost faz a ponte dos nomes em camelCase.
/// Tudo roda na thread de UI.
/// </summary>
public sealed class ScriptApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly AppHost _host;
    private readonly ScriptContext _ctx;
    private readonly Engine _engine;

    public ScriptApi(AppHost host, ScriptContext ctx, Engine engine)
    {
        _host = host;
        _ctx = ctx;
        _engine = engine;
    }

    private HistoryService History => _host.History;

    private TabViewModel CurrentTab => _ctx.Tab ??= _host.Main.SelectedTab ?? History.ClipboardTab;

    private TabViewModel SelectionTab => _ctx.SelectionTab ?? _host.Main.SelectedTab ?? History.ClipboardTab;

    private List<ClipboardItem> Selected => _ctx.SelectedItems ?? _host.Main.SelectedItemsOrdered().ToList();

    // ------------------------------------------------------------------
    // Conversões
    // ------------------------------------------------------------------

    public static string ToStr(object? o) => o switch
    {
        null => "",
        string s => s,
        ByteArray b => b.ToString(),
        byte[] b => DataUtil.Text(b),
        bool b => b ? "true" : "false",
        double d => Math.Abs(d - Math.Round(d)) < double.Epsilon && Math.Abs(d) < 1e15
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("R", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        object[] arr => string.Join("\n", arr.Select(ToStr)),
        _ => o.ToString() ?? "",
    };

    private static bool TryInt(object? o, out int value)
    {
        switch (o)
        {
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                value = (int)d;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = (int)l;
                return true;
            case string s when int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v):
                value = v;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static int Int(object? o, int fallback = 0) => TryInt(o, out var v) ? v : fallback;

    private static byte[] Bytes(object? o) => o switch
    {
        null => Array.Empty<byte>(),
        ByteArray b => b.Bytes,
        byte[] b => b,
        _ => DataUtil.Bytes(ToStr(o)),
    };

    private static object FromBytes(string mime, byte[] bytes) =>
        DataUtil.IsTextMime(mime) ? DataUtil.Text(bytes) : new ByteArray(bytes);

    /// <summary>Converte um valor JS (texto, ByteArray ou objeto mime → valor) em dados de item.</summary>
    private static Dictionary<string, byte[]> ToData(object? o)
    {
        switch (o)
        {
            case IDictionary<string, object?> dict:
            {
                var d = new Dictionary<string, byte[]>();
                foreach (var kv in dict) d[kv.Key] = Bytes(kv.Value);
                return d;
            }
            case Dictionary<string, byte[]> raw:
                return new Dictionary<string, byte[]>(raw);
            default:
                return DataUtil.FromText(ToStr(o));
        }
    }

    private static Dictionary<string, byte[]> PairsToData(object[] a, int start)
    {
        if (a.Length - start == 1) return ToData(a[start]);
        var d = new Dictionary<string, byte[]>();
        for (var i = start; i + 1 < a.Length; i += 2)
            d[ToStr(a[i])] = Bytes(a[i + 1]);
        return d;
    }

    private static Dictionary<string, object> ToJsData(IReadOnlyDictionary<string, byte[]> data)
    {
        var d = new Dictionary<string, object>();
        foreach (var kv in data) d[kv.Key] = FromBytes(kv.Key, kv.Value);
        return d;
    }

    private ClipboardItem RequireItem(int row)
    {
        var tab = CurrentTab;
        if (row < 0 || row >= tab.Items.Count)
            throw new ArgumentOutOfRangeException(nameof(row), $"Invalid row {row} (tab \"{tab.Name}\" has {tab.Items.Count} items)");
        return tab.Items[row];
    }

    private ClipboardItem? ItemAt(int row)
    {
        var tab = CurrentTab;
        return row >= 0 && row < tab.Items.Count ? tab.Items[row] : null;
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    // ------------------------------------------------------------------
    // Área de transferência
    // ------------------------------------------------------------------

    public object Copy(object[] a)
    {
        if (a.Length == 0)
        {
            WindowHelper.CopyFrom(_host.LastForegroundWindow);
            Thread.Sleep(120);
            return true;
        }
        var data = PairsToData(a, 0);
        return _host.WriteClipboard(data, ClipboardWriteMode.Own);
    }

    public object Paste(object[] a)
    {
        _host.PasteToTarget(runAsync: false);
        return true;
    }

    public object Clipboard(object[] a)
    {
        var mime = a.Length > 0 ? ToStr(a[0]) : Mime.Text;
        var snap = _host.ReadClipboard(all: true);
        return snap is not null && snap.Data.TryGetValue(mime, out var bytes) ? FromBytes(mime, bytes) : "";
    }

    public object HasClipboardFormat(object[] a)
    {
        var mime = a.Length > 0 ? ToStr(a[0]) : Mime.Text;
        var snap = _host.ReadClipboard(all: true);
        return snap is not null && snap.Data.ContainsKey(mime);
    }

    // ------------------------------------------------------------------
    // Itens
    // ------------------------------------------------------------------

    public object Read(object[] a)
    {
        var mime = Mime.Text;
        var start = 0;
        if (a.Length > 0 && a[0] is string s && !TryInt(s, out _))
        {
            mime = s;
            start = 1;
        }
        var rows = a.Skip(start).Select(x => Int(x, -1)).ToList();

        if (rows.Count == 0)
        {
            var snap = _host.ReadClipboard(all: true);
            return snap is not null && snap.Data.TryGetValue(mime, out var b) ? FromBytes(mime, b) : "";
        }

        var values = new List<object?>();
        foreach (var row in rows)
        {
            var item = ItemAt(row);
            if (item is null) { values.Add(null); continue; }
            if (mime == Mime.Notes) { values.Add(item.Notes ?? ""); continue; }
            values.Add(item.Data.TryGetValue(mime, out var bytes) ? FromBytes(mime, bytes) : null);
        }

        if (values.Count == 1) return values[0] ?? "";
        if (DataUtil.IsTextMime(mime)) return string.Join("\n", values.Select(v => v is null ? "" : ToStr(v)));
        return values.Select(v => v ?? new ByteArray(Array.Empty<byte>())).ToArray();
    }

    public object Write(object[] a)
    {
        var row = 0;
        var start = 0;
        // write([row], text) ou write([row], mime, data, [mime, data]...)
        if (a.Length >= 2 && TryInt(a[0], out var r))
        {
            row = r;
            start = 1;
        }
        if (a.Length - start <= 0) throw new ArgumentException("write: expected data");
        var data = PairsToData(a, start);
        History.AddItem(CurrentTab, data, row: row, dedupe: false);
        return true;
    }

    public object Change(object[] a)
    {
        if (a.Length < 2) throw new ArgumentException("change: expected row and data");
        var item = RequireItem(Int(a[0]));
        var data = item.DataWithNotes();
        foreach (var kv in PairsToData(a, 1)) data[kv.Key] = kv.Value;
        History.UpdateData(item, data);
        return true;
    }

    public object Add(object[] a)
    {
        for (var i = a.Length - 1; i >= 0; i--)
            History.AddItem(CurrentTab, ToData(a[i]), row: -1, dedupe: false);
        return true;
    }

    public object Insert(object[] a)
    {
        if (a.Length < 2) throw new ArgumentException("insert: expected row and data");
        var row = Int(a[0]);
        for (var i = a.Length - 1; i >= 1; i--)
            History.AddItem(CurrentTab, ToData(a[i]), row: row, dedupe: false);
        return true;
    }

    public object Remove(object[] a)
    {
        if (a.Length == 0)
        {
            History.RemoveItems(SelectionTab, Selected);
            return true;
        }
        var items = a.Select(x => ItemAt(Int(x, -1))).Where(i => i is not null).Cast<ClipboardItem>().ToList();
        History.RemoveItems(CurrentTab, items);
        return true;
    }

    public object Edit(object[] a)
    {
        var tab = CurrentTab;
        if (a.Length == 0)
        {
            _host.Dispatcher.InvokeAsync(() => _host.OpenEditorForItem(tab, null));
            return true;
        }
        foreach (var x in a)
        {
            var row = Int(x, -1);
            if (row < 0)
            {
                _host.Dispatcher.InvokeAsync(() => _host.EditClipboard());
                continue;
            }
            var item = RequireItem(row);
            _host.Dispatcher.InvokeAsync(() => _host.OpenEditorForItem(tab, item));
        }
        return true;
    }

    public object Select(object[] a)
    {
        var item = RequireItem(Int(a.Length > 0 ? a[0] : 0));
        _host.CopyItemsToClipboard(CurrentTab, new[] { item });
        return true;
    }

    public object Move(object[] a)
    {
        var row = Int(a.Length > 0 ? a[0] : 0);
        var tab = SelectionTab;
        var items = Selected.Where(i => tab.Items.Contains(i)).OrderBy(tab.Items.IndexOf).ToList();
        for (var i = 0; i < items.Count; i++)
            History.MoveItem(tab, items[i], row + i);
        return true;
    }

    public object Pin(object[] a)
    {
        foreach (var x in a) History.SetPinned(CurrentTab, RequireItem(Int(x)), true);
        return true;
    }

    public object Unpin(object[] a)
    {
        foreach (var x in a) History.SetPinned(CurrentTab, RequireItem(Int(x)), false);
        return true;
    }

    public object Length(object[] a) => CurrentTab.Items.Count;

    // ------------------------------------------------------------------
    // Abas
    // ------------------------------------------------------------------

    public object? Tab(object[] a)
    {
        if (a.Length == 0) return History.Tabs.Select(t => (object)t.Name).ToArray();
        _ctx.Tab = History.GetOrCreateTab(ToStr(a[0]));
        return null;
    }

    public object RemoveTab(object[] a)
    {
        if (a.Length == 0) throw new ArgumentException("removeTab: expected tab name");
        var tab = History.FindTab(ToStr(a[0])) ?? throw new ArgumentException($"Tab \"{ToStr(a[0])}\" not found");
        History.RemoveTab(tab);
        if (ReferenceEquals(_ctx.Tab, tab)) _ctx.Tab = null;
        return true;
    }

    public object RenameTab(object[] a)
    {
        if (a.Length < 2) throw new ArgumentException("renameTab: expected old and new name");
        var tab = History.FindTab(ToStr(a[0])) ?? throw new ArgumentException($"Tab \"{ToStr(a[0])}\" not found");
        if (!History.RenameTab(tab, ToStr(a[1]))) throw new ArgumentException("Cannot rename tab (name empty or already used)");
        return true;
    }

    public object TabIcon(object[] a)
    {
        if (a.Length == 0) throw new ArgumentException("tabIcon: expected tab name");
        var tab = History.FindTab(ToStr(a[0])) ?? throw new ArgumentException($"Tab \"{ToStr(a[0])}\" not found");
        if (a.Length == 1) return tab.Icon ?? "";
        History.SetTabIcon(tab, ToStr(a[1]));
        return true;
    }

    public object SelectedTab(object[] a) => (_host.Main.SelectedTab ?? History.ClipboardTab).Name;

    // ------------------------------------------------------------------
    // Seleção
    // ------------------------------------------------------------------

    public object SelectedItems(object[] a)
    {
        var tab = SelectionTab;
        return Selected.Select(i => tab.Items.IndexOf(i)).Where(i => i >= 0).Select(i => (object)i).ToArray();
    }

    public object CurrentItem(object[] a) => _host.Main.CurrentItemRow();

    public object? SelectedItemData(object[] a)
    {
        var item = Selected.ElementAtOrDefault(Int(a.Length > 0 ? a[0] : 0));
        return item is null ? null : ToJsData(item.DataWithNotes());
    }

    public object SetSelectedItemData(object[] a)
    {
        if (a.Length < 2) throw new ArgumentException("setSelectedItemData: expected index and data");
        var item = Selected.ElementAtOrDefault(Int(a[0]));
        if (item is null) return false;
        History.UpdateData(item, ToData(a[1]));
        return true;
    }

    public object SelectedItemsData(object[] a) =>
        Selected.Select(i => (object)ToJsData(i.DataWithNotes())).ToArray();

    public object SetSelectedItemsData(object[] a)
    {
        if (a.Length == 0) return false;
        var list = a[0] as object[] ?? a;
        var items = Selected;
        for (var i = 0; i < list.Length && i < items.Count; i++)
            History.UpdateData(items[i], ToData(list[i]));
        return true;
    }

    // ------------------------------------------------------------------
    // Dados de entrada
    // ------------------------------------------------------------------

    public object Str(object[] a) => ToStr(a.Length > 0 ? a[0] : null);

    public object Input(object[] a)
    {
        if (_ctx.Stdin is not null) return _ctx.Stdin;
        if (_ctx.InputData is not null && _ctx.InputData.TryGetValue(_ctx.InputMime, out var b)) return FromBytes(_ctx.InputMime, b);
        return "";
    }

    public object Data(object[] a)
    {
        var mime = a.Length > 0 ? ToStr(a[0]) : Mime.Text;
        var source = _ctx.InputData ?? _host.ReadClipboard(all: true)?.Data;
        return source is not null && source.TryGetValue(mime, out var b) ? FromBytes(mime, b) : "";
    }

    public object SetData(object[] a)
    {
        if (a.Length < 2) throw new ArgumentException("setData: expected format and value");
        _ctx.InputData ??= _host.ReadClipboard(all: true)?.Data ?? new Dictionary<string, byte[]>();
        _ctx.InputData[ToStr(a[0])] = Bytes(a[1]);
        return true;
    }

    public object RemoveData(object[] a)
    {
        if (a.Length < 1) throw new ArgumentException("removeData: expected format");
        _ctx.InputData ??= _host.ReadClipboard(all: true)?.Data ?? new Dictionary<string, byte[]>();
        return _ctx.InputData.Remove(ToStr(a[0]));
    }

    public object DataFormats(object[] a)
    {
        var source = _ctx.InputData ?? _host.ReadClipboard(all: true)?.Data ?? new Dictionary<string, byte[]>();
        return source.Keys.Select(k => (object)k).ToArray();
    }

    // ------------------------------------------------------------------
    // Janela / aplicação
    // ------------------------------------------------------------------

    public object Show(object[] a)
    {
        _host.Main.ShowAndActivate(a.Length > 0 ? ToStr(a[0]) : null);
        return true;
    }

    public object Hide(object[] a)
    {
        _host.Main.HideWindow();
        return true;
    }

    public object Toggle(object[] a) => _host.Main.ToggleWindow();

    public object Visible(object[] a) => _host.Main.IsVisible;

    public object Focused(object[] a) => _host.Main.IsActive;

    public object Menu(object[] a)
    {
        _host.Tray?.ShowMenu();
        return true;
    }

    public object Exit(object[] a)
    {
        _host.Dispatcher.InvokeAsync(() => _host.Exit(force: true));
        return true;
    }

    public object Disable(object[] a)
    {
        _host.SetMonitoring(false);
        return true;
    }

    public object Enable(object[] a)
    {
        _host.SetMonitoring(true);
        return true;
    }

    public object Monitoring(object[] a) => _host.MonitoringEnabled;

    public object Filter(object[] a)
    {
        _host.Main.SetFilter(a.Length > 0 ? ToStr(a[0]) : "");
        return true;
    }

    public object Ignore(object[] a)
    {
        _ctx.Ignore = true;
        return true;
    }

    public object Abort(object[] a) => throw new ScriptAbortException();

    public object Fail(object[] a)
    {
        _ctx.ExitCode = 1;
        return true;
    }

    public object UnknownCommand(string name) =>
        throw new ArgumentException($"Unknown command \"{name}\". Run \"kulycopy --help\" for usage.");

    public object Unload(object[] a) => true;

    // ------------------------------------------------------------------
    // Notificações
    // ------------------------------------------------------------------

    public object Popup(object[] a)
    {
        var title = a.Length > 0 ? ToStr(a[0]) : "KulyCopy";
        var message = a.Length > 1 ? ToStr(a[1]) : "";
        int? seconds = a.Length > 2 && TryInt(a[2], out var ms) && ms > 0 ? Math.Max(1, ms / 1000) : null;
        _host.Notify(title, message, seconds);
        return true;
    }

    public object Notification(object[] a)
    {
        string title = "KulyCopy", message = "";
        int? seconds = null;
        if (a.Length == 1) message = ToStr(a[0]);
        for (var i = 0; i + 1 < a.Length; i += 2)
        {
            var key = ToStr(a[i]);
            var value = a[i + 1];
            switch (key)
            {
                case ".title": title = ToStr(value); break;
                case ".message": message = ToStr(value); break;
                case ".time":
                    if (TryInt(value, out var ms) && ms > 0) seconds = Math.Max(1, ms / 1000);
                    break;
            }
        }
        _host.Notify(title, message, seconds);
        return true;
    }

    // ------------------------------------------------------------------
    // Import / export
    // ------------------------------------------------------------------

    public object ExportTab(object[] a)
    {
        if (a.Length == 0) throw new ArgumentException("exportTab: expected file name");
        TabExport.WriteFile(ToStr(a[0]), History.Export(CurrentTab));
        return true;
    }

    public object ImportTab(object[] a)
    {
        if (a.Length == 0) throw new ArgumentException("importTab: expected file name");
        var exported = TabExport.ReadFile(ToStr(a[0]));
        History.Import(exported, a.Length > 1 ? ToStr(a[1]) : null);
        return true;
    }

    // ------------------------------------------------------------------
    // Configuração
    // ------------------------------------------------------------------

    private static readonly System.Reflection.PropertyInfo[] SettingProps = typeof(AppSettings).GetProperties()
        .Where(p => p.CanRead && p.CanWrite && (p.PropertyType == typeof(string) || p.PropertyType == typeof(bool)
                                                || p.PropertyType == typeof(int) || p.PropertyType == typeof(double)))
        .ToArray();

    private static System.Reflection.PropertyInfo? FindSetting(string name)
    {
        var key = name.Replace("_", "").Replace("-", "").Replace(".", "");
        return SettingProps.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    public object Config(object[] a)
    {
        var settings = _host.Settings;
        if (a.Length == 0)
        {
            var lines = SettingProps.Select(p => p.Name + "=" + ToStr(p.GetValue(settings)));
            var hotkeys = settings.Hotkeys.Select(kv => "Hotkeys." + kv.Key + "=" + kv.Value);
            return string.Join("\n", lines.Concat(hotkeys));
        }

        var name = ToStr(a[0]);
        if (name.StartsWith("Hotkeys.", StringComparison.OrdinalIgnoreCase))
        {
            var key = name[8..];
            if (a.Length == 1) return settings.Hotkey(key);
            settings.Hotkeys[key] = ToStr(a[1]);
            _host.ApplySettings();
            return settings.Hotkey(key);
        }

        var prop = FindSetting(name) ?? throw new ArgumentException($"Unknown option \"{name}\"");
        if (a.Length == 1) return ToStr(prop.GetValue(settings));

        var raw = ToStr(a[1]);
        object value = prop.PropertyType == typeof(bool) ? raw.ToLowerInvariant() is "true" or "1" or "yes" or "on"
            : prop.PropertyType == typeof(int) ? int.Parse(raw, CultureInfo.InvariantCulture)
            : prop.PropertyType == typeof(double) ? double.Parse(raw, CultureInfo.InvariantCulture)
            : raw;
        prop.SetValue(settings, value);
        _host.ApplySettings();
        return ToStr(prop.GetValue(settings));
    }

    public object ToggleConfig(object[] a)
    {
        if (a.Length == 0) throw new ArgumentException("toggleConfig: expected option name");
        var prop = FindSetting(ToStr(a[0])) ?? throw new ArgumentException($"Unknown option \"{ToStr(a[0])}\"");
        if (prop.PropertyType != typeof(bool)) throw new ArgumentException("Option is not boolean");
        var value = !(bool)prop.GetValue(_host.Settings)!;
        prop.SetValue(_host.Settings, value);
        _host.ApplySettings();
        return value;
    }

    public object Settings(object[] a)
    {
        var custom = _host.Settings.Custom;
        if (a.Length == 0) return custom.Keys.Select(k => (object)k).ToArray();
        var key = ToStr(a[0]);
        if (a.Length == 1) return custom.TryGetValue(key, out var v) ? v : "";
        if (a[1] is null) custom.Remove(key);
        else custom[key] = ToStr(a[1]);
        _host.SaveSettings();
        return custom.TryGetValue(key, out var nv) ? nv : "";
    }

    public object Info(object[] a)
    {
        var all = new Dictionary<string, string>
        {
            ["version"] = AppInfo.Version,
            ["config"] = Paths.SettingsFile,
            ["commands"] = Paths.CommandsFile,
            ["data"] = Paths.DataDir,
            ["log"] = Paths.LogFile,
            ["exe"] = Environment.ProcessPath ?? "",
            ["portable"] = Paths.IsPortable ? "true" : "false",
        };
        if (a.Length == 0) return string.Join("\n", all.Select(kv => kv.Key + ": " + kv.Value));
        return all.TryGetValue(ToStr(a[0]), out var v) ? v : "";
    }

    public object Version(object[] a) => "KulyCopy " + AppInfo.Version;

    // ------------------------------------------------------------------
    // Script / ambiente
    // ------------------------------------------------------------------

    public object? Eval(object[] a)
    {
        if (a.Length == 0) return null;
        var value = _engine.Evaluate(ToStr(a[0]));
        return value.IsUndefined() || value.IsNull() ? null : value.ToObject();
    }

    public object Source(object[] a)
    {
        if (a.Length == 0) throw new ArgumentException("source: expected file name");
        _engine.Execute(File.ReadAllText(ToStr(a[0])));
        return true;
    }

    public object CurrentPath(object[] a)
    {
        if (a.Length > 0) Environment.CurrentDirectory = ToStr(a[0]);
        return Environment.CurrentDirectory;
    }

    public object Env(object[] a) => Environment.GetEnvironmentVariable(ToStr(a.Length > 0 ? a[0] : "")) ?? "";

    public object SetEnv(object[] a)
    {
        if (a.Length < 2) throw new ArgumentException("setEnv: expected name and value");
        Environment.SetEnvironmentVariable(ToStr(a[0]), ToStr(a[1]));
        return true;
    }

    public object Sleep(object[] a)
    {
        var ms = Math.Max(0, Int(a.Length > 0 ? a[0] : 0));
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            // Mantém a interface responsiva enquanto o script espera.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var remaining = (end - DateTime.UtcNow).TotalMilliseconds;
            if (remaining > 0) Thread.Sleep((int)Math.Min(15, remaining));
        }
        return true;
    }

    public object? Print(object[] a)
    {
        foreach (var x in a) _ctx.Stdout.Append(ToStr(x));
        return null;
    }

    public object ServerLog(object[] a)
    {
        Log.Script(string.Join(" ", a.Select(ToStr)));
        return true;
    }

    public object Logs(object[] a) => string.Join("\n", Log.Snapshot());

    public object CurrentWindowTitle(object[] a) => _ctx.WindowTitle ?? _host.CurrentWindowTitle();

    public object DateString(object[] a)
    {
        var format = a.Length > 0 ? ToStr(a[0]) : "yyyy-MM-dd HH:mm:ss";
        // Formato estilo Qt: "hh" é 24h quando não há AM/PM.
        if (!format.Contains("AP", StringComparison.OrdinalIgnoreCase) && !format.Contains('t'))
            format = format.Replace("hh", "HH");
        format = format.Replace("zzz", "fff");
        try { return DateTime.Now.ToString(format, CultureInfo.InvariantCulture); }
        catch (FormatException) { return DateTime.Now.ToString(CultureInfo.InvariantCulture); }
    }

    // ------------------------------------------------------------------
    // Processos / rede (ponte via JSON)
    // ------------------------------------------------------------------

    public string? ExecuteJson(object[] a)
    {
        var parts = a.Where(x => x is not null).Select(ToStr).ToList();
        if (parts.Count == 0) throw new ArgumentException("execute: expected program name");

        var psi = new ProcessStartInfo
        {
            FileName = parts[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = _ctx.Stdin is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in parts.Skip(1)) psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            if (_ctx.Stdin is not null)
            {
                process.StandardInput.Write(_ctx.Stdin);
                process.StandardInput.Close();
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _host.Settings.ScriptTimeoutSeconds));
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(true); } catch { }
            }
            process.WaitForExit();
            return Json(new { stdout = stdoutTask.Result, stderr = stderrTask.Result, exit_code = process.ExitCode });
        }
        catch (Exception ex)
        {
            Log.Error("execute() failed", ex);
            return null;
        }
    }

    public string NetworkGetJson(string url)
    {
        try
        {
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new
            {
                data = body,
                status = (int)response.StatusCode,
                error = response.IsSuccessStatusCode ? null : response.ReasonPhrase,
                contentType = response.Content.Headers.ContentType?.ToString() ?? "",
            });
        }
        catch (Exception ex)
        {
            return Json(new { data = "", status = 0, error = ex.Message, contentType = "" });
        }
    }

    public string NetworkPostJson(string url, object? data)
    {
        try
        {
            using var content = new ByteArrayContent(Bytes(data));
            using var response = Http.PostAsync(url, content).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new
            {
                data = body,
                status = (int)response.StatusCode,
                error = response.IsSuccessStatusCode ? null : response.ReasonPhrase,
                contentType = response.Content.Headers.ContentType?.ToString() ?? "",
            });
        }
        catch (Exception ex)
        {
            return Json(new { data = "", status = 0, error = ex.Message, contentType = "" });
        }
    }

    public object Open(object[] a)
    {
        foreach (var x in a)
        {
            var target = ToStr(x);
            if (target.Length == 0) continue;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Diálogo
    // ------------------------------------------------------------------

    public string DialogJson(object[] a)
    {
        var title = "KulyCopy";
        var fields = new List<(string Label, string Default)>();
        var i = 0;
        while (i < a.Length)
        {
            var key = ToStr(a[i]);
            var value = i + 1 < a.Length ? ToStr(a[i + 1]) : "";
            if (key.StartsWith('.'))
            {
                if (key == ".title") title = value;
            }
            else
            {
                fields.Add((key, value));
            }
            i += 2;
        }
        if (fields.Count == 0) fields.Add(("", ""));

        var owner = _host.Main.IsVisible ? _host.Main : null;
        var values = InputDialog.Show(owner, title, fields);
        if (values is null) return "null";
        if (fields.Count == 1) return Json(values[0]);
        var result = new Dictionary<string, string>();
        for (var k = 0; k < fields.Count; k++) result[fields[k].Label] = values[k];
        return Json(result);
    }

    // ------------------------------------------------------------------
    // Comandos
    // ------------------------------------------------------------------

    public string CommandsJson() => CommandStore.Serialize(_host.Commands);

    public void SetCommandsJson(string json)
    {
        _host.Commands = CommandStore.Parse(json);
        _host.SaveCommands();
        _host.RegisterHotkeys();
    }

    public void AddCommandsJson(string json)
    {
        _host.Commands.AddRange(CommandStore.Parse(json));
        _host.SaveCommands();
        _host.RegisterHotkeys();
    }

    public string ImportCommandsJson(string text) => CommandStore.Serialize(CommandStore.Parse(text));

    public string ExportCommandsJson(string json) => CommandStore.Serialize(CommandStore.Parse(json));

    // ------------------------------------------------------------------
    // Teclado, mouse, tela
    // ------------------------------------------------------------------

    public object Keys(object[] a)
    {
        foreach (var x in a)
        {
            var sendKeys = ToSendKeys(ToStr(x));
            try { System.Windows.Forms.SendKeys.SendWait(sendKeys); }
            catch (Exception ex) { Log.Warn("keys(): " + ex.Message); }
        }
        return true;
    }

    private static string ToSendKeys(string text)
    {
        if (HotkeyParser.TryParse(text, out var hk))
        {
            var sb = new StringBuilder();
            if ((hk.Modifiers & Hotkey.ModControl) != 0) sb.Append('^');
            if ((hk.Modifiers & Hotkey.ModShift) != 0) sb.Append('+');
            if ((hk.Modifiers & Hotkey.ModAlt) != 0) sb.Append('%');
            var vk = hk.VirtualKey;
            if (vk is >= 0x41 and <= 0x5A) sb.Append(char.ToLowerInvariant((char)vk));
            else if (vk is >= 0x30 and <= 0x39) sb.Append((char)vk);
            else if (vk is >= 0x70 and <= 0x87) sb.Append("{F").Append(vk - 0x6F).Append('}');
            else
            {
                var name = vk switch
                {
                    0x0D => "{ENTER}", 0x09 => "{TAB}", 0x1B => "{ESC}", 0x08 => "{BACKSPACE}", 0x2D => "{INSERT}",
                    0x2E => "{DELETE}", 0x24 => "{HOME}", 0x23 => "{END}", 0x21 => "{PGUP}", 0x22 => "{PGDN}",
                    0x26 => "{UP}", 0x28 => "{DOWN}", 0x25 => "{LEFT}", 0x27 => "{RIGHT}", 0x20 => " ",
                    0x2C => "{PRTSC}", 0x13 => "{BREAK}",
                    _ => null,
                };
                if (name is null) return EscapeSendKeys(text);
                sb.Append(name);
            }
            return sb.ToString();
        }
        return EscapeSendKeys(text);
    }

    private static string EscapeSendKeys(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if ("+^%~(){}[]".Contains(c)) sb.Append('{').Append(c).Append('}');
            else if (c == '\n') sb.Append("{ENTER}");
            else if (c == '\t') sb.Append("{TAB}");
            else if (c != '\r') sb.Append(c);
        }
        return sb.ToString();
    }

    public object Screenshot(object[] a)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1, 1);
        using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return new ByteArray(ms.ToArray());
    }

    public object QueryKeyboardModifiers(object[] a)
    {
        var mods = new List<object>();
        if (NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL)) mods.Add("Ctrl");
        if (NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT)) mods.Add("Shift");
        if (NativeMethods.IsKeyDown(NativeMethods.VK_MENU)) mods.Add("Alt");
        if (NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN)) mods.Add("Meta");
        return mods.ToArray();
    }

    public object PointerPosition(object[] a)
    {
        NativeMethods.GetCursorPos(out var p);
        return new object[] { p.X, p.Y };
    }

    public object SetPointerPosition(object[] a)
    {
        if (a.Length < 2) throw new ArgumentException("setPointerPosition: expected x and y");
        return NativeMethods.SetCursorPos(Int(a[0]), Int(a[1]));
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    public object ToBase64(object[] a) => Convert.ToBase64String(Bytes(a.Length > 0 ? a[0] : null));

    public object FromBase64(object[] a)
    {
        try { return new ByteArray(Convert.FromBase64String(ToStr(a.Length > 0 ? a[0] : ""))); }
        catch (FormatException) { return new ByteArray(Array.Empty<byte>()); }
    }

    public object Md5sum(object[] a) => DataUtil.Hex(MD5.HashData(Bytes(a.Length > 0 ? a[0] : null)));
    public object Sha1sum(object[] a) => DataUtil.Hex(SHA1.HashData(Bytes(a.Length > 0 ? a[0] : null)));
    public object Sha256sum(object[] a) => DataUtil.Hex(SHA256.HashData(Bytes(a.Length > 0 ? a[0] : null)));
    public object Sha512sum(object[] a) => DataUtil.Hex(SHA512.HashData(Bytes(a.Length > 0 ? a[0] : null)));

    public object EscapeHtml(object[] a) => System.Net.WebUtility.HtmlEncode(ToStr(a.Length > 0 ? a[0] : ""));
}
