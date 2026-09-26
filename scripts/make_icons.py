"""Renders the one Fluent logo (website-v2/favicon.svg, never redrawn) into the Windows icon files.

  python3 scripts/make_icons.py      (needs playwright + Pillow; the outputs are committed)

Writes src/Fluent/Assets/Fluent.ico (16-256 px), FluentMark.png (512 px, for the tray and windows)
and installer/wizard images for Inno Setup.
"""
import io, pathlib
from playwright.sync_api import sync_playwright
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
SVG = (ROOT.parent / "website-v2" / "favicon.svg").read_text()
ASSETS = ROOT / "src" / "Fluent" / "Assets"

with sync_playwright() as p:
    b = p.chromium.launch()
    page = b.new_page(viewport={"width": 1024, "height": 1024})
    page.set_content(f"<html><body style='margin:0;background:transparent'>"
                     f"<div style='width:1024px;height:1024px'>{SVG.replace('<svg ', '<svg width=\"1024\" height=\"1024\" ')}</div></body></html>")
    png = page.screenshot(omit_background=True, clip={"x": 0, "y": 0, "width": 1024, "height": 1024})
    b.close()

big = Image.open(io.BytesIO(png)).convert("RGBA")
big.resize((512, 512), Image.LANCZOS).save(ASSETS / "FluentMark.png")
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
big.save(ASSETS / "Fluent.ico", sizes=[(s, s) for s in sizes])

# Inno Setup wizard images (BMP): the small one sits top-right on every page.
inst = ROOT / "installer"
small = Image.new("RGB", (138, 140), (255, 255, 255))
small.paste(big.resize((110, 110), Image.LANCZOS), (14, 15), big.resize((110, 110), Image.LANCZOS))
small.save(inst / "wizard-small.bmp")
side = Image.new("RGB", (410, 797), (8, 8, 14))
mark = big.resize((260, 260), Image.LANCZOS)
side.paste(mark, (75, 250), mark)
side.save(inst / "wizard-side.bmp")
print("icons written")
