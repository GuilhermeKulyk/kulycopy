using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using KulyCopy.Core;
using Microsoft.Win32;

namespace KulyCopy.UI;

/// <summary>Lista + formulário para editar os comandos (aba "Commands" das preferências).</summary>
public partial class CommandEditor : UserControl
{
    public ObservableCollection<Command> Commands { get; } = new();

    public CommandEditor()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void Load(IEnumerable<Command> commands)
    {
        Commands.Clear();
        foreach (var c in commands) Commands.Add(c);
        if (Commands.Count > 0) List.SelectedIndex = 0;
    }

    private Command? Selected => List.SelectedItem as Command;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var cmd = new Command { Name = "New command", InMenu = true };
        Commands.Add(cmd);
        List.SelectedItem = cmd;
        List.ScrollIntoView(cmd);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        var cmd = Selected;
        if (cmd is null) return;
        var index = Commands.IndexOf(cmd);
        Commands.Remove(cmd);
        if (Commands.Count > 0) List.SelectedIndex = Math.Clamp(index, 0, Commands.Count - 1);
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        var cmd = Selected;
        if (cmd is null) return;
        var copy = cmd.Clone();
        copy.Name += " (copy)";
        copy.GlobalShortcut = "";
        Commands.Insert(Commands.IndexOf(cmd) + 1, copy);
        List.SelectedItem = copy;
    }

    private void OnUp(object sender, RoutedEventArgs e) => Move(-1);

    private void OnDown(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        var cmd = Selected;
        if (cmd is null) return;
        var index = Commands.IndexOf(cmd);
        var target = index + delta;
        if (target < 0 || target >= Commands.Count) return;
        Commands.Move(index, target);
        List.SelectedItem = cmd;
        List.ScrollIntoView(cmd);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "KulyCopy commands (*.json)|*.json|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var imported = CommandStore.Parse(File.ReadAllText(dialog.FileName));
            foreach (var c in imported) Commands.Add(c);
            if (imported.Count > 0) List.SelectedItem = imported[^1];
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "Import failed: " + ex.Message, "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "kulycopy-commands.json", Filter = "KulyCopy commands (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, CommandStore.Serialize(Commands));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "Export failed: " + ex.Message, "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
