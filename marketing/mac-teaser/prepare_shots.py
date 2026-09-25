"""Turns the real CI screenshots (mac-ci-results branch, folder ci/) into the images the reel uses.

  python3 prepare_shots.py <path to a checkout of the ci/ folder>

Only crops, scales and trims: nothing is drawn onto Fluent's UI.
"""
import json
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "shots")


def trim(im):
    """Crops fully transparent borders (the capsule panel has a clear margin for its shadow)."""
    box = im.getchannel("A").getbbox() if im.mode == "RGBA" else None
    return im.crop(box) if box else im


def save(im, name):
    im.save(os.path.join(OUT, name))
    print(f"{name}: {im.size[0]}x{im.size[1]}")


def main(ci):
    os.makedirs(OUT, exist_ok=True)
    demo = os.path.join(ci, "shots", "demo")

    # App screens, drawn by the app itself at 2x.
    for theme in ["aurora", "porcelain", "obsidian", "ember", "lagoon"]:
        p = os.path.join(demo, f"app-dictate-{theme}.png")
        if os.path.exists(p):
            save(Image.open(p).convert("RGB"), f"dictate-{theme}.png")
    for screen in ["style", "settings", "terms", "onboarding", "history"]:
        p = os.path.join(demo, f"app-{screen}-aurora.png")
        if os.path.exists(p):
            save(Image.open(p).convert("RGB"), f"{screen}.png")
    for phase in ["listening", "writing", "inserted", "error"]:
        p = os.path.join(demo, f"app-capsule-{phase}-aurora.png")
        if os.path.exists(p):
            save(trim(Image.open(p).convert("RGBA")), f"capsule-{phase}.png")

    # The real bubble next to a real TextEdit document (full-screen capture on the runner).
    shot = os.path.join(ci, "shots", "bubble-textedit.png")
    report = os.path.join(ci, "reports", "windows-bubble.json")
    meta = {}
    if os.path.exists(shot) and os.path.exists(report):
        screen = Image.open(shot).convert("RGB")
        windows = json.load(open(report)).get("windows", [])
        bubble = next((w["bounds"] for w in windows if 30 <= w["bounds"]["Width"] <= 90), None)
        if bubble:
            scale = screen.size[0] / 1024 if screen.size[0] > 1100 else 1
            bx = (bubble["X"] + bubble["Width"] / 2) * scale
            by = (bubble["Y"] + bubble["Height"] / 2) * scale
            w, h = 560 * scale, 400 * scale
            left = max(0, min(screen.size[0] - w, bx - w * 0.72))
            top = max(0, min(screen.size[1] - h, by - h * 0.5))
            crop = screen.crop((int(left), int(top), int(left + w), int(top + h)))
            crop = crop.resize((int(crop.size[0] * 2000 / crop.size[0]), int(crop.size[1] * 2000 / crop.size[0])), Image.LANCZOS)
            save(crop, "bubble-crop.png")
            meta["bubble"] = {"x": (bx - left) / w, "y": (by - top) / h}

    # The inserted sentence in TextEdit, the Accessibility end-to-end test's own screenshot.
    te = os.path.join(ci, "shots", "textedit-inserted.png")
    if os.path.exists(te):
        screen = Image.open(te).convert("RGB")
        crop = screen.crop((70, 50, 760, 300))
        save(crop.resize((crop.size[0] * 2000 // crop.size[0], crop.size[1] * 2000 // crop.size[0]), Image.LANCZOS),
             "textedit-crop.png")

    with open(os.path.join(OUT, "meta.js"), "w") as f:
        f.write(f"window.SHOT_META = {json.dumps(meta)};\n")


if __name__ == "__main__":
    main(sys.argv[1])
