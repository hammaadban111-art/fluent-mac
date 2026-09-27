"""No visible text above y=250 or below y=1520 (Instagram covers those), every 5th frame.
  python3 check_safe_zones.py reel|tutorial"""
import json, os, sys
from playwright.sync_api import sync_playwright
from render import HERE, sizes
from timeline import FPS, grid

JS = """() => {
  const out = [];
  for (const el of document.querySelectorAll('#stage *')) {
    if (![...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) continue;
    let v = el, vis = true;
    while (v && v.id !== 'stage') { const cs = getComputedStyle(v); if (cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0 || cs.display === 'none') { vis = false; break; } v = v.parentElement; }
    if (!vis || el.closest('#view') || el.closest('.shot')) continue;
    const r = el.getBoundingClientRect();
    if (r.width && r.height) out.push({text: el.textContent.trim().slice(0, 30), top: r.top, bottom: r.bottom});
  }
  return out;
}"""
video = sys.argv[1]
g = grid(video)
with sync_playwright() as p:
    b = p.chromium.launch(args=["--allow-file-access-from-files"])
    pg = b.new_page(viewport={"width": 1080, "height": 1920})
    pg.goto("file://" + os.path.join(HERE, f"{video}.html"))
    pg.evaluate(f"setup({json.dumps(g)}, {json.dumps(sizes())})")
    pg.wait_for_timeout(500)
    bad, top, bottom = [], 1e9, 0
    for i in range(0, round(g["duration"] * FPS), 5):
        pg.evaluate(f"render({i / FPS})")
        for r in pg.evaluate(JS):
            top, bottom = min(top, r["top"]), max(bottom, r["bottom"])
            if r["top"] < 250 or r["bottom"] > 1520:
                bad.append((round(i / FPS, 2), r))
    b.close()
print(f"{video}: highest text {top:.0f}px, lowest {bottom:.0f}px, violations {len(bad)}")
for t, r in bad[:8]:
    print(" ", t, r)
