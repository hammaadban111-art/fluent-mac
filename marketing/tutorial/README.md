# "How to install Fluent for Windows" (vertical video)

38.2 s, 1080×1920, 30 fps, 18 bars at **113 BPM** — the tempo of "Can't Stop the Feeling!" (Justin Timberlake).

Every screen is a real screenshot from a clean Windows machine installing from the live website
(`tests/Fluent.E2E/Capture.cs`, workflow `windows-tutorial.yml`): Edge's "isn't commonly downloaded"
warning and Keep anyway, the installer, Fluent's first run, and a dictation into Notepad (stand-in
microphone and Gemini, since the build machine has neither). Not captured: Windows' own blue
SmartScreen screen, which the build machine never showed, so the video only mentions it as a tip.

    python3 music.py                      # tutorial_sfx.wav + tutorial_music.wav (original 113 BPM track)
    python3 render.py tutorial stills 5 12   # preview frames
    python3 render.py tutorial video      # Fluent-Windows-Install-Guide-{NoMusic,Music}.mp4
    python3 check_safe_zones.py tutorial  # no text under Instagram's UI

Post **NoMusic** and add the song in Instagram's music picker; **Music** has the original track instead.
