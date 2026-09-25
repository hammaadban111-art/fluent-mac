import AppKit
import FluentCore
import SwiftUI

/// `--demo <screen> [--theme id] [--snap-dir dir]`: CI opens one screen of the real app and saves
/// a picture of it, so every screen is checked for rendering and screenshotted. Settings live in a
/// throwaway defaults suite, so a demo never touches a real setup.
///
/// Screens: terms, onboarding, dictate, history, style, settings, capsule-listening,
/// capsule-writing, capsule-inserted, capsule-error, bubble (with `--at x,y` in AX coordinates).
@MainActor
enum Demo {
    static func run(_ screen: String, model: AppModel, overlays: OverlayController) {
        if let theme = LaunchOptions.current.theme { model.themeID = theme; model.overlayThemeID = "match" }
        switch screen {
        case "terms":
            break
        case "onboarding":
            model.acceptTerms()
        default:
            model.acceptTerms()
            model.setupComplete = true
            model.pretendReady()
        }
        switch screen {
        case "history":
            model.historyEnabled = true
            model.seedDemoHistory()
            model.tab = .history
        case "dictate":
            model.historyEnabled = true
            model.seedDemoHistory()
            model.tab = .dictate
        case "style": model.tab = .style
        case "settings": model.tab = .settings
        default: break
        }

        if screen.hasPrefix("capsule-") {
            overlays.pinned = true
            let phase: DictationController.Phase = switch screen {
            case "capsule-writing": .transcribing
            case "capsule-inserted": .done("Inserted")
            case "capsule-error": .error(TranscriptionError.noApiKey.userMessage)
            default: .recording
            }
            model.dictation.showDemo(phase)
            overlays.showCapsule()
            snapAfterDelay(screen) { NSApp.windows.first { $0 is OverlayPanel && $0.isVisible }?.contentView }
            return
        }
        if screen == "bubble" {
            overlays.pinned = true
            overlays.showBubble(for: nil, atAX: LaunchOptions.current.at ?? CGPoint(x: 600, y: 400))
            snapAfterDelay(screen) { NSApp.windows.first { $0 is OverlayPanel && $0.isVisible }?.contentView }
            return
        }
        AppDelegate.showMainWindow()
        snapAfterDelay(screen) { AppDelegate.window?.contentView }
    }

    /// Waits for SwiftUI to settle, saves `<screen>.png` of the view itself (no screen-recording
    /// permission needed) and writes `<screen>.ready` so the CI script can take its own full-screen
    /// capture too.
    private static func snapAfterDelay(_ screen: String, view: @escaping () -> NSView?) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.5) {
            MainActor.assumeIsolated {
                guard let dir = LaunchOptions.current.snapDir else { return }
                try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                // Drawn at 2x whatever the screen is, so the pictures stay sharp in the reel.
                if let v = view(), let rep = NSBitmapImageRep(
                    bitmapDataPlanes: nil, pixelsWide: Int(v.bounds.width * 2), pixelsHigh: Int(v.bounds.height * 2),
                    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) {
                    rep.size = v.bounds.size
                    v.cacheDisplay(in: v.bounds, to: rep)
                    try? rep.representation(using: .png, properties: [:])?
                        .write(to: dir.appendingPathComponent("app-\(screen).png"))
                }
                try? Data(screen.utf8).write(to: dir.appendingPathComponent("\(screen).ready"))
            }
        }
    }
}
