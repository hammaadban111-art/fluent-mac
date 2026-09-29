# Fluent for Windows — CI run 36571008403

- Commit: `66a05cdb88db2d98363ba6282e77499e5e4bba4c`, version 2.2.1
- Runner: Microsoft Windows Server 2025 Datacenter 10.0.26100.0, .NET 10.0.401
- Screen: 1024x768

## Unit tests
- ✅ Passed!  - Failed:     0, Passed:    85, Skipped:     0, Total:    85, Duration: 5 s - Fluent.Core.Tests.dll (net10.0)

## Build
- ✅ Fluent-Setup-2.2.1.exe 67.6 MB; Fluent.exe x64 73.1 MB, arm64 69 MB
- SHA-256 (setup): 9FC98B3748D035744B0D40065184954271B289B183EA7DCDE448781E8DCD110F

## Installer
- ✅ installer exit code 0
- ✅ Fluent.exe in %LOCALAPPDATA%\Programs\Fluent
- ✅ Start menu shortcut
- ✅ desktop shortcut
- ✅ listed in Settings > Apps (uninstall entry)
- ✅ open-at-sign-in entry
- ✅ file version is 2.2.1

## Screens
- ✅ screens ok: 66, missing: 0 

## End-to-end on Windows: 45/45 passed, 0 required failure(s)
- ✅ Fluent starts and answers
- ✅ Right Ctrl on its own can be recorded — hold=Right Ctrl
- ✅ Left Ctrl on its own can be recorded — hold=Left Ctrl
- ✅ Left Shift on its own can be recorded — toggle=Left Shift
- ✅ Right Shift on its own can be recorded — toggle=Right Shift
- ✅ Ctrl + L can be recorded — toggle=Ctrl + L
- ✅ Reset to defaults restores Right Ctrl and Ctrl + Alt + Space — button=found hold=Right Ctrl toggle=Ctrl + Alt + Space
- ✅ Notepad opens
- ✅ Notepad text area found — Document
- ✅ Fluent sees the Notepad text box — process=notepad type=Document kind=Editable
- ✅ bubble appears next to the text box — bubble=[792,520,58,58] field=[112,155,752,437]
- ✅ bubble is round and bubble-sized — 58x58 px
- ✅ the orb is drawn where Fluent says the bubble is — pixel=235,140,208
- ✅ shortcut starts a dictation — phase=Recording
- ✅ capsule shows while recording — [280,10,464,82]
- ✅ bubble hides while the capsule owns the session
- ✅ shortcut dictation lands in Notepad — text="sounds good are you free for lunch tomorrow let's do twelve if that works" route=live insert=Pasted: sounds good are you free for lunch tomorrow let's do twelve if that works
- ✅ transcript came over Gemini Live (streamed) — latency=210.6567 ms
- ✅ latency after stop under 2 s — 210.6567 ms
- ✅ the transcript is also left on the clipboard — clipboard="sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ Notepad kept the focus
- ✅ bubble click starts a dictation
- ✅ bubble click does not steal focus
- ✅ capsule Stop button reachable
- ✅ capsule Stop inserts, spaced after the earlier text — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ Notepad still focused after the capsule click
- ✅ holding Right Ctrl dictates and inserts on release — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ a custom hold key (F8) dictates and inserts on release — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ a custom start/stop key (F9) starts and stops — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ a modifier-only start/stop (tap Right Shift) works — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works"
- ✅ rejected live socket falls back to the batch request — route=batch batchHits=1
- ✅ Style applies (Other → Excited turns the full stop into !) — text="sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works sounds good are you free for lunch tomorrow let's do twelve if that works Batch fallback works!"
- ✅ Esc cancels without inserting — phase=Idle
- ✅ Edge opens the test page
- ✅ Edge Message: Fluent sees an editable box — type=Edit kind=Editable bubble=true
- ✅ Edge Message: text inserted — insert={
   "kind": "Pasted",
   "detail": "Hello from Fluent"
 } text="Hello from Fluent"
- ✅ Edge Subject: Fluent sees an editable box — type=Edit kind=Editable bubble=true
- ✅ Edge Subject: text inserted — insert={
   "kind": "Pasted",
   "detail": " lunch tomorrow"
 } text="Re: lunch tomorrow"
- ✅ Edge Editor: Fluent sees an editable box — type=Edit kind=Editable bubble=true
- ✅ Edge Editor: text inserted — insert={
   "kind": "Pasted",
   "detail": "Typed into a rich editor"
 } text="Typed into a rich editor"
- ✅ a box UI Automation calls not editable still gets the paste — kind=NotEditable type=Group insert={
   "kind": "Pasted",
   "detail": "pasted into a custom box"
 } out="pasted into a custom box"
- ✅ pasted text is also on the clipboard
- ✅ password field: no bubble — kind=Secure
- ✅ password field: Fluent refuses to type, only copies — {"kind":"Copied","detail":"password field"}
- ✅ Fluent quits cleanly

## Uninstall
- ✅ app removed
- ✅ sign-in entry removed

- Draft release: windows-ci-draft

