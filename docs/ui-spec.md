# AutoLayout: UI/UX specification

Status: draft 1 · Scope: UI only (engine behavior is fixed, see the project brief) · Target: WinForms on .NET Framework 4.8, C# 5, no third-party libraries.

## 0. Principles and global decisions

1. **Quiet by default.** AutoLayout lives in the tray. It never steals focus while the user types. A dialog only appears after the user clicks something.
2. **Nothing changes until the user decides.** An unknown keyboard never gets a layout automatically, even with a confident suggestion. Until the user answers, the current layout stays as it is.
3. **Changes apply immediately.** The Keyboards window and the Settings dialog have no OK/Apply. Only the per-keyboard dialog has Save/Cancel, because it is a small form with related fields.
4. **Screens (5 in total):** tray icon and menu, *Keyboards* main window, *Keyboard* dialog (used for both new and edit), *Settings* dialog, and a small *About* dialog.
5. **The default layout lives in the Keyboards window**, not in Settings. It is the answer to "what layout when no external keyboard is connected?", so it sits next to the per-keyboard layouts. Settings only holds app behavior.

### Visual conventions (all forms)
- `Application.EnableVisualStyles()`. Font: Segoe UI 9pt, set on every Form before controls are added. `AutoScaleMode = Dpi`. PerMonitorV2 is declared in `app.manifest` and in `app.config` (`<System.Windows.Forms.ApplicationConfigurationSection><add key="DpiAwareness" value="PerMonitorV2"/>`).
- All sizes below are **logical pixels at 96 DPI**. Scale them through one helper, `Dpi.S(int)` = `value * DeviceDpi / 96`, and recompute on `DpiChanged`.
- Spacing: form padding 12. Gap between related controls 6; gap between sections 16. Buttons are at least 88×26 (AutoSize with `Padding(8,0,8,0)`). Section headers use Segoe UI 9pt **Semibold** (`new Font("Segoe UI Semibold", 9f)`). Do not use GroupBoxes: Windows 11 uses headers plus whitespace instead.
- Colors come only from `SystemColors`. Secondary text uses `SystemColors.GrayText`. This keeps High Contrast working. Dark mode is out of scope because WinForms on .NET Framework has no dark theme.
- ListView: call `SetWindowTheme(lv.Handle, "Explorer", null)` (uxtheme P/Invoke) to get the modern row highlight.
- Single instance: a named mutex. Launching a second time brings up the Keyboards window of the running instance.
- Language: `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt"` selects pt-BR. Every other language gets English. Strings live in a static lookup table (`Strings.Get("Key")`); see §8. Placeholders use `string.Format` with `{0}`.

## 1. Tray icon

| State | When | Icon | Tooltip (≤ 63 chars, the .NET Framework limit; truncate the layout name with "…") |
|---|---|---|---|
| Normal | Default | Keyboard glyph, monochrome. White on dark taskbar, black on light (read `HKCU\…\Themes\Personalize\SystemUsesLightTheme`) | `AutoLayout — {layout}` |
| Attention | An unconfigured keyboard is connected and the user has not answered "Not now" | Glyph + accent-blue dot (bottom-right) | `AutoLayout — {layout}` + newline + `Tray.Tip.Attention` |
| Error | The last apply failed (§5) | Glyph + amber warning badge | `AutoLayout — {layout}` + newline + `Tray.Tip.Error` |

Ship `.ico` files at 16/20/24/32 px in both theme variants. Load the icon with `new Icon(stream, SystemInformation.SmallIconSize)`.

**Clicks.** Handle `MouseClick` (left button) only. `DoubleClick` does the same thing, and because the action is idempotent (it activates an already-open window) a double-click does no harm.
- Attention state: opens the Keyboard dialog for the pending keyboard.
- Otherwise: shows and activates the Keyboards window (restores it if minimized).
- Right-click: the ContextMenuStrip.

**Context menu** (top to bottom):
1. `Layout: {layout}`. Disabled item that shows the current state at a glance.
2. `Set up "{name}"…`. Visible only while an unconfigured keyboard is connected. One item per such keyboard.
3. separator
4. **`&Keyboards…`** (bold, `Font = Bold`; it is the default action)
5. `&Settings…`
6. separator
7. `E&xit`

