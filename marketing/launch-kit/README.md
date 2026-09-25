# Fluent for Mac: launch videos

Two vertical Instagram videos (1080×1920, 30 fps, H.264 + AAC), both on a **115 BPM** grid, in the
website's paper-and-ink look.

| Video | Length | Output |
| --- | --- | --- |
| "Out now" launch reel | 20.9 s (10 bars) | `Fluent-Mac-OutNow-Music.mp4` / `-NoMusic.mp4` |
| Install guide: download, Open Anyway, permissions, Gemini key, first dictation | 41.7 s (20 bars) | `Fluent-Mac-Install-Guide-Music.mp4` / `-NoMusic.mp4` |

| File | What it is |
| --- | --- |
| `timeline.py` | Beat grid, reel scene times, and every tutorial step (frames, camera, clicks, drag, typing) as data. |
| `reel.html`, `tutorial.html` | Each video as a deterministic `render(t)`; `lib.js` + `paper.css` are shared. |
| `render.py` | `python3 render.py reel|tutorial video` (or `stills 1.0 5.5`). |
| `music.py` | Original 115 BPM disco-funk track + sound effects → `<video>_music.wav`, `<video>_sfx.wav`. |
| `check_safe_zones.py` | No text above 250 px or below 1520 px (Instagram's UI). |
| `shots/` | Real screenshots: the live website, the DMG window, macOS's Gatekeeper/permission dialogs and Fluent's own setup screens, captured on a real Mac on 2026-09-25 (account name, username and other apps blurred), plus the CI shots of the bubble/capsule/TextEdit. |

Illustrated, not captured: the Accessibility switch turning on (the "on" switch is copied from another
row of the same screenshot), the pasted key (`AIzaSy•••`, a placeholder), the dragged icon and the cursor.

## Music
Post the **NoMusic** file and add **"Uptown Funk" (Mark Ronson ft. Bruno Mars, 115 BPM)** from
Instagram's music library, starting on a downbeat. The **Music** files carry the original track instead.
