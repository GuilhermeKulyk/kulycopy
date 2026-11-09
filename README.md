# KulyCopy

**An open source clipboard manager for Windows, inspired by [CopyQ](https://github.com/hluk/CopyQ).**

KulyCopy records everything you copy, keeps it organized in tabs, and gives you back the
power-user features that make CopyQ great on Linux: searchable history, pinned items, notes,
scriptable commands, global shortcuts and a full command-line interface.

Written in C# (.NET 8 / WPF), MIT licensed. Also available in Portuguese: [README.pt-BR.md](README.pt-BR.md).

## Features

- **Clipboard history** for text, HTML, RTF, images (stored as PNG) and copied files, with automatic
  de-duplication and a configurable item limit per tab.
- **Tabs** to organize items (`clipboard`, `Notes`, `URLs`, …) with drag-free keyboard navigation,
  move/copy between tabs, per-tab item limits, JSON import/export.
- **Fast search**: just start typing in the main window; the filter is a regular expression.
- **Pinned items** stay at the top and are protected from removal and trimming.
- **Notes** attached to any item.
- **Activate to paste**: press Enter and the item is copied, the window hides and the text is pasted
  into the application you came from. Double click or the tray menu do the same.
- **Edit items** in a built-in editor, create new items by hand, show every stored format of an item.
- **Commands** (CopyQ style): automatic commands run on every new clipboard content (route items to
  tabs, ignore password managers, transform text…), menu commands appear in the item context menu,
  and commands can be bound to global shortcuts. Commands are JavaScript with a CopyQ-like API.
- **Global shortcuts**: show/hide, paste next/previous item, paste as plain text, toggle storing,
  edit clipboard, show clipboard content, new item, plus any command.
- **Command line** interface: `kulycopy read 0`, `kulycopy add "text"`, `kulycopy eval "..."`,
  works against the running instance like CopyQ's CLI.
- **Tray icon** with recent items, light/dark theme (follows Windows), portable mode, autostart.

## Download and install

Windows 10 (1809+) or Windows 11, 64-bit. Nothing else to install: the packages include the .NET runtime.

| Package | Get it | Notes |
| --- | --- | --- |
| **Installer** (recommended) | [`KulyCopy-Setup-x.y.z.exe`](https://github.com/GuilhermeKulyk/kulycopy/releases/latest) | Per-user, no admin rights. Adds a Start Menu shortcut, optional *start with Windows*, clean uninstall. |
| **Portable** | [`KulyCopy-x.y.z-portable-win-x64.zip`](https://github.com/GuilhermeKulyk/kulycopy/releases/latest) | Unzip anywhere and run `kulycopy.exe`; settings stay in the `data` folder next to it. |
| **MSIX** | attached to each release | For the Microsoft Store and enterprise sideloading (unsigned). |

After installing, KulyCopy lives in the system tray. Press **Ctrl+Shift+V** (or click the tray icon)
to open the history, press Enter on an item to paste it.

The installer is not code-signed yet, so Windows SmartScreen may show "Windows protected your PC":
click *More info › Run anyway*. winget and Microsoft Store listings are planned; see
[docs/publishing.md](docs/publishing.md).

### Build from source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/GuilhermeKulyk/kulycopy.git
cd kulycopy
dotnet build -c Release          # app: src\KulyCopy\bin\Release\net8.0-windows\kulycopy.exe
dotnet test                      # unit tests
.\build.ps1                      # dist\: self-contained exe, portable zip, installer (add -Msix for the Store package)
```

**Portable mode** without the zip: create an empty file named `portable` next to `kulycopy.exe` (or start
with `--portable`). Settings and history are then kept in a `data` folder beside the executable.

## Using KulyCopy

| Where | What |
| --- | --- |
| Settings | `%APPDATA%\KulyCopy\settings.json`, `commands.json` |
| History | `%LOCALAPPDATA%\KulyCopy\history.db` (SQLite) and `kulycopy.log` |

Default global shortcut to show/hide the window: **Ctrl+Shift+V** (change it in Preferences › Shortcuts).

### Keyboard (main window)

| Keys | Action |
| --- | --- |
| type anything | Search (regex) · `Esc` clears / hides |
| `Enter` | Activate: copy, hide, paste into previous window |
| `Ctrl+Enter` | Copy to clipboard only |
| `F2` / `Shift+F2` | Edit item / edit notes |
| `F4` | Show item content (all formats) |
| `F5` | Action: run a script on the selected items |
| `F6` | Commands and global shortcuts |
| `Delete` | Remove selected items |
| `Ctrl+N` | New item |
| `Ctrl+Shift+P` | Pin / unpin |
| `Ctrl+Up/Down`, `Ctrl+Home/End` | Move item |
| `Ctrl+Shift+S` / `Ctrl+Shift+R` | Sort / reverse selected |
| `Ctrl+T`, `Ctrl+F2`, `Ctrl+W` | New / rename / remove tab |
| `Left`, `Right`, `Ctrl+Tab`, `Ctrl+1..9` | Switch tabs |
| `Ctrl+Shift+X` | Enable/disable clipboard storing |
| `Ctrl+P` | Preferences |

### Command line

```text
kulycopy                      start, or show/hide if already running
kulycopy <function> [args..]  call a scripting function and print the result
kulycopy eval <script>        evaluate JavaScript
kulycopy - < script.js        read the script from stdin

kulycopy add "some text"
kulycopy read 0
kulycopy read image/png 0 > image.png
kulycopy copy "hello" && kulycopy paste
kulycopy eval "tab('Notes'); add('remember this')"
kulycopy config MaxItems 500
```

`kulycopy --help` prints the full list. Because `kulycopy.exe` is a windowed application, output
goes to the console you started it from, exactly like CopyQ on Windows.

### Scripting and commands

Commands live in **Preferences › Commands** (shortcut `F6`). Each command can be:

- **Automatic**: runs for every new clipboard content. Use `ignore()` to skip storing, `setData()` to
  transform, the *Copy to tab* field to route items, *Remove item* to keep it out of the clipboard tab.
- **In menu**: shown in the item context menu when the item matches (*Match* regex, *Input* format,
  *Match window*). The selected items are available through `selectedItemsData()`.
- **Global shortcut**: runs from anywhere, with the current clipboard as `data()`.
- **Script**: only defines functions/constants for the other commands.

The scripting API follows CopyQ's names: `copy`, `paste`, `read`, `write`, `add`, `remove`, `tab`,
`data`, `setData`, `ignore`, `execute`, `dialog`, `popup`, `config`, `settings`, `networkGet`, …
See [docs/scripting.md](docs/scripting.md) for the full reference and examples.

```javascript
// Automatic command: keep only the plain text when copying from a browser
if (/Chrome|Firefox|Edge/.test(currentWindowTitle())) {
    removeData(mimeHtml);
    removeData(mimeRtf);
}
```

```javascript
// Menu command: upper-case the selected items in place
var items = selectedItemsData();
for (var i = 0; i < items.length; ++i)
    items[i][mimeText] = str(items[i][mimeText]).toUpperCase();
setSelectedItemsData(items);
```

## How it compares to CopyQ

Implemented: history with tabs, multi-format items, search, pin, notes, edit, show content,
activate/paste into previous window, tray menu, global shortcuts, automatic/menu/shortcut commands,
most of the scripting API, CLI over named pipes, import/export, light/dark themes, portable mode.

Not implemented yet (contributions welcome): encrypted tabs (CopyQ uses GPG), tab synchronization
with a directory, item tags plugin, custom item icons, translations, plugin system, the `File`/`Dir`
script classes and X11-only functions.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports with the log (Help › Show Log) are very welcome.

## License

[MIT](LICENSE). KulyCopy is an independent project and is not affiliated with CopyQ.
Third-party components: [Jint](https://github.com/sebastienros/jint) (BSD-2-Clause),
[Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) (MIT).
