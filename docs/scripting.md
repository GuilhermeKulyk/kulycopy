# KulyCopy scripting reference

Commands and the command line use JavaScript (ECMAScript 2023, powered by [Jint](https://github.com/sebastienros/jint)).
The global functions follow the names and behavior of CopyQ's scripting API, so most CopyQ
commands work unchanged. Scripts run inside the KulyCopy process, one at a time, on the UI thread.

Rows are zero-based: row `0` is the item at the top of the tab.

## Running scripts

| How | Context |
| --- | --- |
| `kulycopy eval "script"` or `kulycopy <function> args…` | `arguments` holds the extra command-line arguments; `input()` is standard input; the current tab is the one shown in the window. |
| Automatic command | `data()` is the new clipboard content; `setData()` changes what gets stored; `ignore()` drops it. |
| Menu command | `selectedItemsData()` are the selected items; `data()` is the first one. |
| Global shortcut command | `data()` is the current clipboard. |
| Action dialog (`F5`) | Like a menu command; the result is shown in the dialog. |

A script may `return` a value at the top level (it is then wrapped in a function). The value of the
last expression, or anything passed to `print()`, becomes the command-line output.

## Data

Item data is a map from format name (MIME type) to bytes. Text formats are exposed as strings,
binary formats as a `ByteArray` object (`length`, `toString()`, `toBase64()`); `str(x)` converts anything to a string.

Constants: `mimeText` (`text/plain`), `mimeHtml`, `mimeRtf`, `mimeUriList`, `mimePng` (`image/png`),
`mimeItemNotes`, `mimeOwner`, `mimeWindowTitle`, `mimeHidden`.

## Functions

### Clipboard

| Function | Description |
| --- | --- |
| `copy(text)` / `copy(mime, data, [mime, data]…)` | Set the clipboard. `copy()` with no arguments sends Ctrl+C to the previous window. |
| `paste()` | Paste the clipboard into the previously active window (Ctrl+V). |
| `clipboard([mime])` | Current clipboard content in the given format (default text). |
| `hasClipboardFormat(mime)` | Whether the clipboard has the format. |

### Items (current tab)

| Function | Description |
| --- | --- |
| `read([mime], row, …)` | Content of items. `read()` reads the clipboard. Several rows are joined with newlines. |
| `write([row], mime, data, …)` / `write([row], text)` | Insert a new item (default at top). |
| `change(row, mime, data, …)` | Change formats of an existing item. |
| `add(text|object, …)` | Add items at the top (first argument ends up on row 0). |
| `insert(row, text|object, …)` | Insert items at a row. |
| `remove(row, …)` | Remove items (`remove()` removes the selection). |
| `edit([row])` | Open the editor (new item without a row; `edit(-1)` edits the clipboard). |
| `select(row)` | Copy the item to the clipboard. |
| `move(row)` | Move the selected items to a row. |
| `pin(row, …)` / `unpin(row, …)` | Pin or unpin items (KulyCopy extension). |
| `length()`, `size()`, `count()` | Number of items in the current tab. |

### Tabs and selection

| Function | Description |
| --- | --- |
| `tab()` | List of tab names. |
| `tab(name)` | Make `name` the current tab for the following calls (created if needed). |
| `removeTab(name)`, `renameTab(old, new)`, `tabIcon(name, [icon])` | Tab management. |
| `selectedTab()` | Name of the tab shown in the main window. |
| `selectedItems()` | Rows of the selected items. `currentItem()` / `index()`: row of the current item. |
| `selectedItemData(i)`, `setSelectedItemData(i, data)` | Data of the i-th selected item. |
| `selectedItemsData()`, `setSelectedItemsData(list)` | Data of all selected items. |
| `exportTab(file)`, `importTab(file, [tab])` | JSON export/import of the current tab. |

### Input of the running command

| Function | Description |
| --- | --- |
| `data([mime])` | Data of the item being processed (clipboard if none). |
| `setData(mime, value)`, `removeData(mime)` | Change the data that will be stored. |
| `dataFormats()` | Formats present in the data. |
| `input()` | Standard input (CLI) or the item text. |
| `ignore()` | Automatic command: do not store this content and stop processing further commands. |
| `abort()` | Stop the script without error. `fail()` makes the command exit with code 1. |
| `str(value)` | Convert to string. |

### Window and application

`show([tab])`, `hide()`, `toggle()`, `visible()`, `focused()`, `filter(text)`, `menu()` (tray menu),
`disable()`, `enable()`, `monitoring()`, `exit()`.

### Configuration

| Function | Description |
| --- | --- |
| `config()` | All options as `Name=value` lines. `config(name)` reads, `config(name, value)` sets and applies. Hotkeys: `config('Hotkeys.ShowHide', 'Ctrl+Shift+V')`. |
| `toggleConfig(name)` | Flip a boolean option. |
| `settings([key], [value])` | Free-form persistent values for your scripts. |
| `info([name])` | `version`, `config`, `commands`, `data`, `log`, `exe`, `portable`. |
| `commands()`, `setCommands(list)`, `addCommands(list)`, `importCommands(json)`, `exportCommands(list)` | Manage commands as JSON objects. |
| `version()` | Program version. |

### System

| Function | Description |
| --- | --- |
| `execute(program, arg, …)` | Run a process; returns `{stdout, stderr, exit_code}` or `undefined` if it could not start. Standard input of the script is forwarded. |
| `open(urlOrFile, …)` | Open with the default application (KulyCopy extension). |
| `env(name)`, `setEnv(name, value)`, `currentPath([path])` | Environment. |
| `sleep(ms)` | Wait, keeping the UI responsive. |
| `print(value)`, `serverLog(text)`, `logs()` | Output and logging. |
| `dateString([format])` | Current date/time (.NET format; Qt's `hh` is treated as 24h). |
| `currentWindowTitle()` | Title of the window the content came from. |
| `dialog([".title", t], label, default, …)` | Input dialog; returns a string (one field), an object (several) or `undefined` if cancelled. |
| `popup(title, message, [ms])`, `notification(".title", t, ".message", m, ".time", ms)` | Tray notifications. |
| `keys("Ctrl+V", …)` | Send key presses to the active window. |
| `screenshot()` | PNG of the primary screen as `ByteArray`. |
| `networkGet(url)`, `networkPost(url, data)` | HTTP; returns `{data, status, error, contentType}`. |
| `queryKeyboardModifiers()`, `pointerPosition()`, `setPointerPosition(x, y)` | Input state. |
| `toBase64`, `fromBase64`, `md5sum`, `sha1sum`, `sha256sum`, `sha512sum`, `escapeHtml`, `eval`, `source(file)` | Utilities. |

## Command fields

| Field | Meaning |
| --- | --- |
| Automatic / In menu / Global shortcut / Script | When the command runs. |
| Match text | Regex the item text must match. |
| Match window | Regex the source window title must match. |
| Input format | Format the item must contain. |
| Output format | Format used to store the script output. |
| Copy to tab | Automatic: also store the item here. Menu: copy the selected items here. |
| Output tab | Store the script output as a new item in this tab. |
| Remove item | Automatic: do not store in the clipboard tab. Menu: remove the selected items after running. |
| Hide window | Hide the main window before running. |
| Shortcut / Global shortcut | `Ctrl+Shift+O` style key names. |

## Examples

Ignore anything copied from password managers (automatic, *Match window* `(?i)KeePass|Bitwarden|1Password`):

```javascript
ignore()
```

Keep a separate tab with URLs (automatic, *Match text* `^https?://`, *Copy to tab* `URLs`): no script needed.

Trim whitespace of everything that is copied (automatic):

```javascript
var text = str(data(mimeText));
var trimmed = text.trim();
if (trimmed !== text) {
    setData(mimeText, trimmed);
    removeData(mimeHtml);
}
```

Paste the current date (global shortcut):

```javascript
copy(dateString('yyyy-MM-dd'));
paste();
```

Count words of the selected item (menu command, shows a popup):

```javascript
var words = str(data(mimeText)).split(/\s+/).filter(Boolean).length;
popup('Word count', words + ' words');
```

Join the selected items into one (menu, with *Remove item* checked):

```javascript
var parts = selectedItemsData().map(function (d) { return str(d[mimeText]); });
add(parts.join('\n'));
```

Differences from CopyQ worth knowing: there is no X11 selection (`selection()` returns the clipboard),
`keys()` uses Windows `SendKeys` syntax internally, `execute()` has no callback form, and the `File`,
`Dir`, `TemporaryFile` and `ItemSelection` classes are not available yet.
