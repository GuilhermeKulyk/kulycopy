using System.Windows;
using KulyCopy.Core;
using KulyCopy.Services;

namespace KulyCopy.UI;

public partial class PreferencesWindow : Window
{
    private readonly AppHost _host;
    private readonly AppSettings _settings;

    public PreferencesWindow(AppHost host, int tabIndex)
    {
        _host = host;
        _settings = host.Settings.Clone();
        InitializeComponent();
        DataContext = _settings;
        CommandEditor.Load(host.Commands.Select(c => c.Clone()));
        if (Autostart.IsManagedByWindows)
        {
            AutostartCheck.IsEnabled = false;
            AutostartHint.Visibility = Visibility.Visible;
        }
        SelectTab(tabIndex);
        ThemeManager.ApplyTitleBar(this);
    }

    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.Items.Count) Tabs.SelectedIndex = index;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Apply()) Close();
    }

    private void OnApply(object sender, RoutedEventArgs e) => Apply();

    private bool Apply()
    {
        // Força o commit dos TextBox com foco (binding padrão só atualiza ao perder o foco).
        FocusManager_Commit();

        var problems = new List<string>();
        foreach (var (key, label) in HotkeyActions.All)
        {
            var value = _settings.Hotkey(key);
            if (!string.IsNullOrWhiteSpace(value) && !HotkeyParser.TryParse(value, out _))
                problems.Add($"Invalid shortcut \"{value}\" for \"{label}\".");
        }
        foreach (var cmd in CommandEditor.Commands)
        {
            if (!string.IsNullOrWhiteSpace(cmd.GlobalShortcut) && !HotkeyParser.TryParse(cmd.GlobalShortcut, out _))
                problems.Add($"Invalid global shortcut \"{cmd.GlobalShortcut}\" in command \"{cmd.Name}\".");
            if (!string.IsNullOrWhiteSpace(cmd.Shortcut) && !HotkeyParser.TryParse(cmd.Shortcut, out _))
                problems.Add($"Invalid shortcut \"{cmd.Shortcut}\" in command \"{cmd.Name}\".");
        }
        if (_settings.MaxItems < 1) problems.Add("Maximum number of items must be at least 1.");
        if (string.IsNullOrWhiteSpace(_settings.ClipboardTab)) problems.Add("Clipboard tab name cannot be empty.");

        if (problems.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, problems), "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _host.ReplaceSettings(_settings);
        _host.Commands = CommandEditor.Commands.Select(c => c.Clone()).ToList();
        _host.SaveCommands();
        _host.ApplySettings();
        return true;
    }

    private void FocusManager_Commit()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox box)
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }
}
