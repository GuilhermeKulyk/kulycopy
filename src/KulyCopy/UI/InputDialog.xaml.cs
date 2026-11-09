using System.Windows;
using System.Windows.Controls;
using KulyCopy.Services;

namespace KulyCopy.UI;

/// <summary>Diálogo genérico com um ou mais campos de texto (novo nome de aba, dialog() dos scripts etc.).</summary>
public partial class InputDialog : Window
{
    private readonly List<TextBox> _boxes = new();

    public string[] Values => _boxes.Select(b => b.Text).ToArray();

    public InputDialog(string title, IEnumerable<(string Label, string DefaultValue)> fields)
    {
        InitializeComponent();
        Title = title;
        ThemeManager.ApplyTitleBar(this);

        foreach (var (label, defaultValue) in fields)
        {
            if (!string.IsNullOrEmpty(label))
                Fields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox { Text = defaultValue ?? "", Margin = new Thickness(0, 0, 0, 10) };
            Fields.Children.Add(box);
            _boxes.Add(box);
        }

        Loaded += (_, _) =>
        {
            if (_boxes.Count == 0) return;
            _boxes[0].Focus();
            _boxes[0].SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    public static string[]? Show(Window? owner, string title, IEnumerable<(string Label, string DefaultValue)> fields)
    {
        var dialog = new InputDialog(title, fields);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Topmost = true;
        return dialog.ShowDialog() == true ? dialog.Values : null;
    }

    public static string? ShowSingle(Window? owner, string title, string label, string defaultValue = "")
    {
        var values = Show(owner, title, new[] { (label, defaultValue) });
        return values?[0];
    }
}
