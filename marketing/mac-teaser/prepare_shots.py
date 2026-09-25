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


def window(screen):
    """The bounding box of the app window: everything brighter than the black desktop, between
    the menu bar and the Dock."""
    w, h = screen.size
    top, bottom = int(h * 0.035), int(h * 0.90)
    band = screen.crop((0, top, w, bottom)).convert("L").point(lambda v: 255 if v > 12 else 0)
    box = band.getbbox()
    if not box:
        return screen
    return screen.crop((box[0], box[1] + top, box[2], box[3] + top))


def save(im, name):
    im.save(os.path.join(OUT, name))
    print(f"{name}: {im.size[0]}x{im.size[1]}")


def main(ci):
    os.makedirs(OUT, exist_ok=True)
    demo = os.path.join(ci, "shots", "demo")

    # App screens: cut the Fluent window out of the real full-screen captures (the desktop behind
    # it is black on the runner, and the menu bar and Dock are skipped).
    for theme in ["aurora", "porcelain", "obsidian", "ember", "lagoon"]:
        p = os.path.join(ci, "shots", f"screen-dictate-{theme}.png")
        if os.path.exists(p):
            save(window(Image.open(p).convert("RGB")), f"dictate-{theme}.png")
    for screen in ["style", "settings", "terms", "onboarding", "history"]:
        p = os.path.join(ci, "shots", f"screen-{screen}-aurora.png")
        if os.path.exists(p):
            save(window(Image.open(p).convert("RGB")), f"{screen}.png")
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
            scale = 1   # runner displays are 1x: window bounds (points) equal screenshot pixels
            bx = (bubble["X"] + bubble["Width"] / 2) * scale
            by = (bubble["Y"] + bubble["Height"] / 2) * scale
            w, h = 380 * scale, 300 * scale
            left = max(0, min(screen.size[0] - w, bx - w * 0.72))
            top = max(0, min(screen.size[1] - h, by - h * 0.5))
            crop = screen.crop((int(left), int(top), int(left + w), int(top + h)))
            crop = crop.resize((int(crop.size[0] * 2000 / crop.size[0]), int(crop.size[1] * 2000 / crop.size[0])), Image.LANCZOS)
            save(crop, "bubble-crop.png")
            meta["bubble"] = {"x": (bx - left) / w, "y": (by - top) / h}

    # The inserted sentence in TextEdit, the Accessibility end-to-end test's own screenshot, cropped
    # around the text box Fluent reported (reports/probe-textedit.json).
    te = os.path.join(ci, "shots", "textedit-inserted.png")
    probe = os.path.join(ci, "reports", "probe-textedit.json")
    if os.path.exists(te) and os.path.exists(probe):
        screen = Image.open(te).convert("RGB")
        x, y, fw, fh = json.load(open(probe))["field"]["frame"]
        crop = screen.crop((int(x - 8), int(y - 32), int(x + 420), int(y + 70)))
        save(crop.resize((2000, int(crop.size[1] * 2000 / crop.size[0])), Image.LANCZOS), "textedit-crop.png")

    with open(os.path.join(OUT, "meta.js"), "w") as f:
        f.write(f"window.SHOT_META = {json.dumps(meta)};\n")


if __name__ == "__main__":
    main(sys.argv[1])
