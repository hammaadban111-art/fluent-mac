"""Beat grid and scene data for the two Fluent for Mac launch videos:

  reel      "Out now" launch reel, 10 bars (20.9 s)
  tutorial  install + setup guide, 20 bars (41.7 s)

Both run at 115 BPM, the tempo of "Uptown Funk" (Mark Ronson ft. Bruno Mars), so the cuts land on its
beat when it is added in Instagram's editor. music.py writes an original 115 BPM track too.
Times are in beats (0.52 s) unless named otherwise. render.py passes all of this to the HTML.
"""

BPM = 115
BEAT = 60 / BPM          # 0.5217 s
BAR = 4 * BEAT           # 2.0870 s
FPS = 30


def b(bar, beat=0.0):
    return bar * BAR + beat * BEAT


# ------------------------------------------------------------------ reel
REEL_BARS = 10
REEL = {
    "hook": b(0),        # "Mac people." / "It's out."
    "stamp": b(1),       # logo, "Fluent for Mac", OUT NOW stamp
    "proof": b(2),       # messy speech struck out by pen, then what Fluent typed
    "real": b(3, 2),     # the real app: bubble, capsule, typed into TextEdit
    "notes": b(5),       # sticky notes: emails, chats, docs, prompts...
    "get": b(6),         # the real DMG window: download, drag, talk
    "end": b(7, 2),      # ticket: OUT NOW, free, the website, link in bio
}

# ------------------------------------------------------------------ tutorial
# Each step: start bar, length in bars, title, hint(s) and a list of frames. A frame is
# [beat, shot, [cx, cy, zoom]]: from that beat the view shows `shot` centred on image pixel
# (cx, cy) at `zoom`. Clicks are [beat, x, y] in the pixels of the shot on screen at that beat.
TUTORIAL_BARS = 20
STEPS = [
    {"id": "intro", "bar": 0, "len": 1},
    {"id": "download", "bar": 1, "len": 1.5, "n": 1, "title": "Download it",
     "hints": [[0, "fluent-voice-v2.vercel.app", "mono"]],
     "frames": [[0, "site-hero", [1280, 800, 0.39]], [1.3, "site-hero", [520, 1440, 0.95]]],
     "clicks": [[3.4, 450, 1460]]},
    {"id": "drag", "bar": 2.5, "len": 1.5, "n": 2, "title": "Drag it into Applications",
     "hints": [[0, "Open the file you downloaded"]],
     "frames": [[0, "dmg", [386, 300, 1.25]]],
     "drag": {"at": 1.2, "dur": 2.6, "from": [221, 305], "to": [551, 305], "crop": [176, 262, 90, 90]}},
    {"id": "open", "bar": 4, "len": 1.5, "n": 3, "title": "Open Fluent",
     "hints": [[0, "This warning is normal. Click Done."]],
     "frames": [[0, "not-opened", [186, 175, 2.3]]],
     "clicks": [[3.6, 113, 242]]},
    {"id": "anyway", "bar": 5.5, "len": 2, "n": 4, "title": "Click Open Anyway",
     "hints": [[0, "System Settings → Privacy & Security"]],
     "frames": [[0, "privacy", [420, 369, 1.2]], [2.2, "privacy", [600, 350, 1.85]]],
     "clicks": [[5.2, 695, 340]]},
    {"id": "confirm", "bar": 7.5, "len": 1.5, "n": 5, "title": "Confirm it",
     "hints": [[0, "Open Anyway again"], [3, "Then your Mac password. Only once."]],
     "frames": [[0, "confirm", [186, 231, 1.9]], [3, "password", [186, 236, 1.9]]],
     "clicks": [[1.9, 186, 324], [5.1, 244, 368]]},
    {"id": "terms", "bar": 9, "len": 1.5, "n": 6, "title": "Accept the terms",
     "hints": [[0, "Tick the box, then Continue"]],
     "frames": [[0, "welcome", [546, 400, 0.98]], [2.05, "welcome-checked", [546, 400, 0.98]]],
     "clicks": [[1.9, 339, 564], [4.4, 545, 633]]},
    {"id": "mic", "bar": 10.5, "len": 1.5, "n": 7, "title": "Allow the microphone",
     "hints": [[0, "Allow microphone, then Allow"]],
     "frames": [[0, "setup", [540, 235, 1.55]], [1.55, "mic-prompt", [186, 189, 2.2]],
                [3.55, "setup-mic", [540, 235, 1.55]]],
     "clicks": [[1.35, 338, 265], [3.35, 245, 273]]},
    {"id": "access", "bar": 12, "len": 2, "n": 8, "title": "Turn on Accessibility",
     "hints": [[0, "So Fluent can type into any app"], [3.2, "Switch Fluent on"]],
     "frames": [[0, "setup-mic", [540, 350, 1.5]], [1.55, "ax-prompt", [264, 125, 1.75]],
                [3.1, "ax-off", [520, 440, 1.3]], [5.75, "ax-on", [520, 440, 1.3]]],
     "clicks": [[1.35, 351, 392], [2.9, 328, 175], [5.6, 730, 527]]},
    {"id": "key", "bar": 14, "len": 2, "n": 9, "title": "Add your free Gemini key",
     "hints": [[0, "Get a free key from Google AI Studio"], [3.2, "Paste it, then Save key"]],
     "frames": [[0, "setup-key", [540, 500, 1.55]]],
     "clicks": [[1.3, 728, 517], [2.9, 430, 517], [6.3, 638, 517]],
     "typing": {"at": 3.5, "dur": 0.12, "box": [273, 506, 318, 23], "text": "AIzaSy••••••••••••••••••••••••••"}},
    {"id": "use", "bar": 16, "len": 2.5, "title": "Now just talk",
     "hints": [[0, "Click the Fluent bubble in any text box"], [3, "Talk"], [6, "Click stop. It's typed."],
               [7.6, "Or hold Right Option ⌥ and talk"]],
     "frames": [[0, "bubble-crop", [1150, 700, 0.5]], [3, "capsule-listening", [464, 82, 1.05]],
                [6, "capsule-inserted", [464, 82, 1.05]], [7.2, "textedit-crop", [1000, 238, 0.5]]],
     "clicks": [[2.1, 1440, 757]]},
    {"id": "outro", "bar": 18.5, "len": 1.5},
]


def grid(video):
    if video == "reel":
        return {"beat": BEAT, "bar": BAR, "duration": REEL_BARS * BAR, "scenes": REEL}
    return {"beat": BEAT, "bar": BAR, "duration": TUTORIAL_BARS * BAR, "steps": STEPS}


if __name__ == "__main__":
    import json
    print(json.dumps(grid("tutorial"))[:400])
    print(REEL_BARS * BAR, TUTORIAL_BARS * BAR)
