using System.Text;
using Jint;
using Jint.Runtime;
using KulyCopy.Core;
using KulyCopy.Services;

namespace KulyCopy.Scripting;

/// <summary>
/// Executa scripts JavaScript (Jint) com a API no estilo do CopyQ. Cada execução usa um engine novo,
/// com o prelúdio (funções globais) e os comandos marcados como "script" (definições do usuário).
/// </summary>
public sealed class ScriptHost
{
    private readonly AppHost _host;

    public ScriptHost(AppHost host) => _host = host;

    /// <summary>Funções simples: nome em JavaScript → método de ScriptApi (recebe object[]).</summary>
    private static readonly (string Js, string Cs)[] SimpleFunctions =
    {
        ("copy", "Copy"), ("paste", "Paste"), ("read", "Read"), ("write", "Write"), ("change", "Change"),
        ("add", "Add"), ("insert", "Insert"), ("remove", "Remove"), ("edit", "Edit"), ("select", "Select"),
        ("move", "Move"), ("tab", "Tab"), ("removeTab", "RemoveTab"), ("renameTab", "RenameTab"), ("tabIcon", "TabIcon"),
        ("length", "Length"), ("size", "Length"), ("count", "Length"),
        ("selectedTab", "SelectedTab"), ("currentTab", "SelectedTab"), ("selectedItems", "SelectedItems"),
        ("currentItem", "CurrentItem"), ("index", "CurrentItem"),
        ("selectedItemData", "SelectedItemData"), ("setSelectedItemData", "SetSelectedItemData"),
        ("selectedItemsData", "SelectedItemsData"), ("setSelectedItemsData", "SetSelectedItemsData"),
        ("str", "Str"), ("input", "Input"), ("data", "Data"), ("setData", "SetData"), ("removeData", "RemoveData"),
        ("dataFormats", "DataFormats"),
        ("show", "Show"), ("hide", "Hide"), ("toggle", "Toggle"), ("visible", "Visible"), ("focused", "Focused"),
        ("menu", "Menu"), ("exit", "Exit"), ("disable", "Disable"), ("enable", "Enable"), ("monitoring", "Monitoring"),
        ("filter", "Filter"), ("ignore", "Ignore"), ("abort", "Abort"), ("fail", "Fail"),
        ("clipboard", "Clipboard"), ("selection", "Clipboard"), ("hasClipboardFormat", "HasClipboardFormat"),
        ("popup", "Popup"), ("notification", "Notification"),
        ("exportTab", "ExportTab"), ("importTab", "ImportTab"),
        ("config", "Config"), ("toggleConfig", "ToggleConfig"), ("info", "Info"),
        ("eval", "Eval"), ("source", "Source"), ("currentPath", "CurrentPath"), ("env", "Env"), ("setEnv", "SetEnv"),
        ("sleep", "Sleep"), ("print", "Print"), ("serverLog", "ServerLog"), ("logs", "Logs"),
        ("currentWindowTitle", "CurrentWindowTitle"), ("settings", "Settings"), ("dateString", "DateString"),
        ("version", "Version"), ("keys", "Keys"), ("screenshot", "Screenshot"),
        ("toBase64", "ToBase64"), ("fromBase64", "FromBase64"),
        ("md5sum", "Md5sum"), ("sha1sum", "Sha1sum"), ("sha256sum", "Sha256sum"), ("sha512sum", "Sha512sum"),
        ("escapeHtml", "EscapeHtml"), ("queryKeyboardModifiers", "QueryKeyboardModifiers"),
        ("pointerPosition", "PointerPosition"), ("setPointerPosition", "SetPointerPosition"),
        ("open", "Open"), ("pin", "Pin"), ("unpin", "Unpin"), ("unload", "Unload"), ("forceUnload", "Unload"),
        ("synchronizeToSelection", "Unload"), ("synchronizeFromSelection", "Unload"),
    };

    private static readonly Lazy<string> PreludeSource = new(BuildPrelude);

