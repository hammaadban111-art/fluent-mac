# Fluent for Windows

Speak in any app; Fluent writes it for you. The Windows edition of Fluent (Android, iPhone, Mac), with the
same Gemini Live streaming, Style formatter, six themes and clickwrap terms.

## 1.2 (2026-09-29)

- Pastes into every app, not just browsers: 1.0 refused when UI Automation reported the focused thing as a plain
  pane (WhatsApp from the Microsoft Store hosts WebView2 that way), and showed "Copied — press Ctrl+V". Now only
  password fields are refused.
- Every transcript is also left on the clipboard ("Pasted · also copied").
- Hold-to-talk and start/stop take any key or combination, recorded in Settings (Wispr Flow style). Modifier-only
  start/stop fires on a quick tap; the ordinary key of a combination is swallowed while Fluent uses it.
- If Gemini Live is not final 1.2 s after stop, the batch request starts too and the first answer wins.

## How it works

| Piece | Where |
|---|---|
| Gemini Live streaming + batch fallback (batch also starts if live is not final 1.2 s after stop: `TranscriptRace`), Style formatter, spacing rules, settings, history | `src/Fluent.Core` (plain .NET, tested on any OS) |
| Bubble next to the focused text box, recording capsule, tray icon | `src/Fluent/Overlays.cs`, `Tray.cs` |
| Finding the text box (UI Automation on its own thread, with timeouts) | `src/Fluent/FieldFinder.cs` |
| Putting text in: clipboard + Ctrl+V into whatever app is in front (even when UI Automation calls it "not a text box", e.g. WhatsApp from the Store); the transcript stays on the clipboard afterwards; Unicode typing fallback; password fields only get a copy | `src/Fluent/TextInserter.cs` |
| Hold-to-talk and start/stop on any key or combination the user records in Settings (one low-level keyboard hook; `KeyCombo`, `KeyComboRecorder`, `HotkeyEngine` in `Fluent.Core/Shortcuts.cs`, unit-tested) | `src/Fluent/Hotkeys.cs` |
| Microphone at 16 kHz mono PCM16 | `src/Fluent/Recorder.cs` |
| Gemini key encrypted for the Windows user (DPAPI) | `src/Fluent/Services.cs` |
| Screens (Terms, setup, Dictate, History, Style, Settings) | `src/Fluent/Views/` |

No admin rights anywhere: per-user install, per-user sign-in entry, no services, no drivers.

## Build

On Windows: `pwsh scripts/build.ps1 -Version 1.2` → `dist/Fluent-Setup-1.2.exe` (Inno Setup),
`dist/win-x64/Fluent.exe`, `dist/win-arm64/Fluent.exe`.

On a Mac (compile check and unit tests only): `dotnet test tests/Fluent.Core.Tests` and
`dotnet build src/Fluent/Fluent.csproj`.

CI (`.github/workflows/windows-ci.yml`, branch `windows`): unit tests, build, silent install, screenshots of
every screen in every theme, end-to-end tests against Notepad and Edge (`tests/Fluent.E2E`), uninstall.
Results land on the `windows-ci-results` branch; the installer on a draft release.

## Logo

Only the Android app mark (`website-v2/favicon.svg`). `scripts/make_icons.py` renders it; never redraw it.
