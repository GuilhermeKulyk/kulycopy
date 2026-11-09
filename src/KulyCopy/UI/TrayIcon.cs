using System.Reflection;
using KulyCopy.Core;
using KulyCopy.Models;
using KulyCopy.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace KulyCopy.UI;

/// <summary>Ícone da bandeja (WinForms NotifyIcon) com menu de itens recentes e ações rápidas.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppHost _host;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly Drawing.Icon? _onIcon;
    private readonly Drawing.Icon? _offIcon;

    public TrayIcon(AppHost host)
    {
        _host = host;
        _onIcon = LoadIcon("kulycopy.ico");
        _offIcon = LoadIcon("kulycopy-off.ico");

        _menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        _menu.Opening += (_, _) => Rebuild();

        _icon = new WinForms.NotifyIcon
        {
            Icon = _onIcon ?? Drawing.SystemIcons.Application,
            Text = "KulyCopy",
            ContextMenuStrip = _menu,
            Visible = host.Settings.ShowTray,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) _host.Main.ToggleWindow();
        };
        _icon.BalloonTipClicked += (_, _) => _host.Main.ShowAndActivate(null);

        ApplyTheme();
    }

    private static Drawing.Icon? LoadIcon(string name)
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/" + name, UriKind.Absolute);
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info is null) return null;
            using var stream = info.Stream;
            return new Drawing.Icon(stream);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not load tray icon: " + ex.Message);
            return null;
        }
    }

    public void ApplyTheme()
    {
        if (ThemeManager.IsDark)
        {
            _menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColorTable());
            _menu.ForeColor = Drawing.Color.FromArgb(0xE6, 0xE7, 0xEB);
            _menu.BackColor = Drawing.Color.FromArgb(0x25, 0x26, 0x2D);
        }
        else
        {
            _menu.Renderer = new WinForms.ToolStripProfessionalRenderer();
            _menu.ForeColor = Drawing.SystemColors.MenuText;
            _menu.BackColor = Drawing.SystemColors.Menu;
        }
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        var settings = _host.Settings;

        var tab = string.IsNullOrWhiteSpace(settings.TrayTab)
            ? _host.History.ClipboardTab
            : _host.History.FindTab(settings.TrayTab) ?? _host.History.ClipboardTab;

        var count = Math.Min(settings.TrayItems, tab.Items.Count);
        for (var i = 0; i < count; i++)
        {
            var item = tab.Items[i];
            var label = (i < 10 ? "&" + i + "  " : "    ") + Escape(item.SingleLine(64));
            var menuItem = new WinForms.ToolStripMenuItem(label) { ForeColor = _menu.ForeColor };
            var captured = item;
            menuItem.Click += (_, _) => ActivateFromTray(tab, captured);
            if (item.HasImage && item.Thumbnail is not null) menuItem.ToolTipText = item.Summary;
            _menu.Items.Add(menuItem);
        }
        if (count > 0) _menu.Items.Add(new WinForms.ToolStripSeparator());

        Add(_host.Main.IsVisible ? "&Hide main window" : "&Show main window", () => _host.Main.ToggleWindow());
        Add(_host.MonitoringEnabled ? "&Disable clipboard storing" : "&Enable clipboard storing", _host.ToggleMonitoring);
        Add("&New item...", () => _host.OpenEditorForItem(_host.History.ClipboardTab, null));
        Add("Show &clipboard content", _host.ShowClipboardContent);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        Add("&Preferences...", () => _host.ShowPreferences(0));
        Add("&Commands...", () => _host.ShowPreferences(5));
        Add("&About", () =>
        {
            var about = new AboutWindow();
            if (_host.Main.IsVisible) about.Owner = _host.Main;
            else about.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            about.ShowDialog();
        });
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        Add("E&xit", () => _host.Exit());
    }

    private void Add(string text, Action action)
    {
        var item = new WinForms.ToolStripMenuItem(text) { ForeColor = _menu.ForeColor };
        item.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { Log.Error("Tray action failed", ex); }
        };
        _menu.Items.Add(item);
    }

    private void ActivateFromTray(TabViewModel tab, ClipboardItem item)
    {
        // O clique no menu devolve o foco à janela anterior; lembramos dela antes de colar.
        _host.RememberForegroundWindow();
        if (_host.Settings.TrayPaste && _host.Settings.PasteAfterActivate)
            _host.ActivateItems(tab, new[] { item });
        else
            _host.CopyItemsToClipboard(tab, new[] { item });
    }

    private static string Escape(string s) => s.Replace("&", "&&");

    /// <summary>Abre o menu da bandeja na posição do cursor (atalho global "Show tray menu").</summary>
    public void ShowMenu()
    {
        try
        {
            var method = typeof(WinForms.NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method is not null)
            {
                method.Invoke(_icon, null);
                return;
            }
        }
        catch { /* cai no fallback */ }
        Rebuild();
        _menu.Show(WinForms.Cursor.Position);
    }

    public void SetMonitoring(bool enabled)
    {
        _icon.Icon = (enabled ? _onIcon : _offIcon) ?? Drawing.SystemIcons.Application;
        _icon.Text = enabled ? "KulyCopy" : "KulyCopy (clipboard storing disabled)";
    }

    public void SetVisible(bool visible) => _icon.Visible = visible;

    public void Balloon(string title, string text, int milliseconds)
    {
        if (!_icon.Visible) return;
        _icon.ShowBalloonTip(milliseconds, string.IsNullOrEmpty(title) ? "KulyCopy" : title,
            string.IsNullOrEmpty(text) ? " " : text, WinForms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _onIcon?.Dispose();
        _offIcon?.Dispose();
    }

    /// <summary>Cores do menu da bandeja no tema escuro.</summary>
    private sealed class DarkColorTable : WinForms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Background = Drawing.Color.FromArgb(0x25, 0x26, 0x2D);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(0x30, 0x32, 0x3B);
        private static readonly Drawing.Color Border = Drawing.Color.FromArgb(0x3C, 0x3E, 0x48);

        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Hover;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Border;
    }
}
