# Fluent for Mac

Voice dictation for macOS: click into any text box, click Fluent's bubble or hold Right Option,
speak, and the cleaned-up text (Google Gemini, `gemini-3.5-transcribe`) is typed at your cursor.

- `mac/` — the app (SwiftUI + AppKit, macOS 14+, Apple Silicon and Intel), the shared
  `FluentCore`, tests and scripts.
- `.github/workflows/mac-ci.yml` — builds and tests on a macOS runner; results are pushed to the
  `mac-ci-results` branch.
- `.github/workflows/mac-release.yml` — builds `Fluent-mac.zip`, uploads it and a one-line
  installer to catbox.moe, and tests the install command on a fresh Mac.
- `marketing/mac-teaser/` — the Instagram teaser reel sources.

No API keys live here: each user pastes their own free Gemini key from
https://aistudio.google.com/apikey. This mirrors the `mac` branch of the private `fluent-ios` repo.
