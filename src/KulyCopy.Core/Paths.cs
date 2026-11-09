namespace KulyCopy.Core;

/// <summary>
/// Caminhos de configuração e dados. Em modo portátil (arquivo "portable" ao lado do executável
/// ou argumento --portable) tudo fica na pasta "data" junto do programa.
/// </summary>
public static class Paths
{
    public const string AppName = "KulyCopy";

    private static bool _initialized;
    private static bool _portable;

    public static bool IsPortable => _portable;

    public static string ConfigDir { get; private set; } = "";
    public static string DataDir { get; private set; } = "";

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string CommandsFile => Path.Combine(ConfigDir, "commands.json");
    public static string DatabaseFile => Path.Combine(DataDir, "history.db");
    public static string LogFile => Path.Combine(DataDir, "kulycopy.log");
    public static string ExeDir => AppContext.BaseDirectory;

    public static void Initialize(bool forcePortable = false)
    {
        if (_initialized) return;
        _initialized = true;

        _portable = forcePortable || File.Exists(Path.Combine(ExeDir, "portable"));

        if (_portable)
        {
            ConfigDir = Path.Combine(ExeDir, "data");
            DataDir = ConfigDir;
        }
        else
        {
            ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        }

        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(DataDir);
    }

    /// <summary>Usado só em testes: aponta tudo para uma pasta temporária.</summary>
    public static void InitializeForTests(string dir)
    {
        _initialized = true;
        _portable = true;
        ConfigDir = dir;
        DataDir = dir;
        Directory.CreateDirectory(dir);
    }
}
