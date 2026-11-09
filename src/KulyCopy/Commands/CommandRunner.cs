using System.Text.RegularExpressions;
using KulyCopy.Core;
using KulyCopy.Models;
using KulyCopy.Scripting;
using KulyCopy.Services;

namespace KulyCopy.Commands;

/// <summary>Resultado do processamento dos comandos automáticos para um novo conteúdo.</summary>
public sealed class ClipboardDecision
{
    public Dictionary<string, byte[]> Data { get; set; } = new();
    public List<string> Tabs { get; } = new();
    public bool Ignored { get; set; }
}

/// <summary>Aplica os comandos do usuário: automáticos (novo conteúdo), de menu e de atalho global.</summary>
public sealed class CommandRunner
{
    private readonly AppHost _host;

    public CommandRunner(AppHost host) => _host = host;

    private HistoryService History => _host.History;

    // ------------------------------------------------------------------

    public ClipboardDecision ProcessClipboard(Dictionary<string, byte[]> data, string windowTitle)
    {
        var decision = new ClipboardDecision { Data = data };
        decision.Tabs.Add(_host.Settings.ClipboardTab);
        var storeInClipboardTab = true;

        foreach (var cmd in _host.Commands.Where(c => c.Enabled && c.Automatic).ToList())
        {
            if (!Matches(cmd, decision.Data, windowTitle)) continue;

            if (!string.IsNullOrWhiteSpace(cmd.Tab)) decision.Tabs.Add(cmd.Tab.Trim());
            if (cmd.Remove) storeInClipboardTab = false;

            if (!cmd.HasScript) continue;

            var ctx = new ScriptContext
            {
                InputData = decision.Data,
                InputMime = string.IsNullOrWhiteSpace(cmd.Input) ? Mime.Text : cmd.Input.Trim(),
                Tab = History.GetOrCreateTab(string.IsNullOrWhiteSpace(cmd.Tab) ? _host.Settings.ClipboardTab : cmd.Tab),
                WindowTitle = windowTitle,
                Command = cmd,
            };
            var result = _host.Scripts.Run(cmd.Cmd, ctx);
            if (result.Error is not null) Log.Error($"Automatic command \"{cmd.Name}\" failed: {result.Error}");
            if (ctx.InputData is not null) decision.Data = ctx.InputData;
            if (ctx.Ignore)
            {
                decision.Ignored = true;
                break;
            }
            StoreOutput(cmd, result, decision.Data, intoItem: true);
        }

        if (!storeInClipboardTab) decision.Tabs.RemoveAll(t => t == _host.Settings.ClipboardTab);
        var distinct = decision.Tabs.Distinct().ToList();
        decision.Tabs.Clear();
        decision.Tabs.AddRange(distinct);
        return decision;
    }

    public bool Matches(Command cmd, IReadOnlyDictionary<string, byte[]> data, string? windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(cmd.Input) && !data.ContainsKey(cmd.Input.Trim())) return false;

        if (!string.IsNullOrWhiteSpace(cmd.Match))
        {
            var text = DataUtil.GetText(data) ?? "";
            if (!SafeIsMatch(cmd.Match, text)) return false;
        }

        if (!string.IsNullOrWhiteSpace(cmd.Window))
        {
            if (windowTitle is null || !SafeIsMatch(cmd.Window, windowTitle)) return false;
        }

