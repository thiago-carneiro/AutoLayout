# Publishing AutoLayout on the Microsoft Store

AutoLayout is published free of charge. The Store re-signs the package with a Microsoft certificate, so no code-signing certificate is needed, and Windows' Smart App Control trusts the result.

## 1. Developer account (once)

1. Start at <https://storedeveloper.microsoft.com> and choose **Individual**. Starting anywhere else (Partner Center, Visual Studio) leads to the older, paid flow.
2. Sign in with your personal Microsoft account and verify your identity with a government-issued ID and a selfie. Registration is free for individual developers since September 2025.
3. The verification can take up to 30 minutes to show in Partner Center.

## 2. Reserve the name

In [Partner Center](https://partner.microsoft.com/dashboard/apps-and-games/overview) → **Apps and games** → **New product** → **MSIX or PWA app**, reserve **AutoLayout**. If it is taken, alternatives that keep the meaning: *AutoLayout Keyboard*, *AutoLayout — keyboard layout switcher*. The display name in the package must match the reserved name exactly.

## 3. Product identity

Open the product → **Product management** → **Product identity** and copy three values into `src/AutoLayout/Package.appxmanifest`:

| Partner Center | Manifest |
|---|---|
| *Package/Identity/Name* | `<Identity Name="…">` |
| *Package/Identity/Publisher* | `<Identity Publisher="CN=…">` |
| *Package/Properties/PublisherDisplayName* | `<PublisherDisplayName>` |

If the reserved name is not "AutoLayout", also change `DisplayName` in `<Properties>` and in `<uap:VisualElements>`.

The version is set in two places that must agree: `<Version>` in `src/AutoLayout/AutoLayout.csproj` (for example `1.0.0`) and `Version` in the manifest's `<Identity>` (`1.0.0.0`; the Store requires the last number to be 0). Each submission needs a higher version than the previous one.

## 4. Build the packages

```powershell
.\build.ps1 -Store
```

This runs the tests and writes `dist\store\AutoLayout_<version>_x64_Test\AutoLayout_<version>_x64.msix` and the ARM64 equivalent. The packages are unsigned on purpose: the Store signs them. (The `Dependencies` folders next to them hold the Windows App Runtime packages for side-loading; the Store installs that runtime itself, so do not upload them.)

To try a package on your own PC before submitting, it has to be signed with a certificate the PC trusts; the simplest path is to test the unpackaged build (`.\build.ps1`) instead, which is the same app.

## 5. Submission

Create a submission and fill in each section.

**Pricing and availability**: Free; all markets; public audience.

**Properties**
- Category: *Utilities & tools*.
- Privacy policy URL: `https://github.com/thiago-carneiro/AutoLayout/blob/main/PRIVACY.md`.
- Website: `https://github.com/thiago-carneiro/AutoLayout`; support contact: `https://github.com/thiago-carneiro/AutoLayout/issues`.
- Product declarations: it does not access, collect or transmit personal information (it reads device ids and names locally and keeps them on the PC).

**Age ratings**: answer the questionnaire; AutoLayout has no user-generated content, no communication between users, no purchases and no network access.

**Packages**: upload both `.msix` files (x64 and ARM64). Device family: Windows 10/11 Desktop.

**Restricted capability `runFullTrust`**: Partner Center asks why. A text that fits:

> AutoLayout is a desktop (Win32) app built with WinUI 3. It needs full trust to list the keyboard devices connected (Configuration Manager API), to set the Windows default input language (input.dll, Text Services Framework), and to receive device and session notifications through a hidden window. It does not read, hook or inject keystrokes and makes no network connections. Source code: https://github.com/thiago-carneiro/AutoLayout

**Store listing** (at least en-US; the other languages are optional and can reuse `docs/store-listing.md`):
- Description, short description and features: `docs/store-listing.md`.
- Screenshots: PNG, at least 1366 × 768, up to 10. `docs/images/store/` has them; regenerate with `AutoLayout.exe --demo --capture <folder>` after changing the interface.
- Search terms: keyboard layout, ABNT2, US International, external keyboard, input language.

**Notes for certification** (Submission options):

> AutoLayout runs in the notification area. To see it work, connect a USB or Bluetooth keyboard: a notification asks for its layout. Without one, the main window shows the default layout and the list of keyboards seen.

Submit. Certification usually takes from a few hours to a few days; Partner Center e-mails the result.

## 6. Updates

Raise the version (step 3), run `.\build.ps1 -Store`, create a new submission, upload the new packages and remove the old ones from the submission.
