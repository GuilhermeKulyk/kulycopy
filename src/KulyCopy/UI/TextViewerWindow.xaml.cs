using System.Windows;
using KulyCopy.Clip;
using KulyCopy.Services;

namespace KulyCopy.UI;

/// <summary>Janela simples para textos longos (log, ajuda).</summary>
public partial class TextViewerWindow : Window
{
    private readonly Func<string>? _refresh;

    public TextViewerWindow(string title, string text, Func<string>? refresh = null, bool wrap = false)
    {
        InitializeComponent();
        Title = title;
        _refresh = refresh;
        Viewer.Text = text;
        if (wrap) Viewer.TextWrapping = TextWrapping.Wrap;
        RefreshButton.Visibility = refresh is null ? Visibility.Collapsed : Visibility.Visible;
        ThemeManager.ApplyTitleBar(this);
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_refresh is null) return;
        Viewer.Text = _refresh();
        Viewer.ScrollToEnd();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        ClipboardWriter.WriteText(Viewer.Text, ClipboardWriteMode.Skip);
    }

    public void ScrollToEnd() => Viewer.ScrollToEnd();
}
