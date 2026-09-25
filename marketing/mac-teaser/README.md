# Fluent for Mac — Instagram teaser

A 21-second vertical reel (1080×1920, 30 fps, H.264 + AAC) cut on the beat at **171 BPM**.

| File | What it is |
| --- | --- |
| `timeline.py` | The beat grid (171 BPM, 15 bars) and scene start times. Everything else reads it. |
| `teaser.html` | The edit: every scene, caption and animation as a deterministic `render(t)`. |
| `render.py` | Playwright screenshots each frame, ffmpeg encodes, then muxes both soundtracks. |
| `audio.py` | Synthesises the sound effects (`sfx.wav`) and an original 171 BPM track mixed with them (`music.wav`, about −14 LUFS). |
| `check_safe_zones.py` | Measures every visible text box every 5th frame: none may sit above 250 px or below 1520 px. |
| `prepare_shots.py` | Crops the real Mac screenshots from CI (`mac-ci-results` branch, `ci/shots/`) into `shots/`. |
| `styles.json` | The Slack / Mail / Notes / Messages examples, produced by Fluent's own `StyleFormatter`. |
| `shots/` | Real screenshots of Fluent for Mac taken on a macOS runner. No mocked-up Fluent UI. |

Rebuild:

```
python3 prepare_shots.py <folder with the CI shots>
python3 audio.py
python3 render.py video      # → Fluent-Mac-Teaser-NoMusic.mp4, Fluent-Mac-Teaser-Music.mp4
```

## Music

- **Song to add in Instagram:** "Blinding Lights" by The Weeknd (171 BPM).
- **Where to start it:** at **0:00**, the first beat of the song. The cuts sit on its beats and
  bars (a new scene every 1.40 s, captions on every 0.35 s beat). If Instagram's slider cannot sit
  exactly on 0:00, any start that is a whole number of bars later also stays on the beat
  (0:01.4, 0:02.8, 0:05.6, 0:11.2…).
- **Backups:** "Take On Me" by a-ha (169 BPM) and "As It Was" by Harry Styles (174 BPM). Both are
  within 2% of 171, so the cuts drift by less than a third of a second over the reel. To lock
  exactly, set `BPM` in `timeline.py` and re-render.

Instagram mutes reels that have a copyrighted song baked into the file. Post the **NoMusic**
version and add the song from Instagram's own music library, or post the **Music** version, which
uses an original track synthesised in `audio.py`.

Keep captions out of Instagram's UI: all text sits between y = 250 px and y = 1520 px.

## Where the videos are

catbox.moe now refuses anonymous uploads ("Invalid uploader", HTTP 412) and litterbox failed too,
so both MP4s are published as the `mac-teaser` GitHub Release of the public
`hammaadban111-art/fluent-mac` repository (`.github/workflows/mac-teaser.yml`): permanent links,
no login needed.
