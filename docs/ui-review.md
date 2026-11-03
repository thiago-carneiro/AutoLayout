# UI/UX review, 2026-11-03

An independent design review of every window, page state, menu and dialog, from the screenshots that `AutoLayout.exe --demo --capture <folder>` writes (dark and light themes; English, Portuguese, Arabic for right-to-left and German for long words) and from the source. This file records what was found and what was done, so later reviews start from here.

## Changed

| # | Finding | Change |
|---|---|---|
| 1 | The About links did not fit in German ("Auf Ko-fi unterstü…") | One link per line |
| 2 | Screen readers would read each keyboard row as a class name | `KeyboardItem.ToString()` gives name, layout and status; the layout in use is a live region |
| 3 | The ignored row, at 60% opacity, failed text contrast | Full opacity; the name and icon in the secondary text color |
| 4 | With a keyboard's layout uninstalled, the page said its layout was in use because it was connected | "“{0}” is connected, but its layout isn’t installed, so the default layout is in use." |
| 5 | Forgetting a connected keyboard said "if you connect it again" | A text for the connected case: it goes back to waiting at once |
| 6 | Remote Desktop looked like a disconnected keyboard, could be "forgotten", and its window had a Name field | Own description and status ("No remote session"), no Forget, no Name field, its own window title |
| 7 | Status pills were inconsistent in the light theme and kept old colors after a theme change | Pills only for "In use" and "Waiting"; other states are plain secondary text; colors refresh on theme change |
| 8 | A stray "—" for keyboards with no layout; no direct action on a waiting keyboard | No layout text; an accent "Set up…" button on the waiting row |
| 9 | "Ignore" used the warning icon; no right-click menu | Hide/View icons; the menu is the card's context menu (right-click, Shift+F10) and the "…" button opens it |
| 10 | "Ignore" had four names | "Ignore device" for the menu and the button, with the tooltip "It isn’t a keyboard" |
| 11 | Fixed pane and combo widths; the setup window ignored text size | Navigation pane collapses below 800 px; combo 200–320 px; the setup window widens with Windows text size (up to 1.5×) |
| 12 | The setup window focused the optional name | It focuses the layout |
| 13 | Privacy shown as an InfoBar; switches without On/Off; "Off by default" said twice | Privacy text in the About card; standard switches; the hint shortened |
| 14 | Links 12 px off the text above; a closed InfoBar left a gap | Aligned; the InfoBar collapses when closed |
| 15 | Monitor icon for "no external keyboard", collapse-like icon for Remote Desktop | Laptop and monitor |
| 16 | Tray menu said "Current layout" and used "…" without a following dialog; the new-keyboard notification said "Confirm it to use it"; "USB Keyboard · USB" | "Layout in use", "Open AutoLayout", "Settings"; "Until you confirm it, nothing changes."; the product name is dropped when it repeats the connection; layout-change notifications are silent |
| 17 | The setup window's footer divider was invisible in the dark theme | Divider brush |

The new and changed strings were translated into the 36 machine-translated languages; the translation tests check every language for every key and placeholder.

## Not changed yet

- **Remote Desktop as its own section** (an inline card with a layout selector, like the default layout) instead of a row in the keyboard list. The row now explains itself; a section would be clearer still.
- **A distinguishing mark on the tray icon.** The plain keyboard resembles Windows' touch keyboard icon.
- **Narrow windows and 200% text** were judged from the source, not measured: the setup window's footer could still overflow with very long button labels.
- **Framework text in screenshots with `--lang`.** WinUI's own strings (the On/Off of switches) follow the Windows display language, so a capture in another language mixes the two. In normal use the app follows the Windows display language and they match.

Not judged from screenshots: Mica, hover and focus visuals, high contrast, Narrator output, and the native tray menu and notifications.