        return true;
    }

    private static bool SafeIsMatch(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(300));
        }
        catch (Exception ex)
        {
            Log.Warn($"Invalid regular expression \"{pattern}\": {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------

    public IEnumerable<Command> MenuCommandsFor(IList<ClipboardItem> items)
    {
        if (items.Count == 0) yield break;
        var first = items[0];
        foreach (var cmd in _host.Commands.Where(c => c.Enabled && c.InMenu))
            if (Matches(cmd, first.Data, first.WindowTitle)) yield return cmd;
    }

    public void RunMenuCommand(Command cmd, TabViewModel tab, IList<ClipboardItem> selection)
    {
        var items = selection.Distinct().Where(tab.Items.Contains).OrderBy(tab.Items.IndexOf).ToList();
        if (items.Count == 0) return;

        if (cmd.HideWindow) _host.Main.HideWindow();

        if (!string.IsNullOrWhiteSpace(cmd.Tab))
        {
            var target = History.GetOrCreateTab(cmd.Tab);
            foreach (var item in Enumerable.Reverse(items))
                History.CopyItemToTab(item, target);
        }

        if (cmd.HasScript)
        {
            var first = items[0];
            var ctx = new ScriptContext
            {
                InputData = first.DataWithNotes(),
                InputMime = string.IsNullOrWhiteSpace(cmd.Input) ? Mime.Text : cmd.Input.Trim(),
                Tab = tab,
                SelectedItems = items,
                SelectionTab = tab,
                WindowTitle = first.WindowTitle,
                Command = cmd,
            };
            var result = _host.Scripts.Run(cmd.Cmd, ctx);
            if (result.Error is not null)
            {
                Log.Error($"Command \"{cmd.Name}\" failed: {result.Error}");
                _host.Notify("Command failed", cmd.Name + ": " + result.Error);
            }
            StoreOutput(cmd, result, null, intoItem: false, fallbackTab: tab);
        }

        if (cmd.Remove) History.RemoveItems(tab, items);
    }

    public void RunGlobalShortcutCommand(Command cmd)
    {
        _host.RememberForegroundWindow();
        var snapshot = _host.ReadClipboard(all: true);
        var data = snapshot?.Data ?? new Dictionary<string, byte[]>();
        var tab = _host.Main.SelectedTab ?? History.ClipboardTab;

        if (cmd.HasScript)
        {
            var ctx = new ScriptContext
            {
                InputData = data,
                InputMime = string.IsNullOrWhiteSpace(cmd.Input) ? Mime.Text : cmd.Input.Trim(),
                Tab = tab,
                WindowTitle = _host.CurrentWindowTitle(),
                Command = cmd,
            };
            var result = _host.Scripts.Run(cmd.Cmd, ctx);
            if (result.Error is not null)
            {
                Log.Error($"Global shortcut command \"{cmd.Name}\" failed: {result.Error}");
                _host.Notify("Command failed", cmd.Name + ": " + result.Error);
            }
            if (ctx.InputData is not null) data = ctx.InputData;
            StoreOutput(cmd, result, null, intoItem: false, fallbackTab: tab);
        }

        if (!string.IsNullOrWhiteSpace(cmd.Tab) && data.Count > 0)
            History.AddItem(History.GetOrCreateTab(cmd.Tab), DataUtil.Clone(data), dedupe: true);
    }

    /// <summary>Script avulso do diálogo "Action" (F5), com os itens selecionados como entrada.</summary>
    public ScriptResult RunAdHoc(string script, TabViewModel tab, IList<ClipboardItem> selection, string? outputTab)
    {
        var items = selection.Distinct().Where(tab.Items.Contains).OrderBy(tab.Items.IndexOf).ToList();
        var ctx = new ScriptContext
        {
            InputData = items.FirstOrDefault()?.DataWithNotes(),
            Tab = tab,
            SelectedItems = items,
            SelectionTab = tab,
            WindowTitle = items.FirstOrDefault()?.WindowTitle,
        };
        var result = _host.Scripts.Run(script, ctx);
        if (result.Error is null && !string.IsNullOrWhiteSpace(outputTab))
        {
            var output = OutputBytes(result);
            if (output is not null)
                History.AddItem(History.GetOrCreateTab(outputTab), new Dictionary<string, byte[]> { [Mime.Text] = output }, dedupe: true);
        }
        return result;
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Guarda a saída do script conforme os campos Output/OutputTab do comando:
    /// em OutputTab vira um item novo; só Output em comando automático grava no próprio item.
    /// </summary>
    private void StoreOutput(Command cmd, ScriptResult result, Dictionary<string, byte[]>? itemData, bool intoItem, TabViewModel? fallbackTab = null)
    {
        if (result.Error is not null) return;
        var output = OutputBytes(result);
        if (output is null) return;

        var mime = string.IsNullOrWhiteSpace(cmd.Output) ? Mime.Text : cmd.Output.Trim();

        if (!string.IsNullOrWhiteSpace(cmd.OutputTab))
        {
            var tab = History.GetOrCreateTab(cmd.OutputTab);
            History.AddItem(tab, new Dictionary<string, byte[]> { [mime] = output }, dedupe: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(cmd.Output)) return;

        if (intoItem && itemData is not null) itemData[mime] = output;
        else if (fallbackTab is not null)
            History.AddItem(fallbackTab, new Dictionary<string, byte[]> { [mime] = output }, dedupe: true);
    }

    private static byte[]? OutputBytes(ScriptResult result)
    {
        if (result.Value is ByteArray bytes) return bytes.Bytes;
        var text = result.Stdout + (result.Value is null ? "" : ScriptApi.ToStr(result.Value));
        return text.Length == 0 ? null : DataUtil.Bytes(text);
    }
}