    private static string BuildPrelude()
    {
        var sb = new StringBuilder();
        sb.AppendLine("var mimeText = 'text/plain', mimeHtml = 'text/html', mimeRtf = 'text/rtf', mimeUriList = 'text/uri-list',");
        sb.AppendLine("    mimePng = 'image/png', mimeItemNotes = 'application/x-kulycopy-item-notes',");
        sb.AppendLine("    mimeOwner = 'application/x-kulycopy-owner', mimeWindowTitle = 'application/x-kulycopy-owner-window-title',");
        sb.AppendLine("    mimeHidden = 'application/x-kulycopy-hidden', mimeIcon = 'application/x-kulycopy-item-icon';");
        sb.AppendLine("function __args(a) { return Array.prototype.slice.call(a); }");
        foreach (var (js, cs) in SimpleFunctions)
            sb.AppendLine($"function {js}() {{ return __api.{cs}(__args(arguments)); }}");

        // Funções que trocam objetos complexos via JSON.
        sb.AppendLine("function __fromJson(s) { return (s === null || s === undefined) ? undefined : JSON.parse(s); }");
        sb.AppendLine("function execute() { return __fromJson(__api.ExecuteJson(__args(arguments))); }");
        sb.AppendLine("function dialog() { return __fromJson(__api.DialogJson(__args(arguments))); }");
        sb.AppendLine("function commands() { return __fromJson(__api.CommandsJson()); }");
        sb.AppendLine("function __cmdList(a) { a = __args(a); return (a.length === 1 && Array.isArray(a[0])) ? a[0] : a; }");
        sb.AppendLine("function setCommands() { __api.SetCommandsJson(JSON.stringify(__cmdList(arguments))); }");
        sb.AppendLine("function addCommands() { __api.AddCommandsJson(JSON.stringify(__cmdList(arguments))); }");
        sb.AppendLine("function importCommands(text) { return __fromJson(__api.ImportCommandsJson(String(text))); }");
        sb.AppendLine("function exportCommands() { return __api.ExportCommandsJson(JSON.stringify(__cmdList(arguments))); }");
        sb.AppendLine("function networkGet(url) { return __fromJson(__api.NetworkGetJson(String(url))); }");
        sb.AppendLine("function networkPost(url, data) { return __fromJson(__api.NetworkPostJson(String(url), data)); }");
        sb.AppendLine("function networkGetAsync(url) { return networkGet(url); }");
        sb.AppendLine("function networkPostAsync(url, data) { return networkPost(url, data); }");
        return sb.ToString();
    }

    public static string Prelude => PreludeSource.Value;

    public ScriptResult Run(string script, ScriptContext ctx)
    {
        var result = new ScriptResult();
        Engine engine;
        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _host.Settings.ScriptTimeoutSeconds));
            engine = new Engine(options =>
            {
                options.TimeoutInterval(timeout);
                options.Strict(false);
                options.Interop.AllowGetType = false;
            });
        }
        catch (Exception ex)
        {
            result.Error = "Could not create script engine: " + ex.Message;
            result.ExitCode = 1;
            return result;
        }

        var api = new ScriptApi(_host, ctx, engine);
        try
        {
            engine.SetValue("__api", api);
            engine.SetValue("__arguments", ctx.Arguments);
            engine.SetValue("arguments", ctx.Arguments);
            engine.Execute(Prelude);

            // Comandos do tipo "script" definem funções/constantes para os demais.
            foreach (var cmd in _host.Commands.Where(c => c.Enabled && c.IsScript && c.HasScript).ToList())
            {
                try { engine.Execute(cmd.Cmd); }
                catch (Exception ex) { Log.Error($"Script command '{cmd.Name}' failed to load: {Describe(ex)}"); }
            }

            var prepared = Prepare(script);
            var value = engine.Evaluate(prepared);
            result.Value = value.IsUndefined() || value.IsNull() ? null : value.ToObject();
        }
        catch (ScriptAbortException)
        {
            result.Aborted = true;
        }
        catch (Exception ex) when (ex.InnerException is ScriptAbortException)
        {
            result.Aborted = true;
        }
        catch (JavaScriptException ex) when (ex.Error.ToObject() is ScriptAbortException)
        {
            result.Aborted = true;
        }
        catch (Exception ex)
        {
            result.Error = Describe(ex);
            Log.Script("Error: " + result.Error);
        }

        result.Stdout = ctx.Stdout.ToString();
        result.Ignored = ctx.Ignore;
        result.ExitCode = result.Error is not null ? 1 : ctx.ExitCode;
        return result;
    }

    /// <summary>
    /// Compila o script. Se falhar por causa de um "return" no nível superior (comum em scripts do CopyQ),
    /// embrulha em uma função chamada com os argumentos do script.
    /// </summary>
    private static Jint.Prepared<Acornima.Ast.Script> Prepare(string code)
    {
        try
        {
            return Engine.PrepareScript(code);
        }
        catch (Exception first)
        {
            try
            {
                return Engine.PrepareScript("(function() {\n" + code + "\n}).apply(null, __arguments);");
            }
            catch
            {
                throw first;
            }
        }
    }

    public static string Describe(Exception ex)
    {
        if (ex is JavaScriptException js)
        {
            var loc = js.Location;
            var where = loc.Start.Line > 0 ? $" (line {loc.Start.Line}, column {loc.Start.Column + 1})" : "";
            return js.Message + where;
        }
        if (ex is TimeoutException) return "Script timed out";
        if (ex.InnerException is not null && ex is System.Reflection.TargetInvocationException) return Describe(ex.InnerException);
        return ex.Message;
    }
}
