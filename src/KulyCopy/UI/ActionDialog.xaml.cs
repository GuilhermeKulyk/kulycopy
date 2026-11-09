using System.Windows;
using System.Windows.Input;
using KulyCopy.Models;
using KulyCopy.Scripting;
using KulyCopy.Services;

namespace KulyCopy.UI;

/// <summary>Diálogo "Action" (F5): roda um script avulso sobre os itens selecionados.</summary>
public partial class ActionDialog : Window
{
    private const string LastScriptKey = "action.lastScript";
    private const string LastOutputTabKey = "action.lastOutputTab";

    private readonly AppHost _host;
    private readonly TabViewModel _tab;
    private readonly List<ClipboardItem> _items;

    public ActionDialog(AppHost host, TabViewModel tab, List<ClipboardItem> items)
    {
        _host = host;
        _tab = tab;
        _items = items;
        InitializeComponent();
        ThemeManager.ApplyTitleBar(this);

        Script.Text = host.Settings.Custom.TryGetValue(LastScriptKey, out var last) ? last : "str(data(mimeText)).toUpperCase()";
        OutputTab.Text = host.Settings.Custom.TryGetValue(LastOutputTabKey, out var lastTab) ? lastTab : "";
        Loaded += (_, _) =>
        {
            Script.Focus();
            Script.CaretIndex = Script.Text.Length;
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Run();
            e.Handled = true;
        }
    }

    private void OnRun(object sender, RoutedEventArgs e) => Run();

    private void Run()
    {
        var script = Script.Text;
        var outputTab = SaveOutput.IsChecked == true ? OutputTab.Text.Trim() : null;
        if (outputTab is { Length: 0 }) outputTab = _tab.Name;

        _host.Settings.Custom[LastScriptKey] = script;
        _host.Settings.Custom[LastOutputTabKey] = OutputTab.Text;
        _host.SaveSettings();

        var result = _host.Runner.RunAdHoc(script, _tab, _items, outputTab);
        var text = result.Stdout;
        if (result.Value is not null) text += ScriptApi.ToStr(result.Value);
        if (result.Error is not null) text += (text.Length > 0 ? Environment.NewLine : "") + "Error: " + result.Error;
        if (result.Aborted) text += (text.Length > 0 ? Environment.NewLine : "") + "(aborted)";
        Output.Text = text.Length == 0 ? "(no output)" : text;
    }
}
