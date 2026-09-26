# Fluent for Windows

Speak in any app; Fluent writes it for you. The Windows edition of Fluent (Android, iPhone, Mac), with the
same Gemini Live streaming, Style formatter, six themes and clickwrap terms.

## How it works

| Piece | Where |
|---|---|
| Gemini Live streaming + batch fallback, Style formatter, spacing rules, settings, history | `src/Fluent.Core` (plain .NET, tested on any OS) |
| Bubble next to the focused text box, recording capsule, tray icon | `src/Fluent/Overlays.cs`, `Tray.cs` |
| Finding the text box (UI Automation on its own thread, with timeouts) | `src/Fluent/FieldFinder.cs` |
| Putting text in: clipboard + Ctrl+V, user's clipboard restored, kept out of Win+V history; Unicode typing fallback | `src/Fluent/TextInserter.cs` |
| Start/stop shortcut (RegisterHotKey) and hold-to-talk (watch-only keyboard hook) | `src/Fluent/Hotkeys.cs` |
| Microphone at 16 kHz mono PCM16 | `src/Fluent/Recorder.cs` |
| Gemini key encrypted for the Windows user (DPAPI) | `src/Fluent/Services.cs` |
| Screens (Terms, setup, Dictate, History, Style, Settings) | `src/Fluent/Views/` |

No admin rights anywhere: per-user install, per-user sign-in entry, no services, no drivers.

## Build

On Windows: `pwsh scripts/build.ps1 -Version 1.0.0` → `dist/Fluent-Setup-1.0.0.exe` (Inno Setup),
`dist/win-x64/Fluent.exe`, `dist/win-arm64/Fluent.exe`.

On a Mac (compile check and unit tests only): `dotnet test tests/Fluent.Core.Tests` and
`dotnet build src/Fluent/Fluent.csproj`.

CI (`.github/workflows/windows-ci.yml`, branch `windows`): unit tests, build, silent install, screenshots of
every screen in every theme, end-to-end tests against Notepad and Edge (`tests/Fluent.E2E`), uninstall.
Results land on the `windows-ci-results` branch; the installer on a draft release.

## Logo

Only the Android app mark (`website-v2/favicon.svg`). `scripts/make_icons.py` renders it; never redraw it.
