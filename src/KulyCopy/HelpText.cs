using KulyCopy.Core;

namespace KulyCopy;

/// <summary>Textos de ajuda da linha de comando (--help) e da janela de ajuda (F1).</summary>
public static class HelpText
{
    public static string Cli => $"""
        KulyCopy {AppInfo.Version} - clipboard manager for Windows (MIT License)

        Usage:
          kulycopy                       Start KulyCopy, or show/hide the window if it is already running
          kulycopy --show                Start and show the main window
          kulycopy --server              Start the main instance explicitly
          kulycopy --portable            Keep settings and history in a "data" folder next to the executable
          kulycopy <function> [args...]  Call a scripting function in the running instance and print the result
          kulycopy eval <script> [args]  Evaluate a JavaScript snippet (all functions below are available)
          kulycopy - [args...]           Read the script from standard input
          kulycopy --help | --version

        Examples:
          kulycopy show                  Show the main window
          kulycopy add "some text"       Add an item to the current tab
          kulycopy read 0                Print the text of the first item
          kulycopy read image/png 0 > a.png
          kulycopy copy "hello"          Put text on the clipboard
          kulycopy paste                 Paste the clipboard into the last active window
          kulycopy tab                   List tabs
          kulycopy eval "tab('Notes'); add('remember this')"
          kulycopy eval "print(size())"
          kulycopy disable               Stop storing clipboard changes (enable to resume)
          kulycopy exit

        Scripting functions:
          Clipboard:  copy, paste, clipboard, hasClipboardFormat
          Items:      read, write, change, add, insert, remove, edit, select, move, pin, unpin,
                      length/size/count, selectedItems, currentItem, selectedItemData, selectedItemsData,
                      setSelectedItemData, setSelectedItemsData
          Tabs:       tab, removeTab, renameTab, tabIcon, selectedTab, exportTab, importTab
          Input:      input, data, setData, removeData, dataFormats, str, ignore, abort, fail
          Window:     show, hide, toggle, visible, focused, menu, filter, exit, disable, enable, monitoring
          Config:     config, toggleConfig, settings, info, version, commands, setCommands, addCommands,
                      importCommands, exportCommands
          System:     execute, open, env, setEnv, currentPath, sleep, print, serverLog, logs, dateString,
                      currentWindowTitle, dialog, popup, notification, keys, screenshot, networkGet,
                      networkPost, queryKeyboardModifiers, pointerPosition, setPointerPosition
          Utilities:  toBase64, fromBase64, md5sum, sha1sum, sha256sum, sha512sum, escapeHtml, eval, source

        Constants: mimeText, mimeHtml, mimeRtf, mimeUriList, mimePng, mimeItemNotes, mimeWindowTitle
        """;

    public static string Full => Cli + """


        Keyboard shortcuts in the main window:
          Type any text              Search (regular expression)
          Esc                        Clear search / hide window
          Enter                      Activate item: copy, hide and paste into the previous window
          Ctrl+Enter                 Copy item to clipboard only
          F2 / Shift+F2              Edit item / edit notes
          F4                         Show item content (all formats)
          F5                         Action: run a script on the selected items
          F6                         Commands and global shortcuts
          Delete                     Remove selected items (pinned items are protected)
          Ctrl+N                     New item
          Ctrl+Shift+P               Pin / unpin
          Ctrl+Up / Ctrl+Down        Move item up / down
          Ctrl+Home / Ctrl+End       Move item to top / bottom
          Ctrl+Shift+S / Ctrl+Shift+R  Sort / reverse selected items
          Ctrl+C / Ctrl+V            Copy selected items / paste clipboard as new item
          Ctrl+T / Ctrl+F2 / Ctrl+W  New / rename / remove tab
          Left, Right, Ctrl+Tab      Switch tabs (Ctrl+1..9 selects a tab directly)
          Ctrl+Shift+Left/Right      Move tab
          Ctrl+Shift+X               Enable/disable clipboard storing
          Ctrl+Shift+C               Show clipboard content
          Ctrl+P                     Preferences
          Ctrl+Q                     Exit

        Commands (Preferences > Commands):
          Automatic commands run for every new clipboard content and can route items to tabs,
          ignore them (ignore()) or transform them (setData()). Menu commands appear in the item
          context menu when the item matches. Global shortcut commands run from anywhere.
          Commands are stored as JSON in the settings folder and can be imported/exported.
        """;
}
