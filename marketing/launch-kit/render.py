"""Renders the Fluent for Mac launch videos from reel.html / tutorial.html (each a deterministic
render(t)): Playwright screenshots every frame, ffmpeg encodes 1080x1920 30 fps H.264, then the
soundtracks from music.py are muxed in.

  python3 render.py reel|tutorial stills 1.0 5.5 ...   PNG stills (stills/<video>_<t>.png)
  python3 render.py reel|tutorial video                  <video> MP4s, with and without music
"""
import json, os, subprocess, sys
from PIL import Image
from playwright.sync_api import sync_playwright
from timeline import FPS, grid

HERE = os.path.dirname(os.path.abspath(__file__))
NAMES = {"reel": "Fluent-Mac-OutNow", "tutorial": "Fluent-Mac-Install-Guide"}


def sizes():
    d = os.path.join(HERE, "shots")
    return {f[:-4]: list(Image.open(os.path.join(d, f)).size) for f in sorted(os.listdir(d)) if f.endswith(".png")}


def main():
    video, mode = sys.argv[1], sys.argv[2]
    g = grid(video)
    with sync_playwright() as p:
        browser = p.chromium.launch(args=["--allow-file-access-from-files"])
        page = browser.new_page(viewport={"width": 1080, "height": 1920}, device_scale_factor=1)
        page.goto("file://" + os.path.join(HERE, f"{video}.html"))
        page.evaluate(f"setup({json.dumps(g)}, {json.dumps(sizes())})")
        page.evaluate("document.fonts.ready")
        page.evaluate("Promise.all([...document.images].map(i => i.decode().catch(() => 0)))")
        page.wait_for_timeout(800)
        if mode == "stills":
            os.makedirs(os.path.join(HERE, "stills"), exist_ok=True)
            for t in [float(x) for x in sys.argv[3:]]:
                page.evaluate(f"render({t})")
                page.screenshot(path=os.path.join(HERE, "stills", f"{video}_{t:05.2f}.png"))
            browser.close()
            return
        silent = os.path.join(HERE, f"{video}_silent.mp4")
        ff = subprocess.Popen(["ffmpeg", "-loglevel", "error", "-y", "-f", "image2pipe", "-framerate", str(FPS),
                               "-c:v", "png", "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "18",
                               "-profile:v", "high", "-pix_fmt", "yuv420p", "-r", str(FPS), silent], stdin=subprocess.PIPE)
        frames = round(g["duration"] * FPS)
        for i in range(frames):
            page.evaluate(f"render({i / FPS})")
            ff.stdin.write(page.screenshot(type="png"))
            if i % 150 == 0:
                print(f"{video} frame {i}/{frames}", flush=True)
        ff.stdin.close(); ff.wait()
        browser.close()
    for audio, suffix in [(f"{video}_sfx.wav", "NoMusic"), (f"{video}_music.wav", "Music")]:
        out = os.path.join(HERE, f"{NAMES[video]}-{suffix}.mp4")
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", silent, "-i", os.path.join(HERE, audio),
                        "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "256k", "-ar", "48000",
                        "-t", f"{g['duration']:.3f}", "-movflags", "+faststart", out], check=True)
        print("wrote", out)


if __name__ == "__main__":
    main()
