"""Scrolls the window under (x, y) by posting mouse-wheel events: python3 scroll.py x y lines"""
import sys, time
import Quartz
x, y, lines = float(sys.argv[1]), float(sys.argv[2]), int(sys.argv[3])
Quartz.CGEventPost(Quartz.kCGHIDEventTap, Quartz.CGEventCreateMouseEvent(None, Quartz.kCGEventMouseMoved, (x, y), 0))
step = -10 if lines > 0 else 10
for _ in range(abs(lines) // 10 or 1):
    e = Quartz.CGEventCreateScrollWheelEvent(None, Quartz.kCGScrollEventUnitLine, 1, step)
    Quartz.CGEventSetLocation(e, (x, y))
    Quartz.CGEventPost(Quartz.kCGHIDEventTap, e)
    time.sleep(0.03)
