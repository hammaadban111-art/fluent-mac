"""Checks that no visible text in the reel goes above y=250 px or below y=1520 px (Instagram's
UI covers those areas), by measuring every text element at every 5th frame. (Horizontally, text
boxes briefly overshoot only while a caption pops in at 1.35x scale for 0.16 s.)"""
import json
from playwright.sync_api import sync_playwright
from render import chromium, HERE
from timeline import BAR, BEAT, DURATION, FPS, SCENES
import os

JS = """() => {
  const out = [];
  for (const el of document.querySelectorAll('#stage *')) {
    if (!el.childNodes.length || ![...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) continue;
    let v = el, visible = true;
    while (v && v.id !== 'stage') { const cs = getComputedStyle(v); if (cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0 || cs.display === 'none') { visible = false; break; } v = v.parentElement; }
    if (!visible) continue;
    const r = el.getBoundingClientRect();
    if (r.width && r.height) out.push({text: el.textContent.trim().slice(0, 40), top: r.top, bottom: r.bottom, left: r.left, right: r.right});
  }
  return out;
}"""

with sync_playwright() as p:
    b = p.chromium.launch(executable_path=chromium())
    pg = b.new_page(viewport={"width": 1080, "height": 1920})
    pg.goto("file://" + os.path.join(HERE, "teaser.html"))
    pg.evaluate(f"setup({json.dumps({'beat': BEAT, 'bar': BAR, 'duration': DURATION, 'scenes': SCENES})})")
    pg.wait_for_timeout(500)
    bad, worst_top, worst_bottom = [], 1e9, 0
    for i in range(0, round(DURATION * FPS), 5):
        t = i / FPS
        pg.evaluate(f"render({t})")
        for r in pg.evaluate(JS):
            worst_top, worst_bottom = min(worst_top, r["top"]), max(worst_bottom, r["bottom"])
            if r["top"] < 250 or r["bottom"] > 1520:
                bad.append((round(t, 2), r))
    b.close()
print(f"highest text top: {worst_top:.0f}px, lowest text bottom: {worst_bottom:.0f}px")
print("safe-zone violations (text above 250 px or below 1520 px):", len(bad))
for t, r in bad[:15]:
    print(t, r)
