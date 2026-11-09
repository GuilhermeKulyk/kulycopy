using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KulyCopy.Core;
using KulyCopy.Models;
using KulyCopy.Native;
using KulyCopy.Services;
using Microsoft.Win32;

namespace KulyCopy.UI;

/// <summary>Janela principal: abas, lista de itens, busca e todos os atalhos de teclado.</summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppHost _host;
    private TabViewModel? _selectedTab;
    private bool _geometryReady;

    /// <summary>Permite fechar de verdade (caso contrário, fechar só esconde).</summary>
    internal bool AllowClose { get; set; }

    public ObservableCollection<TabViewModel> Tabs => _host.History.Tabs;

    public bool ShowRowNumbers => _host.Settings.ShowRowNumbers;

    public TextWrapping TextWrapping => _host.Settings.TextWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value)) return;
            if (_selectedTab is not null) _selectedTab.FilterText = "";
            _selectedTab = value;
            if (value is not null) value.FilterText = SearchBox.Text;
            OnPropertyChanged();
            Dispatcher.InvokeAsync(EnsureSelection, DispatcherPriority.Loaded);
        }
    }

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        DataContext = this;
        ThemeManager.ApplyTitleBar(this);
        ApplyAppearance();
        RestoreGeometry();

        Tabs.CollectionChanged += (_, _) =>
        {
            if (SelectedTab is null || !Tabs.Contains(SelectedTab)) SelectedTab = Tabs.FirstOrDefault();
        };
        SelectedTab = Tabs.FirstOrDefault(t => t.Name == host.Settings.ClipboardTab) ?? Tabs.FirstOrDefault();

        _host.MonitoringChanged += _ => UpdateMonitoringMenu();
        UpdateMonitoringMenu();
        Loaded += (_, _) => _geometryReady = true;
    }

    // ------------------------------------------------------------------
    // Aparência / geometria
    // ------------------------------------------------------------------

    public void ApplyAppearance()
    {
        var s = _host.Settings;
        FontFamily = new FontFamily(string.IsNullOrWhiteSpace(s.FontFamily) ? "Segoe UI" : s.FontFamily);
        FontSize = s.FontSize;
        Topmost = s.AlwaysOnTop;
        OnPropertyChanged(nameof(ShowRowNumbers));
        OnPropertyChanged(nameof(TextWrapping));
        foreach (var tab in Tabs)
            foreach (var item in tab.Items) item.InvalidatePreview();
    }

    private void RestoreGeometry()
    {
        var s = _host.Settings;
        if (s.WindowWidth >= MinWidth) Width = s.WindowWidth;
        if (s.WindowHeight >= MinHeight) Height = s.WindowHeight;

        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop) && IsOnScreen(s.WindowLeft, s.WindowTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private static bool IsOnScreen(double left, double top)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        return left >= vl - 50 && top >= vt - 50 && left < vl + vw - 100 && top < vt + vh - 100;
    }

    private void OnGeometryChanged(object sender, EventArgs e)
    {
        if (!_geometryReady || WindowState != WindowState.Normal || !IsVisible) return;
        var s = _host.Settings;
        s.WindowLeft = Left;
        s.WindowTop = Top;
        s.WindowWidth = Width;
        s.WindowHeight = Height;
    }

    private void UpdateMonitoringMenu()
    {
        MonitoringMenuItem.Header = _host.MonitoringEnabled ? "_Disable Clipboard Storing" : "_Enable Clipboard Storing";
    }

    // ------------------------------------------------------------------
    // Mostrar / esconder
    // ------------------------------------------------------------------

    public void ShowAndActivate(string? tabName)
    {
        _host.RememberForegroundWindow();
        if (!string.IsNullOrWhiteSpace(tabName)) SelectedTab = _host.History.GetOrCreateTab(tabName);

        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        WindowHelper.FocusWindow(new WindowInteropHelper(this).Handle);
        Topmost = true;
        Topmost = _host.Settings.AlwaysOnTop;

        EnsureSelection();
        FocusList();
    }

    public void HideWindow()
    {
        SearchBox.Text = "";
        Hide();
    }

    /// <summary>Alterna a janela; retorna true se ficou visível.</summary>
    public bool ToggleWindow()
    {
        if (IsVisible && IsActive)
        {
            HideWindow();
            return false;
        }
        ShowAndActivate(null);
        return true;
    }

    public void SetFilter(string text)
    {
        SearchBox.Text = text;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_host.Settings.HideOnUnfocus || AllowClose) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (IsActive || !IsVisible) return;
            if (OwnedWindows.Count > 0) return;
            if (WindowHelper.IsOwnWindow(NativeMethods.GetForegroundWindow())) return;
            HideWindow();
        }, DispatcherPriority.Background);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        HideWindow();
    }

    // ------------------------------------------------------------------
    // Seleção
    // ------------------------------------------------------------------

    private void EnsureSelection()
    {
        if (ItemList.Items.Count == 0) return;
        if (ItemList.SelectedIndex < 0 || !ItemList.Items.Contains(ItemList.SelectedItem))
            ItemList.SelectedIndex = 0;
        if (ItemList.SelectedItem is not null) ItemList.ScrollIntoView(ItemList.SelectedItem);
    }

    private void FocusList()
    {
        if (ItemList.SelectedIndex >= 0)
        {
            ItemList.UpdateLayout();
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(ItemList.SelectedIndex) is ListBoxItem container)
            {
                container.Focus();
                return;
            }
        }
        ItemList.Focus();
    }

    public IEnumerable<ClipboardItem> SelectedItemsOrdered()
    {
        var tab = SelectedTab;
        if (tab is null) return Enumerable.Empty<ClipboardItem>();
        return ItemList.SelectedItems.Cast<ClipboardItem>().OrderBy(tab.Items.IndexOf).ToList();
    }

    private ClipboardItem? CurrentItem =>
        ItemList.SelectedItem as ClipboardItem ?? (ItemList.Items.Count > 0 ? ItemList.Items[0] as ClipboardItem : null);

    public int CurrentItemRow()
    {
        var tab = SelectedTab;
        var item = CurrentItem;
        return tab is null || item is null ? -1 : tab.Items.IndexOf(item);
    }

    private void MoveSelection(int delta, bool absolute = false)
    {
        var count = ItemList.Items.Count;
        if (count == 0) return;
        var index = absolute ? delta : ItemList.SelectedIndex + delta;
        index = Math.Clamp(index, 0, count - 1);
        ItemList.SelectedIndex = index;
        ItemList.ScrollIntoView(ItemList.Items[index]);
    }

    private int VisibleRows()
    {
        if (ItemList.SelectedIndex < 0) return 10;
        var container = ItemList.ItemContainerGenerator.ContainerFromIndex(ItemList.SelectedIndex) as FrameworkElement;
        var rowHeight = container?.ActualHeight ?? 40;
        if (rowHeight < 10) rowHeight = 40;
        return Math.Max(1, (int)(ItemList.ActualHeight / rowHeight));
    }

    // ------------------------------------------------------------------
    // Busca
    // ------------------------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SelectedTab is null) return;
        SelectedTab.FilterText = SearchBox.Text;
        EnsureSelection();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None && Keyboard.Modifiers != ModifierKeys.Shift) return;
        switch (e.Key)
        {
            case Key.Up: MoveSelection(-1); e.Handled = true; break;
            case Key.Down: MoveSelection(+1); e.Handled = true; break;
            case Key.PageUp: MoveSelection(-VisibleRows()); e.Handled = true; break;
            case Key.PageDown: MoveSelection(+VisibleRows()); e.Handled = true; break;
            case Key.Home when Keyboard.Modifiers == ModifierKeys.None && SearchBox.Text.Length == 0:
                MoveSelection(0, absolute: true); e.Handled = true; break;
            case Key.End when Keyboard.Modifiers == ModifierKeys.None && SearchBox.Text.Length == 0:
                MoveSelection(int.MaxValue, absolute: true); e.Handled = true; break;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox.IsKeyboardFocusWithin) return;
        if (!(ItemList.IsKeyboardFocusWithin || TabBar.IsKeyboardFocusWithin)) return;
        if (string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl)) return;
        if (Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Alt) return;

        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void OnFind(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // ------------------------------------------------------------------
    // Teclado
    // ------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var inSearch = SearchBox.IsKeyboardFocusWithin;
        var inList = ItemList.IsKeyboardFocusWithin || TabBar.IsKeyboardFocusWithin;

        if (TryRunCommandShortcut(key, mods))
        {
            e.Handled = true;
            return;
        }

        var handled = true;
        switch (key)
        {
            case Key.Escape:
                if (SearchBox.Text.Length > 0)
                {
                    SearchBox.Text = "";
                    FocusList();
                }
                else HideWindow();
                break;

            case Key.Enter when mods == ModifierKeys.None && (inList || inSearch):
                ActivateSelected();
                break;
            case Key.Enter when mods == ModifierKeys.Control && (inList || inSearch):
                CopySelected();
                break;

            case Key.F1 when mods == ModifierKeys.None: OnHelp(this, new RoutedEventArgs()); break;
            case Key.F2 when mods == ModifierKeys.None: EditSelected(); break;
            case Key.F2 when mods == ModifierKeys.Shift: EditNotes(); break;
            case Key.F2 when mods == ModifierKeys.Control: RenameTab(); break;
            case Key.F4 when mods == ModifierKeys.None: ShowContent(); break;
            case Key.F5 when mods == ModifierKeys.None: OnAction(this, new RoutedEventArgs()); break;
            case Key.F6 when mods == ModifierKeys.None: _host.ShowPreferences(5); break;

            case Key.Delete when mods == ModifierKeys.None && (inList || inSearch): RemoveSelected(); break;

            case Key.N when mods == ModifierKeys.Control: NewItem(); break;
            case Key.T when mods == ModifierKeys.Control: NewTab(); break;
            case Key.W when mods == ModifierKeys.Control: RemoveTab(); break;
            case Key.F when mods == ModifierKeys.Control: OnFind(this, new RoutedEventArgs()); break;
            case Key.P when mods == ModifierKeys.Control: _host.ShowPreferences(0); break;
            case Key.Q when mods == ModifierKeys.Control: _host.Exit(); break;
            case Key.C when mods == ModifierKeys.Control && inList: CopySelected(); break;
            case Key.V when mods == ModifierKeys.Control && inList: PasteItems(); break;
            case Key.A when mods == ModifierKeys.Control && inList: ItemList.SelectAll(); break;
            case Key.P when mods == (ModifierKeys.Control | ModifierKeys.Shift): TogglePin(); break;
            case Key.S when mods == (ModifierKeys.Control | ModifierKeys.Shift): SortSelected(false); break;
            case Key.R when mods == (ModifierKeys.Control | ModifierKeys.Shift): SortSelected(true); break;
            case Key.X when mods == (ModifierKeys.Control | ModifierKeys.Shift): _host.ToggleMonitoring(); break;
            case Key.C when mods == (ModifierKeys.Control | ModifierKeys.Shift): _host.ShowClipboardContent(); break;

            case Key.Up when mods == ModifierKeys.Control: MoveSelected(-1); break;
            case Key.Down when mods == ModifierKeys.Control: MoveSelected(+1); break;
            case Key.Home when mods == ModifierKeys.Control: MoveSelectedToEdge(top: true); break;
            case Key.End when mods == ModifierKeys.Control: MoveSelectedToEdge(top: false); break;

            case Key.Tab when mods == ModifierKeys.Control: SwitchTab(+1); break;
            case Key.Tab when mods == (ModifierKeys.Control | ModifierKeys.Shift): SwitchTab(-1); break;
            case Key.Left when mods == ModifierKeys.None && ItemList.IsKeyboardFocusWithin: SwitchTab(-1); break;
            case Key.Right when mods == ModifierKeys.None && ItemList.IsKeyboardFocusWithin: SwitchTab(+1); break;
            case Key.Left when mods == (ModifierKeys.Control | ModifierKeys.Shift): MoveTab(-1); break;
            case Key.Right when mods == (ModifierKeys.Control | ModifierKeys.Shift): MoveTab(+1); break;

            case >= Key.D1 and <= Key.D9 when mods == ModifierKeys.Control:
                SelectTabIndex(key - Key.D1);
                break;

            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    private bool TryRunCommandShortcut(Key key, ModifierKeys mods)
    {
        var tab = SelectedTab;
        if (tab is null) return false;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return false;

        uint m = 0;
        if (mods.HasFlag(ModifierKeys.Control)) m |= Hotkey.ModControl;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= Hotkey.ModShift;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= Hotkey.ModAlt;
        if (mods.HasFlag(ModifierKeys.Windows)) m |= Hotkey.ModWin;

        foreach (var cmd in _host.Commands.Where(c => c.Enabled && c.InMenu && !string.IsNullOrWhiteSpace(c.Shortcut)).ToList())
        {
            if (!HotkeyParser.TryParse(cmd.Shortcut, out var hk) || hk.VirtualKey != vk || hk.Modifiers != m) continue;
            var items = SelectedItemsOrdered().ToList();
            if (items.Count == 0 || !_host.Runner.Matches(cmd, items[0].Data, items[0].WindowTitle)) continue;
            _host.Runner.RunMenuCommand(cmd, tab, items);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Ações sobre itens
    // ------------------------------------------------------------------

    private void ActivateSelected()
    {
        var tab = SelectedTab;
        var items = SelectedItemsOrdered().ToList();
        if (tab is null || items.Count == 0) return;
        _host.ActivateItems(tab, items);
    }

    private void CopySelected()
    {
        var tab = SelectedTab;
        var items = SelectedItemsOrdered().ToList();
        if (tab is null || items.Count == 0) return;
        _host.CopyItemsToClipboard(tab, items);
    }

    private void EditSelected()
    {
        var tab = SelectedTab;
        var item = CurrentItem;
        if (tab is null || item is null) return;
        _host.OpenEditorForItem(tab, item);
    }

    private void NewItem()
    {
        var tab = SelectedTab ?? _host.History.ClipboardTab;
        _host.OpenEditorForItem(tab, null);
    }

    private void EditNotes()
    {
        var item = CurrentItem;
        if (item is null) return;
        var editor = new ItemEditorWindow("Edit notes", item.Notes ?? "", _host.Settings.FontFamily);
        var text = editor.ShowModal(this);
        if (text is null) return;
        _host.History.SetNotes(item, text);
    }

    private void ShowContent()
    {
        var item = CurrentItem;
        if (item is null) return;
        var window = new ShowContentWindow("Item content · " + item.Summary, item.Data, item.Notes) { Owner = this };
        window.Show();
    }

    private void TogglePin()
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var items = SelectedItemsOrdered().ToList();
        if (items.Count == 0) return;
        var pin = items.Any(i => !i.Pinned);
        foreach (var item in items) _host.History.SetPinned(tab, item, pin);
    }

    private void RemoveSelected()
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var items = SelectedItemsOrdered().ToList();
        if (items.Count == 0) return;

        var removable = items.Where(i => !i.Pinned).ToList();
        if (removable.Count == 0)
        {
            MessageBox.Show(this, "Pinned items cannot be removed. Unpin them first (Ctrl+Shift+P).", "KulyCopy",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var index = ItemList.SelectedIndex;
        _host.History.RemoveItems(tab, removable);
        if (ItemList.Items.Count > 0)
        {
            ItemList.SelectedIndex = Math.Clamp(index, 0, ItemList.Items.Count - 1);
            ItemList.ScrollIntoView(ItemList.SelectedItem);
        }
        FocusList();
    }

    private void MoveSelected(int delta)
    {
        var tab = SelectedTab;
        if (tab is null || SearchBox.Text.Length > 0) return;
        var items = SelectedItemsOrdered().Where(i => !i.Pinned).ToList();
        if (items.Count == 0) return;
        var firstUnpinned = tab.FirstUnpinnedIndex();

        var ordered = delta < 0 ? items : Enumerable.Reverse(items).ToList();
        foreach (var item in ordered)
        {
            var index = tab.Items.IndexOf(item);
            var target = index + delta;
            if (target < firstUnpinned || target >= tab.Items.Count) break;
            _host.History.MoveItem(tab, item, target);
        }
        foreach (var item in items) ItemList.SelectedItems.Add(item);
        if (items.Count > 0) ItemList.ScrollIntoView(items[0]);
        FocusList();
    }

    private void MoveSelectedToEdge(bool top)
    {
        var tab = SelectedTab;
        if (tab is null || SearchBox.Text.Length > 0) return;
        var items = SelectedItemsOrdered().Where(i => !i.Pinned).ToList();
        if (items.Count == 0) return;

        if (top)
            foreach (var item in Enumerable.Reverse(items)) _host.History.MoveToTop(tab, item);
        else
            foreach (var item in items) _host.History.MoveToBottom(tab, item);

        foreach (var item in items) ItemList.SelectedItems.Add(item);
        ItemList.ScrollIntoView(items[0]);
        FocusList();
    }

    private void SortSelected(bool reverse)
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var items = SelectedItemsOrdered().ToList();
        if (items.Count < 2) items = tab.Items.ToList();
        _host.History.SortItems(tab, items, reverse);
    }

    private void PasteItems()
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var snapshot = _host.ReadClipboard(all: true);
        if (snapshot is null || snapshot.Data.Count == 0) return;
        var row = CurrentItemRow();
        _host.History.AddItem(tab, snapshot.Data, row: row < 0 ? -1 : row, dedupe: false);
    }

    private void MoveSelectedToTab(TabViewModel target)
    {
        var tab = SelectedTab;
        if (tab is null || target == tab) return;
        var items = SelectedItemsOrdered().ToList();
        if (items.Count == 0) return;
        _host.History.MoveItemsToTab(tab, target, items);
        EnsureSelection();
    }

    // ------------------------------------------------------------------
    // Abas
    // ------------------------------------------------------------------

    private void NewTab()
    {
        var name = InputDialog.ShowSingle(this, "New tab", "Tab name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        SelectedTab = _host.History.GetOrCreateTab(name);
        FocusList();
    }

    private void RenameTab()
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var name = InputDialog.ShowSingle(this, "Rename tab", "New name:", tab.Name);
        if (string.IsNullOrWhiteSpace(name) || name == tab.Name) return;
        if (!_host.History.RenameTab(tab, name))
            MessageBox.Show(this, "A tab with this name already exists.", "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RemoveTab()
    {
        var tab = SelectedTab;
        if (tab is null) return;
        if (Tabs.Count == 1)
        {
            MessageBox.Show(this, "The last tab cannot be removed.", "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (tab.Items.Count > 0)
        {
            var answer = MessageBox.Show(this, $"Remove tab \"{tab.Name}\" and its {tab.Items.Count} item(s)?", "KulyCopy",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }
        var index = Tabs.IndexOf(tab);
        _host.History.RemoveTab(tab);
        SelectedTab = Tabs.ElementAtOrDefault(Math.Clamp(index, 0, Tabs.Count - 1));
    }

    private void SwitchTab(int delta)
    {
        if (Tabs.Count == 0) return;
        var index = SelectedTab is null ? 0 : Tabs.IndexOf(SelectedTab);
        index = ((index + delta) % Tabs.Count + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[index];
        FocusList();
    }

    private void SelectTabIndex(int index)
    {
        if (index < 0 || index >= Tabs.Count) return;
        SelectedTab = Tabs[index];
        FocusList();
    }

    private void MoveTab(int delta)
    {
        var tab = SelectedTab;
        if (tab is null) return;
        _host.History.MoveTab(tab, Tabs.IndexOf(tab) + delta);
        TabBar.ScrollIntoView(tab);
    }

    private void OnTabLimit(object sender, RoutedEventArgs e)
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var value = InputDialog.ShowSingle(this, "Tab item limit",
            $"Maximum items for \"{tab.Name}\" (empty = use global setting, currently {_host.Settings.MaxItems}):",
            tab.MaxItems?.ToString() ?? "");
        if (value is null) return;
        if (value.Trim().Length == 0) _host.History.SetTabMaxItems(tab, null);
        else if (int.TryParse(value.Trim(), out var n) && n > 0) _host.History.SetTabMaxItems(tab, n);
    }

    private void OnTabContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var container = ItemsControl.ContainerFromElement(TabBar, source) as ListBoxItem;
            if (container?.DataContext is TabViewModel tab) SelectedTab = tab;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem("_New Tab...", "Ctrl+T", (_, _) => NewTab()));
        menu.Items.Add(MakeMenuItem("_Rename Tab...", "Ctrl+F2", (_, _) => RenameTab()));
        menu.Items.Add(MakeMenuItem("Re_move Tab", "Ctrl+W", (_, _) => RemoveTab()));
        menu.Items.Add(MakeMenuItem("Tab _Item Limit...", null, OnTabLimit));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("Move Tab _Left", "Ctrl+Shift+Left", (_, _) => MoveTab(-1)));
        menu.Items.Add(MakeMenuItem("Move Tab Ri_ght", "Ctrl+Shift+Right", (_, _) => MoveTab(+1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem("_Export Tab...", null, OnExportTab));
        TabBar.ContextMenu = menu;
    }

    private void OnMoveToTabMenuOpened(object sender, RoutedEventArgs e) => FillMoveToTabMenu(MoveToTabMenu);

    private void FillMoveToTabMenu(MenuItem parent)
    {
        parent.Items.Clear();
        var current = SelectedTab;
        foreach (var tab in Tabs.Where(t => t != current))
        {
            var target = tab;
            parent.Items.Add(MakeMenuItem(tab.Name.Replace("_", "__"), null, (_, _) => MoveSelectedToTab(target)));
        }
        if (parent.Items.Count > 0) parent.Items.Add(new Separator());
        parent.Items.Add(MakeMenuItem("_New Tab...", null, (_, _) =>
        {
            var name = InputDialog.ShowSingle(this, "Move to new tab", "Tab name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            MoveSelectedToTab(_host.History.GetOrCreateTab(name));
        }));
    }

    // ------------------------------------------------------------------
    // Menu de contexto dos itens
    // ------------------------------------------------------------------

    private void OnItemContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            var container = ItemsControl.ContainerFromElement(ItemList, source) as ListBoxItem;
            if (container is not null && !container.IsSelected)
            {
                ItemList.SelectedItems.Clear();
                container.IsSelected = true;
            }
        }

        var menu = new ContextMenu();
        var items = SelectedItemsOrdered().ToList();

        if (items.Count > 0)
        {
            var anyPinned = items.Any(i => i.Pinned);
            menu.Items.Add(MakeMenuItem("_Activate (copy and paste)", "Enter", (_, _) => ActivateSelected()));
            menu.Items.Add(MakeMenuItem("_Copy to Clipboard", "Ctrl+Enter", (_, _) => CopySelected()));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("_Edit", "F2", (_, _) => EditSelected()));
            menu.Items.Add(MakeMenuItem("Edit _Notes", "Shift+F2", (_, _) => EditNotes()));
            menu.Items.Add(MakeMenuItem("_Show Content...", "F4", (_, _) => ShowContent()));
            menu.Items.Add(MakeMenuItem(anyPinned ? "_Unpin" : "_Pin", "Ctrl+Shift+P", (_, _) => TogglePin()));
            menu.Items.Add(MakeMenuItem("_Remove", "Delete", (_, _) => RemoveSelected()));

            var moveTo = new MenuItem { Header = "Move to Ta_b" };
            FillMoveToTabMenu(moveTo);
            menu.Items.Add(moveTo);

            var commands = _host.Runner.MenuCommandsFor(items).ToList();
            if (commands.Count > 0)
            {
                menu.Items.Add(new Separator());
                var tab = SelectedTab!;
                foreach (var cmd in commands)
                {
                    var captured = cmd;
                    menu.Items.Add(MakeMenuItem(cmd.Name.Replace("_", "__"), string.IsNullOrWhiteSpace(cmd.Shortcut) ? null : cmd.Shortcut,
                        (_, _) => _host.Runner.RunMenuCommand(captured, tab, SelectedItemsOrdered().ToList())));
                }
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("A_ction...", "F5", OnAction));
        }
        else
        {
            menu.Items.Add(MakeMenuItem("_New Item", "Ctrl+N", (_, _) => NewItem()));
            menu.Items.Add(MakeMenuItem("_Paste Items", "Ctrl+V", (_, _) => PasteItems()));
        }

        ItemList.ContextMenu = menu;
    }

    private static MenuItem MakeMenuItem(string header, string? gesture, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += handler;
        return item;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(ItemList, source) is ListBoxItem)
            ActivateSelected();
    }

    // ------------------------------------------------------------------
    // Handlers do menu principal
    // ------------------------------------------------------------------

    private void OnNewItem(object sender, RoutedEventArgs e) => NewItem();
    private void OnActivate(object sender, RoutedEventArgs e) => ActivateSelected();
    private void OnCopyItems(object sender, RoutedEventArgs e) => CopySelected();
    private void OnPasteItems(object sender, RoutedEventArgs e) => PasteItems();
    private void OnShowContent(object sender, RoutedEventArgs e) => ShowContent();
    private void OnRemove(object sender, RoutedEventArgs e) => RemoveSelected();
    private void OnEdit(object sender, RoutedEventArgs e) => EditSelected();
    private void OnEditNotes(object sender, RoutedEventArgs e) => EditNotes();
    private void OnTogglePin(object sender, RoutedEventArgs e) => TogglePin();
    private void OnMoveTop(object sender, RoutedEventArgs e) => MoveSelectedToEdge(top: true);
    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelected(+1);
    private void OnMoveBottom(object sender, RoutedEventArgs e) => MoveSelectedToEdge(top: false);
    private void OnSort(object sender, RoutedEventArgs e) => SortSelected(false);
    private void OnReverse(object sender, RoutedEventArgs e) => SortSelected(true);
    private void OnNewTab(object sender, RoutedEventArgs e) => NewTab();
    private void OnRenameTab(object sender, RoutedEventArgs e) => RenameTab();
    private void OnRemoveTab(object sender, RoutedEventArgs e) => RemoveTab();
    private void OnNextTab(object sender, RoutedEventArgs e) => SwitchTab(+1);
    private void OnPrevTab(object sender, RoutedEventArgs e) => SwitchTab(-1);
    private void OnMoveTabLeft(object sender, RoutedEventArgs e) => MoveTab(-1);
    private void OnMoveTabRight(object sender, RoutedEventArgs e) => MoveTab(+1);
    private void OnPreferences(object sender, RoutedEventArgs e) => _host.ShowPreferences(0);
    private void OnCommands(object sender, RoutedEventArgs e) => _host.ShowPreferences(5);
    private void OnShowClipboard(object sender, RoutedEventArgs e) => _host.ShowClipboardContent();
    private void OnToggleMonitoring(object sender, RoutedEventArgs e) => _host.ToggleMonitoring();
    private void OnProcessClipboard(object sender, RoutedEventArgs e) => _host.ProcessClipboardNow();
    private void OnExit(object sender, RoutedEventArgs e) => _host.Exit();

    private void OnAction(object sender, RoutedEventArgs e)
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var dialog = new ActionDialog(_host, tab, SelectedItemsOrdered().ToList()) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnImportTab(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "KulyCopy tab (*.json)|*.json|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var exported = TabExport.ReadFile(dialog.FileName);
            var tab = _host.History.Import(exported);
            SelectedTab = tab;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed: " + ex.Message, "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportTab(object sender, RoutedEventArgs e)
    {
        var tab = SelectedTab;
        if (tab is null) return;
        var safeName = string.Concat(tab.Name.Split(Path.GetInvalidFileNameChars()));
        var dialog = new SaveFileDialog { FileName = safeName + ".json", Filter = "KulyCopy tab (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            TabExport.WriteFile(dialog.FileName, _host.History.Export(tab));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed: " + ex.Message, "KulyCopy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnHelp(object sender, RoutedEventArgs e)
    {
        var window = new TextViewerWindow("KulyCopy help", HelpText.Full, wrap: true) { Owner = this };
        window.Show();
    }

    private void OnShowLog(object sender, RoutedEventArgs e)
    {
        static string Logs() => string.Join(Environment.NewLine, Log.Snapshot());
        var window = new TextViewerWindow("KulyCopy log", Logs(), Logs) { Owner = this };
        window.Show();
        window.ScrollToEnd();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    // ------------------------------------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