## 2. Keyboards window (`Teclados` / `Keyboards`)

`Form`, sizable, `ClientSize` 820×460, `MinimumSize` 640×380, `StartPosition = CenterScreen`, ShowInTaskbar = true while open. The app icon is the normal glyph (colored variant). **Close/Esc hides** the window; the app keeps running. Exit is only available in the tray menu. The window remembers its size (not its position).

Layout: root `TableLayoutPanel`, 1 column, 5 rows, `Dock = Fill`, `Padding = 12`.

**Row 0: status (AutoSize).** Two labels stacked in a `FlowLayoutPanel` (TopDown):
- `lblNow` (Semibold): `Main.Now.ByKeyboard` → *"Windows default layout: Português (Brasil ABNT2), because "K380" is connected."* or `Main.Now.Default` when no external keyboard is connected.
- `lblNowHint` (GrayText, `MaximumSize` width 760): `Main.Now.Hint` (explains Win+Space).

**Row 1: default layout (AutoSize, top margin 16).** `FlowLayoutPanel` LeftToRight:
- `Label` `Main.Default.Label` ("Layout with &no external keyboard:"). Its mnemonic moves focus to the combo, which is next in tab order.
- `ComboBox` DropDownList, width 340, items from §2.3. Changing it applies immediately; if no external keyboard is connected, the new layout is applied at once.
- `LinkLabel` `Common.AddLayouts` → `Process.Start("ms-settings:regionlanguage")`.

**Row 2: list caption (AutoSize, top margin 16).** `Label` `Main.List.Label` ("External &keyboards"). Its mnemonic focuses the ListView.

**Row 3: list (Percent 100).** A `Panel` (Dock Fill) containing:
- `ListView` Dock Fill: `View = Details`, `FullRowSelect`, `HideSelection = false`, `MultiSelect = false`, `HeaderStyle = Clickable` (sorting), `AccessibleName = Main.List.Label`, and `ContextMenuStrip` with the same commands as the buttons.
- `lblEmpty` Dock Fill, centered, GrayText, `BackColor = SystemColors.Window`, brought to front. Visible only when the list is empty. Text: `Main.Empty`.

Columns (width at 96 DPI):

| Column | Width | Content |
|---|---|---|
| Name | 200 | Friendly name, or the HID product string, or `VID:PID` as a last resort. Ignored rows use GrayText. The active row is **bold**. |
| Connection | 80 | `USB` / `Bluetooth` |
| VID:PID | 85 | `046D:B342` (uppercase hex) |
| Layout | 190 | Layout display name (§2.3). Shows `—` for unconfigured and ignored devices. Shows `Layout.NotInstalled` ("… (not installed)") if the layout is missing. |
| Status | 110 | `Active` (connected and currently applied) · `Connected` · `Not set up` · `Disconnected` · `Ignored` |
| Last seen | 110 | `Now` when connected, else `DateTime.ToString("g")` in the current culture |

Default sort order:
1. Active
2. Other connected keyboards, most recently connected first
3. Not set up
4. Disconnected, most recent last-seen first
5. Ignored

Clicking a column header sorts by that column; clicking it again reverses the order. The list refreshes live on device arrival/removal and keeps the selection by device key.

**Row 4: buttons (AutoSize, top margin 12).** `TableLayoutPanel` with 2 columns:
- Left `FlowLayoutPanel`: `&Edit…` · `&Ignore` / `Un&ignore` (the label toggles with the selection) · `&Forget`. These are disabled when nothing is selected.
- Right `FlowLayoutPanel` (RightToLeft): `Close` (`CancelButton`) · `&Settings…`.

