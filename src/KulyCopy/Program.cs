using KulyCopy.Core;
using KulyCopy.Ipc;
using KulyCopy.Native;

namespace KulyCopy;

/// <summary>
/// Ponto de entrada. Sem argumentos inicia a interface (ou mostra a janela se já houver uma instância);
/// com argumentos funciona como cliente de linha de comando da instância em execução.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var list = args.ToList();
        var server = RemoveFlag(list, "--server", "-s");
        var show = RemoveFlag(list, "--show");
        var portable = RemoveFlag(list, "--portable");

        if (list.Count > 0 && list[0] is "--help" or "-h" or "help" or "/?")
        {
            ConsoleHelper.Attach();
            ConsoleHelper.WriteStdout(HelpText.Cli + Environment.NewLine);
            return 0;
        }

        if (list.Count > 0 && list[0] is "--version" or "-v" or "-V")
        {
            ConsoleHelper.Attach();
            ConsoleHelper.WriteStdout("KulyCopy " + AppInfo.Version + Environment.NewLine);
            return 0;
        }

        var cli = list.ToArray();

        if (!server)
        {
            string? stdin = null;
            if (cli.Length > 0)
            {
                ConsoleHelper.Attach();
                stdin = ConsoleHelper.ReadStdinIfRedirected();
                if (cli[0] == "-" && stdin is null) stdin = "";
            }

            var request = new IpcRequest
            {
                Args = cli.Length == 0 ? new[] { "toggle" } : cli,
                Stdin = stdin,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            var response = IpcClient.TrySend(request);
            if (response is not null)
            {
                if (cli.Length > 0)
                {
                    if (response.StdoutBase64 is not null) ConsoleHelper.WriteStdout(Convert.FromBase64String(response.StdoutBase64));
                    if (response.Stdout.Length > 0) ConsoleHelper.WriteStdout(response.Stdout);
                    if (response.Stderr.Length > 0) ConsoleHelper.WriteStderr(response.Stderr);
                }
                return response.ExitCode;
            }

            if (cli.Length > 0)
            {
                ConsoleHelper.WriteStderr("KulyCopy is not running. Start it first (run \"kulycopy\" without arguments)." + Environment.NewLine);
                return 2;
            }
        }
        else if (IpcClient.TrySend(new IpcRequest { Args = new[] { "version" } }) is not null)
        {
            ConsoleHelper.Attach();
            ConsoleHelper.WriteStderr("KulyCopy is already running." + Environment.NewLine);
            return 1;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        Paths.Initialize(portable);

        var app = new App { ShowAtStart = show };
        app.InitializeComponent();
        return app.Run();
    }

    private static bool RemoveFlag(List<string> args, params string[] names)
    {
        var found = false;
        foreach (var name in names)
            found |= args.RemoveAll(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) > 0;
        return found;
    }
}
