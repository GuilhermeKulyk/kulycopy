using System.Diagnostics;
using System.Windows;
using KulyCopy.Core;
using KulyCopy.Services;

namespace KulyCopy.UI;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "Version " + AppInfo.Version + (Paths.IsPortable ? " (portable)" : "");
        PathsText.Text = "Settings: " + Paths.ConfigDir + Environment.NewLine + "History: " + Paths.DataDir;
        ThemeManager.ApplyTitleBar(this);
    }

    private void OnOpenConfig(object sender, RoutedEventArgs e) => OpenFolder(Paths.ConfigDir);

    private void OnOpenData(object sender, RoutedEventArgs e) => OpenFolder(Paths.DataDir);

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open folder", ex);
        }
    }
}