### 2.1 Commands
| Command | Triggers | Behavior |
|---|---|---|
| Edit (incl. change layout, rename) | `Edit…` button, double-click row, Enter, F2, context menu | Opens the Keyboard dialog in edit mode (§3.2). F2 opens it with the Name box focused. For a *Not set up* row, it opens in "new" mode instead. |
| Ignore / Unignore | Button, context menu | Applies immediately with no confirmation, because it is reversible. Unignore sets the status to *Not set up* if no layout was ever chosen; otherwise the stored layout is kept. If the keyboard is connected, the active layout is recomputed. |
| Forget | Button, Del key, context menu | Confirmation `MessageBox` (YesNo, Question): `Forget.Confirm`. Removes the record. If the device is connected, it reappears immediately as *Not set up*, but no toast is shown and the active layout is recomputed without it. |

### 2.2 Empty state
When the list is empty, the overlay label shows:
> *No external keyboard yet. Connect a USB or Bluetooth keyboard and AutoLayout will ask which layout it uses.*

The default-layout row stays usable, so a user without external keyboards can still configure it.

### 2.3 Layout picker (shared by every layout ComboBox)
- Items are the input profiles installed for the user (language + keyboard layout). Text format: `{layout name} · {language name}`, for example `Estados Unidos-Internacional · Português (Brasil)`. The language part is needed because the same layout can be installed under two languages.
- Items are sorted by display text.
- Each item stores the profile id (`0416:00020409`) and the display text.
- The display text is cached in the data model so it can still be shown after the layout is uninstalled.
- If a keyboard's stored layout is no longer installed, it appears as a first extra item, `{name} (not installed)`. That item cannot be saved as a new choice.

## 3. Keyboard dialog (new keyboard / edit keyboard)

`Form` with `FormBorderStyle = FixedDialog`, no Minimize/Maximize, `ShowInTaskbar = true` in new mode (so it can be found again) and false in edit mode. It is **modeless**, uses `StartPosition = CenterScreen`, and has a width of 440 with AutoSize height.

Root `TableLayoutPanel`, 2 columns (Label AutoSize | control Percent 100), `Padding = 12`:

| Row | Control | Notes |
|---|---|---|
| Heading (spans 2) | Label, Segoe UI 11pt Semibold: `Kbd.Heading.New` / `Kbd.Heading.Edit` | |
| Details (spans 2) | Label GrayText: `K380 Multi-Device Keyboard · Bluetooth · 046D:B342` | Product string · connection · VID:PID |
| Name | `&Name:` + TextBox | Prefilled with the friendly name, or the product string. Empty → falls back to the product string. |
| Layout | `&Layout:` + ComboBox (§2.3) | See suggestion rules below |
| Hint (col 2) | Label GrayText, `MaximumSize` width 320 | Suggestion hint; hidden when there is no suggestion |
| Link (col 2) | LinkLabel `Kbd.MissingLayout` → `ms-settings:regionlanguage` | Refresh the combo on `Activated` so newly added layouts show up |
| Try it | `&Try it:` + TextBox (single line) | Optional (P2). On `Enter` (focus), set `InputLanguage.CurrentInputLanguage` to the selected layout so the user can type with the new keyboard and check `ç`, `´`, `/`. Never read or stored. Hint below: `Kbd.Try.Hint`. |
| Ignore (edit mode only, spans 2) | CheckBox `Kbd.IgnoreCheck` | |
| Buttons (spans 2, top margin 16) | Left: `&Not a keyboard` (new mode only). Right: `Save` (AcceptButton) · `Not now` (new) / `Cancel` (edit) as CancelButton | `Save` is disabled until a layout is selected |

### 3.1 First-connection flow
1. **An unknown, non-ignored external keyboard arrives.** The engine creates a record with state *Not set up* and the layout does **not** change. The tray icon switches to Attention. A balloon/toast is shown with:
   - title `Toast.New.Title` ("New keyboard: K380 Multi-Device Keyboard")
   - text `Toast.New.Text`, or `Toast.New.TextSuggested` ("Looks like Português (Brasil ABNT2). Click to confirm.")

   The toast is always shown, because it is a question rather than a switch notification, so the "show notifications" setting does not suppress it.
