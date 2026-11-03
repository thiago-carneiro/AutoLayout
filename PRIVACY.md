# AutoLayout privacy policy

Last updated: 2026-11-03

AutoLayout is a free, open-source Windows app that sets the default keyboard layout according to the keyboards connected. This page says what it handles and where it goes. In short: everything stays on your PC.

## What AutoLayout does not do

- It does not read, record, hook or inject keystrokes. It never sees what you type.
- It makes no network connections and has no telemetry, analytics or crash reporting of its own.
- It does not need, and does not ask for, administrator rights.

## What it reads

- The list of keyboard devices connected, with their vendor and product ids and the name Windows has for each one.
- Whether the session is a Remote Desktop session.
- The keyboard layouts installed in Windows and the current default layout.
- On ThinkPads with Lenovo's power management driver, the keyboard backlight level.

## What it keeps, and where

In `%LocalAppData%\AutoLayout`, in your Windows profile (with the Store version, Windows keeps this folder in the app's own package data and deletes it when the app is uninstalled):

- `settings.json`: the keyboards you set up (vendor and product ids, product name, the name you gave them, their layout, and when each one was first and last seen) and your settings.
- `autolayout.log`, only if you turn the log on in Settings: when keyboards were connected and disconnected and which layout was applied. It is limited to 256 KB and is overwritten after that.

Nothing is sent anywhere. Uninstalling AutoLayout and deleting that folder removes everything it kept.

## Microsoft Store

If you install AutoLayout from the Microsoft Store, Microsoft handles the download, installation and updates under its own privacy statement, and may report install counts and crash data to the developer through Partner Center. AutoLayout itself adds nothing to that.

## Contact

Questions and reports: <https://github.com/thiago-carneiro/AutoLayout/issues>.
