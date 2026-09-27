"""Beat grid and scene data for "How to install Fluent for Windows" (vertical, 18 bars = 38.2 s).

113 BPM, the tempo of "Can't Stop the Feeling!" (Justin Timberlake), so the cuts land on its beat when
it is added in Instagram's editor. music.py also writes an original 113 BPM track.

Every screen is a real screenshot from a clean Windows machine (tests/Fluent.E2E/Capture.cs): the
live website in Edge, Edge's download warning, the installer, Fluent's first run and a first
dictation into Notepad. Times are in beats (0.531 s) unless named otherwise. A frame is
[beat, shot, [cx, cy, zoom]]: from that beat the view shows `shot` centred on screen pixel (cx, cy)
at `zoom`. Clicks are [beat, x, y] in screen pixels of the shot showing at that beat.
"""

BPM = 113
BEAT = 60 / BPM          # 0.5310 s
BAR = 4 * BEAT           # 2.1239 s
FPS = 30


def b(bar, beat=0.0):
    return bar * BAR + beat * BEAT


# The Mac reel's names, kept so music.py imports unchanged; this project has no reel.
REEL, REEL_BARS = {}, 0

TUTORIAL_BARS = 18
STEPS = [
    {"id": "intro", "bar": 0, "len": 1},
    {"id": "download", "bar": 1, "len": 1.5, "n": 1, "title": "Download it",
     "hints": [[0, "fluent-voice-v2.vercel.app", "mono"]],
     "frames": [[0, "01-site-top", [960, 560, 0.52]], [1.2, "02-site-hover", [625, 560, 1.9]]],
     "clicks": [[3.4, 625, 592]]},
    {"id": "keep", "bar": 2.5, "len": 2.5, "n": 2, "title": "Keep the file",
     "hints": [[0, "Edge is careful with new apps"], [2.2, "Click ⋯ then Keep"], [4.6, "Arrow next to Delete → Keep anyway"]],
     "frames": [[0, "03b-edge-warning", [1600, 190, 2.6]], [1.0, "03c-edge-more-hover", [1600, 190, 2.6]], [2.2, "03d-edge-menu", [1680, 240, 2.3]],
                [4.6, "03f-edge-keep-dialog", [1600, 330, 2.1]], [6.4, "03g2-edge-arrow-menu", [1600, 340, 2.1]]],
     "clicks": [[1.9, 1747, 176], [4.1, 1784, 232], [6.1, 1737, 507], [8.4, 1717, 541]]},
    {"id": "open", "bar": 5, "len": 1.5, "n": 3, "title": "Open it",
     "hints": [[0, "Click Open file"], [3, "If Windows warns: More info → Run anyway"]],
     "frames": [[0, "05-edge-open-file", [1560, 180, 2.6]]],
     "clicks": [[2.2, 1486, 172]]},
    {"id": "install", "bar": 6.5, "len": 2, "n": 4, "title": "Install it",
     "hints": [[0, "Click Next"], [3.4, "Then Finish. No admin password."]],
     "frames": [[0, "10-setup-tasks", [960, 560, 1.5]], [3.4, "12-setup-finish", [960, 560, 1.5]]],
     "clicks": [[2.6, 1125, 721], [6.6, 1125, 721]]},
    {"id": "terms", "bar": 8.5, "len": 1.5, "n": 5, "title": "Accept the terms",
     "hints": [[0, "Tick the box, then Continue"]],
     "frames": [[0, "14-terms", [960, 650, 1.4]], [2.05, "15-terms-checked", [960, 700, 1.6]]],
     "clicks": [[1.9, 750, 719], [4.4, 960, 789]]},
    {"id": "key", "bar": 10, "len": 2, "n": 6, "title": "Add your free Gemini key",
     "hints": [[0, "Free from aistudio.google.com/apikey"], [2.4, "Paste it, then Save key"], [5.2, "Microphone ✓  Key ✓  Finish setup"]],
     "frames": [[0, "16-setup", [880, 470, 1.6]], [2.4, "17-setup-key-typed", [880, 500, 1.9]],
                [5.2, "18-setup-done", [960, 560, 1.25]]],
     "clicks": [[1.8, 811, 506], [4.6, 1012, 508], [7.2, 1190, 733]]},
    {"id": "use", "bar": 12, "len": 4, "title": "Now just talk",
     "hints": [[0, "Click into any text box, then the Fluent bubble"], [3.2, "Talk"], [7.2, "Click stop"],
               [9.2, "It's typed."], [12.4, "Or hold Right Ctrl and talk"]],
     "frames": [[0, "20-notepad-bubble", [1450, 820, 1.8]], [3.2, "22-capsule-listening", [960, 60, 2.0]],
                [5.2, "23-capsule-listening-2", [960, 60, 2.0]], [7.2, "24-capsule-stop-hover", [960, 60, 2.0]],
                [9.2, "26-notepad-inserted", [480, 262, 1.8]]],
     "clicks": [[2.6, 1597, 887], [8.4, 1120, 52]]},
    {"id": "outro", "bar": 16, "len": 2},
]


def grid(video):
    return {"beat": BEAT, "bar": BAR, "duration": TUTORIAL_BARS * BAR, "steps": STEPS}


if __name__ == "__main__":
    print(TUTORIAL_BARS * BAR)
