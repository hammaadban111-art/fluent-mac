// CI only: switches the runner's virtual display to its largest mode (up to 2560 wide) so the
// screenshots used for the teaser are sharp. Prints every mode it saw.
import CoreGraphics

let display = CGMainDisplayID()
let options = [kCGDisplayShowDuplicateLowResolutionModes: kCFBooleanTrue] as CFDictionary
let modes = (CGDisplayCopyAllDisplayModes(display, options) as? [CGDisplayMode]) ?? []
print("current: \(CGDisplayPixelsWide(display))x\(CGDisplayPixelsHigh(display))")
for m in modes { print("mode \(m.width)x\(m.height) (\(m.pixelWidth)x\(m.pixelHeight) px)") }
let usable = modes.filter { $0.isUsableForDesktopGUI() && $0.width <= 2560 }
if let best = usable.max(by: { $0.width * $0.height < $1.width * $1.height }),
   best.width > CGDisplayPixelsWide(display) {
    let result = CGDisplaySetDisplayMode(display, best, nil)
    print("set \(best.width)x\(best.height): \(result == .success ? "ok" : "error \(result.rawValue)")")
} else {
    print("kept the current mode")
}
print("now: \(CGDisplayPixelsWide(display))x\(CGDisplayPixelsHigh(display))")
