using System.Windows;
using System.Windows.Threading;
using KulyCopy.Core;
using KulyCopy.Services;

namespace KulyCopy;

public partial class App : Application
{
    /// <summary>Mostra a janela principal logo ao iniciar (argumento --show).</summary>
    public bool ShowAtStart { get; set; }

    public AppHost? Host { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        // Logoff/desligamento do Windows: fecha de verdade em vez de só esconder a janela.
        SessionEnding += (_, _) => Host?.Exit(force: true);

        try
        {
            Host = new AppHost();
            Host.Start(ShowAtStart);
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            MessageBox.Show("KulyCopy failed to start:" + Environment.NewLine + Environment.NewLine + ex,
                "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Host?.Shutdown();
        base.OnExit(e);
    }
}
