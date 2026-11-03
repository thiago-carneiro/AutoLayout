# AutoLayout

Windows has one keyboard layout at a time, not one per keyboard. If your laptop's keyboard is US-International and your desk keyboard is Brazilian ABNT2, you end up pressing Win+Space all day — and Windows switches back to its default after the lock screen anyway.

AutoLayout sits in the tray and sets the Windows **default keyboard layout** according to the keyboards connected:

- **No external keyboard:** your default layout (e.g. US-International for the built-in keyboard).
- **An external keyboard you set up is connected:** its layout. With several, the last one connected wins.
- **First time a USB or Bluetooth keyboard is connected:** a notification asks for its layout, with a suggestion when one can be made. Nothing changes until you answer.
- **Someone uses this PC through Remote Desktop:** Windows uses the layout of the remote computer's keyboard and AutoLayout stays out of the way (or uses a layout you choose for Remote Desktop). Back at the console, the rules above apply again.

Because it is the *default* layout that changes, new windows and the session after the lock screen get the right layout too. Win+Space still works as usual: AutoLayout only acts when a keyboard is connected or disconnected.

**AutoLayout never reads what you type.** It does not hook, record or inject keystrokes. It only lists keyboard devices and listens for device, session and display notifications. It needs no administrator rights and makes no network connections.

## ThinkPad keyboard backlight

ThinkPads turn the keyboard backlight off when the screen turns off, and at every boot, and leave it off. On ThinkPads, AutoLayout can put back the level you chose with Fn+Space (off, low, high) when the screen turns on, after sleep and when Windows starts. Choosing "off" with Fn+Space is respected. The option only appears when Lenovo's ThinkPad power management driver is installed.

Hat tip to [SaltwaterC/KeyboardBacklightForLenovo](https://github.com/SaltwaterC/KeyboardBacklightForLenovo), which showed that Lenovo's own drivers expose the keyboard backlight to applications. AutoLayout's implementation is its own.

## Building

Only Windows is needed — no SDK, no Visual Studio. In PowerShell:

```powershell
.\build.ps1
```

This uses the .NET Framework 4.8 MSBuild included in Windows 10 and 11 and writes `src\AutoLayout\bin\Release\AutoLayout.exe`. A warning about missing reference assemblies (MSB3644) is expected without the .NET Framework Developer Pack.

`tools\make-icon.ps1` regenerates the icon.

### Smart App Control

A freshly built `AutoLayout.exe` is not signed, so Windows 11 blocks it when **Smart App Control** is on. Releases need to be signed with a trusted code-signing certificate; until then, AutoLayout runs on PCs where Smart App Control is off.

## Using it

Run `AutoLayout.exe`. On first run the **Keyboards** window opens with your current default layout selected. Close it; AutoLayout keeps running in the tray.

- **Tray icon:** click to open the Keyboards window (or to set up a new keyboard when one is waiting). Right-click for the menu.
- **Keyboards window:** the layout without external keyboards, and every keyboard seen, with its layout and status. Edit (Enter / F2), ignore (for mice and receivers that Windows lists as keyboards) or forget (Del) a keyboard.
- **Settings:** start with Windows, notifications on layout changes, ThinkPad backlight, log file.

Settings and the log are in `%LocalAppData%\AutoLayout`.

## Layout suggestions

Keyboards rarely say which layout they have: most vendors use the same product id for every regional version, and the HID country code is almost always zero. AutoLayout suggests a layout only from what is reliable — words in the product name such as "ABNT" or "Japanese", Apple keyboards whose product id tells the JIS variant (ids from the Linux kernel's `hid-ids.h`), and, as a weak guess, the layout of your Windows region. You always confirm.

## License

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).
