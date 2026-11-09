# KulyCopy privacy policy

*Last updated: 2026-11-09*

KulyCopy is a clipboard manager. Its whole purpose is to remember what you copy, so it necessarily
handles personal data. This page explains exactly what happens to that data.

## What KulyCopy stores

- The content you copy to the Windows clipboard: text, formatted text (HTML/RTF), images and the
  paths of copied files, together with the time and the title of the window it was copied from.
- Your settings, tabs, notes, pinned items and the commands (scripts) you configure.

## Where it is stored

Everything is stored **only on your computer**, in your user profile:

| Data | Location |
| --- | --- |
| Clipboard history (SQLite database) and log | `%LOCALAPPDATA%\KulyCopy` |
| Settings and commands | `%APPDATA%\KulyCopy` |
| Portable mode | the `data` folder next to `kulycopy.exe` |
| Microsoft Store installation | the app's own virtualized application-data folders |

The log file never contains clipboard contents, only technical events and errors.

## What KulyCopy does not do

- It does not send clipboard contents, settings or usage data anywhere. There is no telemetry,
  no analytics, no account and no cloud synchronization.
- It does not contact the internet by itself. The only network access happens when **you** write a
  command or script that calls `networkGet()` / `networkPost()`, or run a command that opens a URL.
- It does not read anything from other applications beyond what they place on the Windows clipboard.

## Your control

- Delete items, tabs or the whole history at any time inside the app.
- Disable clipboard storing temporarily (tray menu or shortcut), or add automatic commands that
  ignore specific applications (for example password managers; one is included and enabled by default).
- Uninstalling asks whether to delete settings and history; they are never deleted silently.
- The source code is open (MIT License), so this behavior can be verified.

## Contact

Questions and reports: open an issue at <https://github.com/GuilhermeKulyk/kulycopy/issues>.
