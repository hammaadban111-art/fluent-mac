# Fluent for Mac — CI run 36568499850

- Commit: `22c0d3b4346a43278462b006128c47164151b7df`
- Xcode 26.6, swift-driver version: 1.148.6 Apple Swift version 6.3.3 (swiftlang-6.3.3.1.3 clang-2100.1.1.101)
- Runner: macOS 26.6.2, arm64, 3 CPUs

## Unit tests
- ✅ ✔ Test run with 83 tests in 20 suites passed after 1.358 seconds.
## Build
- ✅ Fluent.app 1.0.0 (x86_64 arm64), zip 2.6M
## End-to-end on macOS 26.6.2 (arm64)
- ✅ Fluent is trusted for Accessibility on the runner (TCC rows written by ci-grant-tcc.sh)
- TextEdit field as Fluent sees it: `{"app": "TextEdit", "bundleID": "com.apple.TextEdit", "frame": [79, 88, 656, 384], "kind": "editable", "role": "AXTextArea", "selectedTextSettable": true, "subrole": "", "valueSettable": true}`
- ✅ TextEdit: inserted via **accessibility**; read back through Accessibility by a fresh process, and saved to disk as exactly "Fluent typed this into TextEdit on a Mac runner."
- ✅ TextEdit: a second dictation got exactly one separating space: "Fluent typed this into TextEdit on a Mac runner. Second sentence."
- ✅ Paste-only field: the Accessibility write was refused, Fluent fell back to ⌘V; the app's own text is exactly "Pasted: and this arrived by paste."
- ✅ After the paste the transcript is also on the clipboard, to paste again anywhere
- ✅ Chrome textarea: inserted via **pasted** and read back exactly
- ✅ Bubble: shown next to the focused TextEdit document (centre at 721,458); see shots/bubble-textedit.png
- ✅ Clicking the bubble opened the recording capsule; see shots/capsule-after-click.png
- ✅ TextEdit stayed the active app when the bubble was clicked (the bubble does not steal focus)
- ✅ Screen **terms** (aurora) rendered; its Accessibility tree contains "Welcome to Fluent"
- ✅ Screen **onboarding** (aurora) rendered; its Accessibility tree contains "Set up Fluent"
- ✅ Screen **dictate** (aurora) rendered; its Accessibility tree contains "Ready when you are."
- ✅ Screen **history** (aurora) rendered; its Accessibility tree contains "History"
- ✅ Screen **style** (aurora) rendered; its Accessibility tree contains "Match my style"
- ✅ Screen **settings** (aurora) rendered; its Accessibility tree contains "Gemini API key"
- ✅ Screen **dictate** (porcelain) rendered; its Accessibility tree contains "Ready when you are."
- ✅ Screen **dictate** (obsidian) rendered; its Accessibility tree contains "Ready when you are."
- ✅ Screen **dictate** (ember) rendered; its Accessibility tree contains "Ready when you are."
- ✅ Screen **dictate** (lagoon) rendered; its Accessibility tree contains "Ready when you are."
- ✅ Screen **settings** (porcelain) rendered; its Accessibility tree contains "Gemini API key"
- ✅ Screen **capsule-listening** (aurora) rendered; its Accessibility tree contains "Listening"
- ✅ Screen **capsule-writing** (aurora) rendered; its Accessibility tree contains "Writing it up"
- ✅ Screen **capsule-inserted** (aurora) rendered; its Accessibility tree contains "Pasted"
- ✅ Screen **capsule-error** (aurora) rendered; its Accessibility tree contains "Add your Gemini API key in Settings first."
- ✅ Screen **capsule-listening** (ember) rendered; its Accessibility tree contains "Listening"
- ✅ Screen **capsule-listening** (lagoon) rendered; its Accessibility tree contains "Listening"
- ℹ️ `say` produced a 103784-byte 16 kHz WAV (3 s of speech)
- ✅ Mock Gemini server accepted the request from a `say` WAV ({"path": "/v1beta/interactions", "wav_bytes": 103784, "sample_rate": 16000, "transcription_config": {"language_codes": [], "mode": "smart"}, "problems": []}) and the transcript came back through the Messages (Personal, Casual) style: "Hey the mock server heard you"
- ℹ️ No GEMINI_API_KEY repository secret, so the real Gemini transcription test was skipped

**All end-to-end checks passed.**