2. **Clicking the toast** (`BalloonTipClicked`), the tray icon, or the menu item opens the Keyboard dialog in new mode, activated (the user asked for it). The dialog opens **only on a click** because the user has often just plugged in a keyboard and started typing, and a window popping up would steal those keystrokes.
3. **Suggestion handling:**
   - *No suggestion:* the combo shows the placeholder `Kbd.Layout.Placeholder` (rendered as a first, non-savable item) and Save is disabled.
   - *Confident suggestion:* the suggested layout is preselected, its text gets the suffix ` (suggested)`, and the hint `Kbd.Hint.High` is shown.
   - *Uncertain suggestion:* the layout is preselected the same way, but with the hint `Kbd.Hint.Low`.
   - *Suggested layout not installed:* the placeholder is selected and the hint `Kbd.Hint.NotInstalled` ("Probably ABNT2, which isn't installed. Add it in Windows settings.") is shown.
4. **Outcomes:**
   - **Save:** the record becomes *Configured*. If the keyboard is still connected, it becomes the most recent keyboard and its layout is applied now. A switch toast is shown if enabled.
   - **Not a keyboard:** the record becomes *Ignored*. No confirmation, because the action can be reversed in the list.
   - **Not now / Esc / close:** the record stays *Not set up*, the Attention dot clears, and the tray menu item remains while the device is connected. It is asked again (toast) on the next connection.
