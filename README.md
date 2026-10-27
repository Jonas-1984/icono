# icono

Icon and floor plan editor for Windows (WPF, .NET 8, SkiaSharp). Draw walls with typed lengths, place doors, windows and kitchen objects from the
OBJECTS catalogue, add dimensions, export to SVG / PNG / ICO.

*Status: test version `0.1.0-test.1` - not final, expect errors.*

## Privacy

icono does not connect to the internet, has no telemetry and no account. Settings, the list of recent files and an error log stay in
`%LocalAppData%\Icono` on your device. Details: [installer/docs/Datenschutz-DE.txt](installer/docs/Datenschutz-DE.txt) /
[Privacy-EN.txt](installer/docs/Privacy-EN.txt).

## Build

```
dotnet build Icono.sln
dotnet test Icono.sln
```

## Installer (for testers)

Needs the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

This runs the tests, publishes a self-contained Windows x64 build and writes `installer\Output\ICONO-Setup-<version>.exe` together with `SHA256.txt`.
The installer is per user by default (no administrator rights), shows the test terms and the privacy notice, and offers to delete the local data
when uninstalling. Release builds leave out the Sinks catalogue until the origin of its drawings is documented (see `src/Icono.Rendering/Icono.Rendering.csproj`).

The version is set in `Directory.Build.props` and `installer/icono.iss` (keep both in step).

## Licences

Third-party components and their licences: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Copyright (c) 2026 Jonas Shojaei (bitbins.de).
