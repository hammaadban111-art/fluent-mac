"""Lists on-screen windows, or captures one window by owner/title into a PNG (with its shadow).
  python3 winshot.py list
  python3 winshot.py <owner> [title-substring] out.png   -> prints the window's bounds x y w h
"""
import subprocess, sys
import Quartz

def windows():
    ws = Quartz.CGWindowListCopyWindowInfo(Quartz.kCGWindowListOptionOnScreenOnly | Quartz.kCGWindowListExcludeDesktopElements, Quartz.kCGNullWindowID)
    return [w for w in ws if w.get("kCGWindowLayer", 0) >= 0]

if sys.argv[1] == "list":
    for w in windows():
        b = w["kCGWindowBounds"]
        print(w["kCGWindowNumber"], w.get("kCGWindowLayer"), repr(w.get("kCGWindowOwnerName")), repr(w.get("kCGWindowName")), int(b["X"]), int(b["Y"]), int(b["Width"]), int(b["Height"]))
    sys.exit()
owner, out = sys.argv[1], sys.argv[-1]
title = sys.argv[2] if len(sys.argv) == 4 else None
for w in windows():
    if owner.lower() in (w.get("kCGWindowOwnerName") or "").lower() and (title is None or title.lower() in (w.get("kCGWindowName") or "").lower()):
        b = w["kCGWindowBounds"]
        if b["Height"] < 60:
            continue
        subprocess.run(["screencapture", "-x", "-l", str(w["kCGWindowNumber"]), out], check=True)
        print(int(b["X"]), int(b["Y"]), int(b["Width"]), int(b["Height"]))
        break
else:
    sys.exit("no such window")
