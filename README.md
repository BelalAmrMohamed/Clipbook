# Clipbook

A Windows clipboard manager (C# / .NET 8 / WPF) that does everything the built-in Win+V history does, plus a quick-note box, image-to-file pasting in Explorer, unlimited pins with configurable limits, and more.

## Build & run

1. Open `Clipbook.sln` in Visual Studio 2022 (needs the ".NET desktop development" workload and the .NET 8 SDK; v2 targets `net8.0-windows10.0.19041.0` for the Windows OCR API).
2. Press F5. The app starts in the system tray (the blue clipboard icon).
3. Press **Win+V** (or **Ctrl+Shift+V** if Win+V could not be taken) to open the panel.

Command line: `dotnet run -c Release`, or `dotnet publish -c Release -r win-x64 --self-contained false`.

> This code was written without access to a Windows machine or NuGet, so it has **not been compiled or run**. Expect a few small compile or runtime fixes on the first build. Errors are logged to `%AppData%\Clipbook\error.log`.

## Features

**Built-in clipboard parity:** history of text, HTML, images and file lists; click to paste; pin; delete; clear all (keeps pins); Win+V hotkey; tray app that starts with Windows.

**Requested extras**
- **Quick note box** at the top of the panel, in a single row with the search button and Save. It starts one line tall and grows as you add lines, then scrolls. Press Ctrl+Enter to save the note (pinned by default, configurable).
- **Search** is the magnifier next to the note box (or Ctrl+F). The same input row turns into the search box and the icon becomes a red cancel button; Esc or the red icon brings the note box (and any unsaved draft) back.
- **Drag and resize:** drag the panel by its header or footer; resize it from any edge. The size is remembered.
- **Images in Explorer:** open the panel while a File Explorer window (or the Desktop) is focused, click an image, and it is saved there as `Clipbook_yyyy-MM-dd_HHmmss.png`. The footer shows the target folder. In any other app, clicking an image pastes it normally. Works with Explorer tabs.
- **Unlimited pins**, and **temp-history limits** by item count, memory (MB) and age (hours). 0 = unlimited. Pins never count and are never auto-deleted.
- **Pinned placement:** top of history, bottom of history, or a separate "Pinned" tab (Settings).

**Recommended extras included in v1**
- Search, plus filters by type, source app and collection
- Collections (named groups of pinned snippets)
- Plain-text paste: Shift+Enter in the panel, "Paste as plain text" in the item menu, or the global hotkey (Ctrl+Alt+V) to paste the current clipboard without formatting
- Sensitive-data detection (API keys, JWTs, private keys, Luhn-valid card numbers, `password=...`): skip, or record hidden and auto-delete after N seconds
- App exclusion list, and respect for password managers' "don't record" clipboard flags
- AES-256-GCM encrypted history; the key is protected by your Windows account (DPAPI)
- Text tools (upper/lower/title case, trim, remove line breaks, sort/dedupe lines, JSON format/minify). They create a new item and keep the original.
- Source app and timestamp on every item
- Dark / light / follow-Windows theme and configurable hotkeys
- Duplicate merging
- Extra: pause recording from the tray menu

**New in v2**
- **OCR:** right-click (or "…") an image, then "Copy text from image (OCR)". The recognised text becomes a new item. It uses the offline OCR built into Windows, so it needs an OCR-capable language installed (Settings > Time & language).
- **Paste queue:** copy several things and paste them one after another. Turn on collecting (queue button in the panel header, tray menu, or Ctrl+Alt+Shift+B), copy your items, then press Ctrl+Alt+B repeatedly to paste them in order. You can also add any item to the queue from its menu, paste all items in one go, and put Enter or Tab between them (useful for forms). Hotkeys are configurable.
- **Colour swatches:** text such as `#ff8800`, `rgb(10, 20, 30)` or `hsl(200, 50%, 40%)` shows a colour square.
- **Link titles:** optional (Settings > Behavior). Shows the page title under copied links. This contacts the link's website, so it is off by default and skips local/private addresses.
- **Export / import:** Settings > Backup. Export your history, pins and collections to JSON, optionally encrypted with a password (AES-256). Import merges without deleting anything and skips duplicates.
- Fixes: pasting with Alt/Win-based hotkeys no longer risks opening a window menu or the Start menu; tray menu text now says Clipbook.

**Still to come:** OCR search index (search inside images), more link previews (icons, descriptions).

## Keyboard shortcuts (in the panel)

| Key | Action |
|---|---|
| Enter | Paste selected item |
| Shift+Enter | Paste as plain text |
| Ctrl+P | Pin / unpin |
| Ctrl+C | Copy without pasting |
| Delete | Delete item |
| Down / Up | Move between search box and list |
| Ctrl+F | Start searching (Esc cancels the search) |
| Esc | Cancel search, otherwise close |
| Ctrl+Enter (note box) | Save note (Enter adds a new line) |
| Enter / Shift+Enter (search) | Paste top result / new line |

## How Win+V replacement works

Windows reserves Win+V, so `RegisterHotKey` fails for it. The app installs a low-level keyboard hook, swallows Win+V, and injects a dummy key so the Start menu doesn't open. If the hook can't be installed, the fallback hotkey (Ctrl+Shift+V by default) is used and a tray notification tells you. If the hook is blocked by security software or running in an elevated window, Windows' own panel may still appear; use the fallback.

## Known limitations

- Pasting into windows running as Administrator from a non-elevated app is blocked by Windows (UAC). Run the app elevated if you need that.
- Changing "Encrypt history" only affects newly saved items; old ones stay readable either way.
- Copying image files in Explorer stores them as images (up to 10 files, 25 MB each; GIFs stay as file entries). Other file copies are stored as paths, so they paste only while the files still exist.

## Data location

`%AppData%\Clipbook\` holds `history.db`, `images\`, `settings.json` and `key.bin`.
