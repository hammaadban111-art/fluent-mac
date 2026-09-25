"""The shared beat grid for the Fluent for Mac teaser. Every cut, caption and sound effect is
placed on this grid, so the edit lands on the beat of any 171 BPM track started on its first beat
(see README.md for the song and where to start it in Instagram's editor)."""

BPM = 171
BEAT = 60 / BPM          # 0.3509 s
BAR = 4 * BEAT           # 1.4035 s
BARS = 15
DURATION = BARS * BAR    # 21.05 s
FPS = 30


def b(bar, beat=0.0):
    """Time in seconds of `beat` (0-based, may be fractional) within `bar` (0-based)."""
    return bar * BAR + beat * BEAT


# Scene starts (seconds). The HTML reads the same numbers through scenes.json.
SCENES = {
    "hook": b(0),          # "Still typing every message?" one word per beat
    "slow": b(1),          # a sentence typed painfully slowly
    "faster": b(2, 2),     # "There's a faster way."
    "logo": b(3),          # Fluent for Mac reveal (the drop)
    "click": b(4),         # 1. Click any text box: real bubble next to TextEdit
    "talk": b(5),          # 2. Just talk: real capsule, words appear per beat
    "writing": b(6, 2),    # "Writing it up…"
    "typed": b(7),         # 3. It's typed for you
    "hotkey": b(8),        # Hold ⌥ and talk
    "apps": b(9),          # Slack / Mail / Notes / Messages, one per two beats
    "style": b(11),        # real Style screen
    "themes": b(12),       # five real themes, one per beat
    "end": b(14),          # logo + "Fluent for Mac, coming soon"
}

if __name__ == "__main__":
    import json
    print(json.dumps({"bpm": BPM, "beat": BEAT, "bar": BAR, "duration": DURATION, "scenes": SCENES}, indent=1))
