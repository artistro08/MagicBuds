<p align="center">
    <img src="src/MagicBuds.App/Assets/Square150x150Logo.scale-200.png" width="96" alt="MagicBuds icon">
</p>

<h1 align="center">
    MagicBuds
</h1>

<p align="center">
    A Windows 11 tray app that controls Pixel Buds Pro 2 from a native WinUI flyout.
</p>

## Introduction

MagicBuds puts the controls from [mypixelbuds.google.com](https://mypixelbuds.google.com) in your tray. It talks to the buds over Bluetooth directly, so you don't need Chrome open.

- **Flyout:** each bud and the case in a battery ring, and the four noise modes: Noise Cancellation, Off, Adaptive and Transparency.
- **Settings › Buds:** Conversation Detection, Multipoint and the equalizer, with the web app's presets.
- **Settings › Controls and gestures:** touch controls, head gestures, press and hold for each earbud, and which noise modes press and hold cycles through.
- **Settings › Shortcuts:** switch noise modes from anywhere in Windows. Win+F10 is Noise Cancellation, Win+F11 Adaptive, Win+F12 Transparency and Win+F6 Off, and you can change or clear each one.
- **Settings › Devices:** pick your buds' color (Porcelain, Green, Charcoal or Pink) so the pictures match.
- Low battery notifications, start at sign-in, and light and dark themes.

> Only one app can hold the buds' control link at a time. Close the web app before switching to MagicBuds.

## Building From Source

### Prerequisites

- Windows 11 22H2 or newer
- .NET 10 SDK
- Visual Studio 2026 with the .NET desktop development workload and the Windows App SDK C# templates (only needed for packaging)

### Building

Build everything and run the tests:

```powershell
.\scripts\Build.ps1
```

To close a running copy cleanly first (it lets go of the buds properly, which a killed process can't):

```powershell
.\src\MagicBuds.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MagicBuds.exe --quit
```

### App Icons

The tray icons, the app icon and every package logo are drawn from `assets-src\buds.svg`. After changing it, run:

```powershell
pwsh -STA -File .\scripts\New-AppAssets.ps1
```

### Packaging

1. Once, from an elevated PowerShell, create and trust the signing certificate:

    ```powershell
    .\scripts\New-DevCertificate.ps1
    ```

2. Build the signed MSIX packages only:

    ```powershell
    .\scripts\Build-Package.ps1
    ```

    Or build every release file (MSIX and MSI, x64 and ARM64, plus the certificate) into `artifacts\release`:

    ```powershell
    .\scripts\Build-Release.ps1 -Version 1.0.0.0
    ```

## Documentation

How the buds' protocol works, with a capture and a decoder, is in [docs/protocol/notes.md](docs/protocol/notes.md).

## License

MIT, see [LICENSE](LICENSE). Notices for the work it builds on are in [LICENSE-THIRD-PARTY](LICENSE-THIRD-PARTY):

- The protocol details are based on [pbpctrl](https://github.com/qzed/pbpctrl) (MIT).
- The shortcut picker is ported from [PowerToys](https://github.com/microsoft/PowerToys) (MIT).
- Three of the noise mode icons are [Material Symbols](https://fonts.google.com/icons) (Apache 2.0). The Adaptive icon and the buds pictures are Google's.

## Disclaimer

MagicBuds is an independent, unofficial app. It's in no way affiliated with, associated with, authorized by, or endorsed by Google LLC. Google, Pixel and Pixel Buds are trademarks of Google LLC, used here only to say which earbuds the app works with.
