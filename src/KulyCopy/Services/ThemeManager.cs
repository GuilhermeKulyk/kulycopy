using System.Windows;
using System.Windows.Interop;
using KulyCopy.Native;
using Microsoft.Win32;

namespace KulyCopy.Services;

/// <summary>Troca o dicionário de cores (claro/escuro) em tempo de execução e ajusta a barra de título.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    public static void Apply(string theme)
    {
        var dark = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                   || (!theme.Equals("Light", StringComparison.OrdinalIgnoreCase) && SystemUsesDarkTheme());
        IsDark = dark;

        var uri = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute);
        var dict = new ResourceDictionary { Source = uri };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Add(dict);

        foreach (Window window in Application.Current.Windows)
            ApplyTitleBar(window);

        ThemeChanged?.Invoke();
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Barra de título escura (Windows 10 1809+ / 11). Pode ser chamado antes da janela ter HWND.</summary>
    public static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        var value = IsDark ? 1 : 0;
        try { NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)); }
        catch { /* versões antigas do Windows não têm o atributo */ }
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window w) ApplyTitleBar(w);
    }
}