5. **If the user ignores the toast:** nothing changes. The current layout stays (default or the previous keyboard's). The Attention icon, the tray menu item, and the *Not set up* row all persist while the device is connected. When the device is unplugged, it is still listed as *Not set up* and the next connection asks again.
6. **Several unknown keyboards at once:** they are queued. One dialog at a time, with title `Kbd.Title.Queue` ("New keyboard (1 of 2)"). After Save/Not a keyboard/Not now, the next one opens. Records are keyed by VID:PID, so several HID interfaces of the same device appear only once.

### 3.2 Edit mode
The edit dialog has the same fields. The *Ignore* checkbox replaces the *Not a keyboard* button. Save applies immediately: if the keyboard is the active one, the layout is re-applied.

## 4. Settings dialog

`Form` with `FixedDialog`, width 460, AutoSize height, modeless, single instance (re-activated if already open). It opens from the tray menu or the Keyboards window. **Every control applies immediately.** The only button is `Close` (Accept and Cancel).

Root `TableLayoutPanel`, 1 column, sections separated by 16 px, Semibold section headers:

1. **General**
   - CheckBox `&Start with Windows`. Checking it creates `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\AutoLayout.lnk`; unchecking deletes it. The checkbox state is read from whether the shortcut exists, so it is always accurate. On failure: MessageBox `Err.Startup`, and the checkbox reverts.
   - CheckBox `Show a ¬ification when the layout changes` (default **on**).
2. **Diagnostics**
   - CheckBox `&Write a log file` (default on).
   - LinkLabels `Open log` (Notepad via `Process.Start(path)`) · `Open folder`. These are disabled if the file does not exist.
   - Label (GrayText): `Set.Privacy` ("AutoLayout never reads what you type. It only watches which keyboards are connected.").
3. **Footer:** LinkLabel `&About AutoLayout…` on the left, `Close` on the right.

**About dialog** (FixedDialog, width 380): app name in 11pt Semibold, `Version {0}`, `© 2026 The AutoLayout authors`, `Licensed under the Apache License 2.0.`, and LinkLabels `View license` (opens the bundled `LICENSE`), `Credits and third-party notices` (opens `NOTICE`), and `Project page`, then an `OK` button.

## 5. Feedback and errors

**Successful switch** (only if *Show notification* is on; balloon with `ToolTipIcon.None`):
- *Keyboard connected:* title = layout name, text `Toast.Applied.Text` ("K380" connected.).
- *Keyboard disconnected and the layout reverted:* title = default layout name, text `Toast.Reverted.Text`.
- Coalesce bursts: if several events land within 1 s, show one toast for the final state.

Whatever the notification setting, the tooltip, the menu header, and the Keyboards status line always reflect the current state.

**Errors.** Each error shows a toast even when notifications are off, puts the icon in the Error state, writes to the log, and is shown at most once per device per session. The error state clears on the next successful apply.

| Situation | Behavior | Toast | Click opens |
|---|---|---|---|
| A keyboard's layout is no longer installed | Layout unchanged. The row shows "(not installed)". | `Toast.Err.Missing` | Keyboard dialog (edit) for that device |
| The default layout is no longer installed | Layout unchanged | `Toast.Err.DefaultMissing` | Keyboards window, focus on the default combo |
| Setting the default input method failed | Layout unchanged | `Toast.Err.Apply` | Keyboards window. The status line shows `Main.Now.Error` with a "View log" LinkLabel. |
| Only the foreground-window switch failed (e.g. an elevated window) | Default input method is still set | none (log only) | n/a |

## 6. Accessibility and keyboard

- **Tab order** follows visual order (set `TabIndex` explicitly). Every input has a preceding Label with a mnemonic. Every form defines AcceptButton/CancelButton. Esc closes any window.
- **ListView keys:** Enter = Edit, F2 = Edit (Name focused), Del = Forget, Shift+F10 / Menu key = context menu.
- **Screen-reader names:**
  - The tray icon is read via its tooltip text.
  - `ListView.AccessibleName` = "External keyboards".
  - Status icons (if a SmallImageList is used) are decorative; the Status column carries the text.
  - The Error-state LinkLabel has `AccessibleName` "View log".
  - The empty-state label has `AccessibleRole = StaticText`.
- **No information by color or icon alone:** active rows use bold *and* the "Active" status text, and ignored rows use gray *and* the "Ignored" status text.

## 7. Data the UI needs

**Per keyboard (persisted):**

| Field | Type | Notes |
|---|---|---|
| `Key` | string | Stable id, e.g. `046D:B342`. Must also encode the vendor-id source if Bluetooth uses Bluetooth-SIG ids (e.g. `BT:046D:B342`). |
| `Vid`, `Pid` | ushort | For the VID:PID column |
| `ProductName` | string | HID product string at first sight. May be empty. |
| `FriendlyName` | string, nullable | User-defined name |
| `Connection` | enum Usb, Bluetooth | Last observed |
| `State` | enum NotSetUp, Configured, Ignored | |
| `LayoutProfileId` | string, nullable | e.g. `0416:00010416` (language:KLID). This is what gets applied. |
| `LayoutDisplayName` | string, nullable | Cached display text, shown when the layout is uninstalled |
| `FirstSeen`, `LastSeen` | DateTime (UTC) | LastSeen is updated on arrival and removal |

**Per keyboard (runtime only):**
- `IsConnected`
- `ConnectedAt`: ordering for "most recent wins"
- `IsActive`
- `SuggestedProfileId` and `SuggestionConfidence` (None/Low/High)
- `LastError` (None/LayoutMissing/ApplyFailed)
- `ErrorShownThisSession`
- `DeviceInstanceIds` (list, for de-duplicating HID interfaces)

**Global (persisted):**
- `DefaultLayoutProfileId` + `DefaultLayoutDisplayName`
- `NotifyOnSwitch` (true)
- `LogEnabled` (true)
- `MainWindowSize`
- `FirstRunDone`

`StartWithWindows` is not stored; it is derived from whether the shortcut exists.

**Global (runtime):**
- `ActiveSourceKey` (keyboard key, or null = default)
- `AppliedProfileId`
- `LastApplyError`
- `PendingQueue` (keys awaiting the dialog)
- `InstalledProfiles` (id + display text, refreshed on `WM_INPUTLANGCHANGE`/`WM_SETTINGCHANGE` and on dialog activation)

**First run:** the Keyboards window opens once, with the default layout prefilled from the current Windows default input method. Already-connected external keyboards go through §3.1 (toast, then dialog).

## 8. Strings (resource keys)

Keys are used with `Strings.Get(key)`. `{n}` = `string.Format` argument. Curly quotes are intentional.

| Key | pt-BR | en |
|---|---|---|
| App.Name | AutoLayout | AutoLayout |
| Tray.Tip | AutoLayout — {0} | AutoLayout — {0} |
| Tray.Tip.Attention | Teclado novo: clique para configurar | New keyboard: click to set up |
| Tray.Tip.Error | Problema: clique para ver | Problem: click for details |
| Menu.Header | Layout: {0} | Layout: {0} |
| Menu.SetUp | Configurar “{0}”… | Set up “{0}”… |
| Menu.Keyboards | &Teclados… | &Keyboards… |
| Menu.Settings | &Configurações… | &Settings… |
| Menu.Exit | Sai&r | E&xit |
| Main.Title | Teclados — AutoLayout | Keyboards — AutoLayout |
| Main.Now.ByKeyboard | Layout padrão do Windows: {0}, porque “{1}” está conectado. | Windows default layout: {0}, because “{1}” is connected. |
| Main.Now.Default | Layout padrão do Windows: {0} (nenhum teclado externo conectado). | Windows default layout: {0} (no external keyboard connected). |
| Main.Now.Error | Não foi possível aplicar {0}. | Couldn’t apply {0}. |
| Main.Now.Hint | Você ainda pode trocar com Win+Espaço. O AutoLayout só age quando um teclado é conectado ou desconectado. | You can still switch with Win+Space. AutoLayout only acts when a keyboard is connected or disconnected. |
| Main.Default.Label | Layout &sem teclado externo: | Layout with &no external keyboard: |
| Common.AddLayouts | Adicionar layouts no Windows… | Add layouts in Windows… |
| Common.ViewLog | Ver log | View log |
| Main.List.Label | Teclados e&xternos | External &keyboards |
| Main.Empty | Nenhum teclado externo ainda. Conecte um teclado USB ou Bluetooth e o AutoLayout vai perguntar qual layout ele usa. | No external keyboard yet. Connect a USB or Bluetooth keyboard and AutoLayout will ask which layout it uses. |
| Col.Name | Nome | Name |
| Col.Connection | Conexão | Connection |
| Col.VidPid | VID:PID | VID:PID |
| Col.Layout | Layout | Layout |
| Col.Status | Status | Status |
| Col.LastSeen | Visto por último | Last seen |
| Status.Active | Ativo | Active |
| Status.Connected | Conectado | Connected |
| Status.NotSetUp | Não configurado | Not set up |
| Status.Disconnected | Desconectado | Disconnected |
| Status.Ignored | Ignorado | Ignored |
| Layout.None | — | — |
| Layout.NotInstalled | {0} (não instalado) | {0} (not installed) |
| LastSeen.Now | Agora | Now |
| Btn.Edit | &Editar… | &Edit… |
| Btn.Ignore | &Ignorar | &Ignore |
| Btn.Unignore | Não &ignorar | Un&ignore |
| Btn.Forget | E&squecer | &Forget |
| Btn.Settings | &Configurações… | &Settings… |
| Btn.Close | Fechar | Close |
| Btn.OK | OK | OK |
| Forget.Title | Esquecer teclado | Forget keyboard |
| Forget.Confirm | Esquecer “{0}”? Se ele for conectado de novo, o AutoLayout vai perguntar o layout outra vez. | Forget “{0}”? If it is connected again, AutoLayout will ask for its layout again. |
| Kbd.Title.New | Teclado novo | New keyboard |
| Kbd.Title.Edit | Editar teclado | Edit keyboard |
| Kbd.Title.Queue | {0} ({1} de {2}) | {0} ({1} of {2}) |
| Kbd.Heading.New | Um teclado novo foi conectado | A new keyboard was connected |
| Kbd.Heading.Edit | {0} | {0} |
| Kbd.Name | &Nome: | &Name: |
| Kbd.Layout | &Layout: | &Layout: |
| Kbd.Layout.Placeholder | Escolha um layout… | Choose a layout… |
| Kbd.Suggested | {0} (sugerido) | {0} (suggested) |
| Kbd.Hint.High | Sugerido com base no modelo do teclado. Confira antes de salvar. | Suggested from the keyboard model. Check it before saving. |
| Kbd.Hint.Low | Palpite incerto: este modelo é vendido com layouts diferentes. Use “Testar” para conferir. | Uncertain guess: this model is sold with different layouts. Use “Try it” to check. |
| Kbd.Hint.NotInstalled | Provavelmente {0}, que não está instalado. Adicione-o nas configurações do Windows. | Probably {0}, which isn’t installed. Add it in Windows settings. |
| Kbd.MissingLayout | O layout não está na lista? | Layout not listed? |
| Kbd.Try | &Testar: | &Try it: |
| Kbd.Try.Hint | Digite aqui com o teclado novo (ex.: ç, ´, /). Nada é gravado. | Type here with the new keyboard (e.g. ç, ´, /). Nothing is stored. |
| Kbd.IgnoreCheck | Não é um teclado (mouse, receptor etc.): &ignorar | Not a keyboard (mouse, receiver, etc.): &ignore it |
| Kbd.Btn.Save | Salvar | Save |
| Kbd.Btn.NotKeyboard | &Não é um teclado | &Not a keyboard |
| Kbd.Btn.Later | Agora não | Not now |
| Kbd.Btn.Cancel | Cancelar | Cancel |
| Set.Title | Configurações — AutoLayout | Settings — AutoLayout |
| Set.General | Geral | General |
| Set.Startup | &Iniciar com o Windows | &Start with Windows |
| Set.Notify | &Mostrar notificação ao trocar de layout | Show a &notification when the layout changes |
| Set.Diagnostics | Diagnóstico | Diagnostics |
| Set.Log | &Gravar arquivo de log | &Write a log file |
| Set.OpenLog | Abrir log | Open log |
| Set.OpenFolder | Abrir pasta | Open folder |
| Set.Privacy | O AutoLayout nunca lê o que você digita: ele só observa quais teclados estão conectados. | AutoLayout never reads what you type. It only watches which keyboards are connected. |
| Set.About | &Sobre o AutoLayout… | &About AutoLayout… |
| About.Title | Sobre o AutoLayout | About AutoLayout |
| About.Version | Versão {0} | Version {0} |
| About.Copyright | © 2026 Os autores do AutoLayout | © 2026 The AutoLayout authors |
| About.License | Licenciado sob a Apache License 2.0. | Licensed under the Apache License 2.0. |
| About.ViewLicense | Ver licença | View license |
| About.Credits | Créditos e avisos de terceiros | Credits and third-party notices |
| About.Website | Página do projeto | Project page |
| Toast.New.Title | Teclado novo: {0} | New keyboard: {0} |
| Toast.New.Text | Clique para escolher o layout. Até lá, nada muda. | Click to choose its layout. Until then, nothing changes. |
| Toast.New.TextSuggested | Parece ser {0}. Clique para confirmar. | Looks like {0}. Click to confirm. |
| Toast.Applied.Text | “{0}” conectado. | “{0}” connected. |
| Toast.Reverted.Text | “{0}” desconectado. Voltou ao layout padrão. | “{0}” disconnected. Back to the default layout. |
| Toast.Err.Missing.Title | Layout não instalado | Layout not installed |
| Toast.Err.Missing | “{0}” usa {1}, que não está mais instalado. Clique para escolher outro. | “{0}” uses {1}, which is no longer installed. Click to choose another. |
| Toast.Err.DefaultMissing | O layout padrão {0} não está mais instalado. Clique para escolher outro. | The default layout {0} is no longer installed. Click to choose another. |
| Toast.Err.Apply.Title | Não foi possível trocar o layout | Couldn’t switch the layout |
| Toast.Err.Apply | O Windows não aceitou {0}. Clique para ver detalhes. | Windows didn’t accept {0}. Click for details. |
| Err.Startup | Não foi possível alterar a inicialização com o Windows: {0} | Couldn’t change the start-with-Windows setting: {0} |
| Conn.Usb | USB | USB |
| Conn.Bluetooth | Bluetooth | Bluetooth |

Length limits: balloon title ≤ 63 chars, balloon text ≤ 255, tooltip ≤ 63 (.NET Framework). Truncate `{0}` names with "…" before formatting, never the fixed text.
