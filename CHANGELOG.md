# Changelog

All notable changes to KulyCopy are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-11-09

First public version.

### Added
- Clipboard history for text, HTML, RTF, images (PNG) and copied files, stored in SQLite.
- Tabs with per-tab item limits, move/copy between tabs, JSON import/export.
- Regex search, pinned items, notes, item editor, item content viewer with all formats.
- Activate (Enter) copies and pastes into the previously active window; tray menu with recent items.
- Commands with CopyQ-style JavaScript API: automatic, menu, global shortcut and script commands.
- Global shortcuts: show/hide, paste next/previous, paste as plain text, toggle storing, edit clipboard,
  show clipboard content, new item.
- Command-line interface over named pipes (`kulycopy read 0`, `kulycopy eval ...`).
- Light and dark themes (following Windows), portable mode, autostart, preferences dialog.
- Distribution: per-user Windows installer (Inno Setup, English and Brazilian Portuguese), portable zip,
  MSIX package for the Microsoft Store, `build.ps1` to produce all of them, CI releases on tags.
