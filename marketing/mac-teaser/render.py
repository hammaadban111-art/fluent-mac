"""Renders the Fluent for Mac teaser: teaser.html is animated by a deterministic render(t), each
frame is screenshotted with Playwright/Chromium, and ffmpeg encodes 1080x1920 30 fps H.264 + AAC.

  python3 render.py stills 0.5 3 6        # PNG stills at those seconds (for checking)
  python3 render.py video                  # Fluent-Mac-Teaser-NoMusic.mp4 and Fluent-Mac-Teaser-Music.mp4

Needs: playwright (Chromium), ffmpeg, and audio.py run first (sfx.wav, music.wav).
"""
import glob
import json
import os
import subprocess
import sys

from playwright.sync_api import sync_playwright

from timeline import BAR, BEAT, DURATION, FPS, SCENES

HERE = os.path.dirname(os.path.abspath(__file__))


def chromium():
    for pattern in ["/opt/pw-browsers/chromium-*/chrome-linux/chrome", "/opt/pw-browsers/chromium*/chrome"]:
        found = sorted(glob.glob(pattern))
        if found:
            return found[-1]
    return None


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "video"
    grid = {"beat": BEAT, "bar": BAR, "duration": DURATION, "scenes": SCENES}
    with sync_playwright() as p:
        exe = chromium()
        browser = p.chromium.launch(executable_path=exe, args=["--allow-file-access-from-files"]) if exe \
            else p.chromium.launch(args=["--allow-file-access-from-files"])
        page = browser.new_page(viewport={"width": 1080, "height": 1920}, device_scale_factor=1)
        page.goto("file://" + os.path.join(HERE, "teaser.html"))
        page.evaluate(f"setup({json.dumps(grid)})")
        page.evaluate("document.fonts.ready")
        page.wait_for_timeout(800)
        if mode == "stills":
            os.makedirs(os.path.join(HERE, "stills"), exist_ok=True)
            for t in [float(x) for x in sys.argv[2:]]:
                page.evaluate(f"render({t})")
                page.screenshot(path=os.path.join(HERE, "stills", f"still_{t:05.2f}.png"))
        else:
            silent = os.path.join(HERE, "video_silent.mp4")
            ff = subprocess.Popen(
                ["ffmpeg", "-loglevel", "error", "-y", "-f", "image2pipe", "-framerate", str(FPS), "-c:v", "png",
                 "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-profile:v", "high",
                 "-pix_fmt", "yuv420p", "-r", str(FPS), silent], stdin=subprocess.PIPE)
            frames = round(DURATION * FPS)
            for i in range(frames):
                page.evaluate(f"render({i / FPS})")
                ff.stdin.write(page.screenshot(type="png"))
                if i % 90 == 0:
                    print(f"frame {i}/{frames}", flush=True)
            ff.stdin.close()
            ff.wait()
            for audio, name in [("sfx.wav", "Fluent-Mac-Teaser-NoMusic.mp4"), ("music.wav", "Fluent-Mac-Teaser-Music.mp4")]:
                subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", silent, "-i", os.path.join(HERE, audio),
                                "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "256k",
                                "-ar", "48000", "-t", f"{DURATION:.3f}", "-movflags", "+faststart",
                                os.path.join(HERE, name)], check=True)
                print("wrote", name)
        browser.close()


if __name__ == "__main__":
    main()
