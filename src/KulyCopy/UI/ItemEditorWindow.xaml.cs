using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KulyCopy.Services;

namespace KulyCopy.UI;

/// <summary>Editor de texto de um item (ou de notas, ou de um item novo).</summary>
public partial class ItemEditorWindow : Window
{
    private bool _modal;
    private bool _saved;

    /// <summary>Disparado ao salvar; usado quando a janela é aberta sem bloquear (scripts).</summary>
    public event Action<string>? Saved;

    public string Text => Editor.Text;

    public ItemEditorWindow(string title, string text, string fontFamily)
    {
        InitializeComponent();
        Title = title;
        Editor.Text = text;
        Editor.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? "Consolas" : fontFamily);
        ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) =>
        {
            Editor.Focus();
            Editor.CaretIndex = Editor.Text.Length;
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None))
        {
            Save();
            e.Handled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (!_modal) Close();
    }

    private void Save()
    {
        if (_saved) return;
        _saved = true;
        Saved?.Invoke(Editor.Text);
        if (_modal) DialogResult = true;
        else Close();
    }

    /// <summary>Mostra como diálogo modal; retorna o texto salvo ou null se cancelado.</summary>
    public string? ShowModal(Window? owner)
    {
        _modal = true;
        if (owner is { IsVisible: true }) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return ShowDialog() == true ? Editor.Text : null;
    }
}
