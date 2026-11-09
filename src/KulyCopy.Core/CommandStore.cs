using System.Text.Json;
using System.Text.Json.Serialization;

namespace KulyCopy.Core;

/// <summary>Carrega e salva a lista de comandos em um arquivo JSON legível (commands.json).</summary>
public static class CommandStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<Command> Load(string path)
    {
        if (!File.Exists(path)) return Defaults();
        try
        {
            var json = File.ReadAllText(path);
            return Parse(json);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load commands, using defaults", ex);
            return Defaults();
        }
    }

    public static void Save(string path, IEnumerable<Command> commands)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(commands));
        File.Move(tmp, path, overwrite: true);
    }

    public static string Serialize(IEnumerable<Command> commands) =>
        JsonSerializer.Serialize(commands.ToList(), JsonOptions);

    public static List<Command> Parse(string json)
    {
        json = json.Trim();
        if (json.Length == 0) return new List<Command>();
        // Aceita tanto um array quanto um único objeto.
        if (json.StartsWith('{'))
        {
            var one = JsonSerializer.Deserialize<Command>(json, JsonOptions);
            return one is null ? new List<Command>() : new List<Command> { one };
        }
        return JsonSerializer.Deserialize<List<Command>>(json, JsonOptions) ?? new List<Command>();
    }

    /// <summary>Comandos de exemplo instalados na primeira execução (a maioria desligada, como no CopyQ).</summary>
    public static List<Command> Defaults() => new()
    {
        new Command
        {
            Name = "Ignore password managers",
            Enabled = true,
            Automatic = true,
            Window = "(?i)(KeePass|Bitwarden|1Password|LastPass|Dashlane|Password|Senha)",
            Cmd = "ignore()",
            Icon = "lock",
        },
        new Command
        {
            Name = "Open in browser",
            Enabled = true,
            InMenu = true,
            Match = @"^\s*(https?|ftp)://\S+\s*$",
            Input = Mime.Text,
            Cmd = "open(str(data(mimeText)).trim())",
            Shortcut = "Ctrl+Shift+O",
            Icon = "globe",
        },
        new Command
        {
            Name = "Move to Trash",
            Enabled = true,
            InMenu = true,
            Tab = "(trash)",
            Remove = true,
            Shortcut = "Ctrl+Delete",
            Icon = "trash",
        },
        new Command
        {
            Name = "Copy URLs to tab 'URLs'",
            Enabled = false,
            Automatic = true,
            Match = @"^\s*(https?|ftp)://\S+\s*$",
            Tab = "URLs",
            Icon = "link",
        },
        new Command
        {
            Name = "Ignore items with no or single character",
            Enabled = false,
            Automatic = true,
            Match = @"^(.|\s*)$",
            Cmd = "ignore()",
        },
        new Command
        {
            Name = "Paste as plain text",
            Enabled = false,
            IsGlobalShortcut = true,
            GlobalShortcut = "Ctrl+Shift+Alt+V",
            Cmd = "copy(str(clipboard(mimeText)))\npaste()",
            Icon = "paste",
        },
        new Command
        {
            Name = "Add to tab 'Tasks'",
            Enabled = false,
            InMenu = true,
            Tab = "Tasks",
            Icon = "task",
        },
    };
}
