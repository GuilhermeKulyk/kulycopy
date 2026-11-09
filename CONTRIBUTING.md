# Contributing to KulyCopy

Thanks for helping! KulyCopy is a small C# / WPF code base and contributions of any size are welcome:
bug reports, documentation, translations, new scripting functions, UI polish.

## Getting started

1. Install the .NET 8 SDK and clone the repository.
2. `dotnet build` builds everything; `dotnet test` runs the unit tests in `tests/`.
3. Run `src\KulyCopy\bin\Debug\net8.0-windows\kulycopy.exe --portable --show` to try your build
   without touching your real KulyCopy settings (data goes to a `data` folder next to the exe).
4. The CLI talks to whatever instance is running from the same executable path, so
   `kulycopy.exe eval "..."` is a quick way to poke at the running app.

## Project layout

| Path | Contents |
| --- | --- |
| `src/KulyCopy.Core` | Platform-independent core: models, SQLite store, settings, commands, hotkey parsing, CF_HTML, import/export. No UI dependencies, covered by tests. |
| `src/KulyCopy` | The Windows app: clipboard monitor (Win32), global hotkeys, scripting (Jint), WPF windows, tray icon, named-pipe IPC and CLI. |
| `tests/KulyCopy.Core.Tests` | xUnit tests for the core library. |
| `docs/` | Scripting reference. |

## Guidelines

- Keep the scripting API compatible with CopyQ names and semantics where it makes sense; document any
  deliberate difference in `docs/scripting.md`.
- Never change or delete user data (history, settings, commands) silently. Migrations must be explicit
  and backwards compatible; the SQLite schema version lives in the `meta` table.
- Do not commit anything machine-specific: absolute paths, user names, API keys, build output,
  `.vs/`, `*.user` files. The `.gitignore` covers the usual suspects; please keep it that way.
- Prefer small, focused pull requests with a short description of the behavior change.
- Code style follows `.editorconfig` (4 spaces, file-scoped namespaces, nullable enabled).

## Reporting bugs

Please include the KulyCopy version (Help › About), Windows version, steps to reproduce and, if
relevant, the log (Help › Show Log, or `%LOCALAPPDATA%\KulyCopy\kulycopy.log`). Remove anything
private from the log before posting it; clipboard contents are never written to the log by default.

## License

By contributing you agree that your contributions are licensed under the MIT License.
