import AppKit
import ApplicationServices
import FluentMacKit

/// Headless checks run by CI against real apps on the runner, using the app's own code:
///
///   Fluent --self-test probe  --out r.json            what Fluent sees in the frontmost app
///   Fluent --self-test insert --text "…" --out r.json  inserts at the caret of the focused field
///   Fluent --self-test read   --out r.json            reads the focused field back (fresh process)
///   Fluent --self-test windows --out r.json           Fluent's on-screen windows (bubble, capsule)
///   Fluent --self-test click --at x,y --out r.json     clicks a screen point (the bubble)
///   Fluent --self-test key --key s --out r.json        presses ⌘<key> (⌘S saves the TextEdit file)
///   Fluent --self-test dump --pid N --out r.json       every text in app N's windows (UI checks)
///
/// Each writes a JSON report and exits 0; the CI script decides pass or fail from the report.
enum SelfTest {
    static func run(_ args: [String]) -> Never {
        let mode = args.first ?? "probe"
        func value(_ name: String) -> String? {
            guard let i = args.firstIndex(of: name), i + 1 < args.count else { return nil }
            return args[i + 1]
        }
        let out = value("--out").map { URL(fileURLWithPath: $0) }
        let app = NSApplication.shared
        app.setActivationPolicy(.prohibited)

        Task { @MainActor in
            var report: [String: Any] = [
                "mode": mode,
                "trusted": AXIsProcessTrusted(),
                "frontmost": NSWorkspace.shared.frontmostApplication?.bundleIdentifier ?? "",
            ]
            try? await Task.sleep(nanoseconds: 400_000_000)
            // Chromium and Electron build their tree only after Fluent asks (the first lookup sends
            // the nudge), so look again a few times, as the running app's 4-per-second watcher would.
            var field = FieldFinder.frontmost()
            var attempts = 1
            while field?.kind != .editable && attempts < 8 && mode != "windows" && mode != "dump" && mode != "click" {
                try? await Task.sleep(nanoseconds: 500_000_000)
                field = FieldFinder.frontmost()
                attempts += 1
            }
            report["lookups"] = attempts
            if let app = NSWorkspace.shared.frontmostApplication {
                let el = AXUIElementCreateApplication(app.processIdentifier)
                report["appFocusedRole"] = el.element("AXFocusedUIElement")?.string("AXRole") ?? NSNull()
                report["focusedWindowRole"] = el.element("AXFocusedWindow")?.string("AXRole") ?? NSNull()
                report["systemFocusedRole"] = AXUIElement.systemWide.element("AXFocusedUIElement")?.string("AXRole") ?? NSNull()
            }
            report["field"] = describe(field)

            switch mode {
            case "insert":
                let text = value("--text") ?? "Hello from Fluent"
                var log: [String] = []
                TextInserter.log = { log.append($0) }
                let before = field?.element.string("AXValue")
                report["valueBefore"] = before ?? NSNull()
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString("clipboard-sentinel", forType: .string)
                let outcome = await TextInserter.insert(text, fallback: field)
                switch outcome {
                case .accessibility(let piece): report["outcome"] = "accessibility"; report["piece"] = piece
                case .pasted(let piece): report["outcome"] = "pasted"; report["piece"] = piece
                case .failed(let why): report["outcome"] = "failed"; report["reason"] = why
                }
                try? await Task.sleep(nanoseconds: 1_600_000_000)   // let the clipboard restore run
                report["valueAfter"] = FieldFinder.frontmost()?.element.string("AXValue") ?? NSNull()
                report["clipboardAfter"] = NSPasteboard.general.string(forType: .string) ?? NSNull()
                report["log"] = log
            case "click":
                let parts = (value("--at") ?? "0,0").split(separator: ",").compactMap { Double($0) }
                let point = CGPoint(x: parts.first ?? 0, y: parts.count > 1 ? parts[1] : 0)
                for type in [CGEventType.mouseMoved, .leftMouseDown, .leftMouseUp] {
                    CGEvent(mouseEventSource: nil, mouseType: type, mouseCursorPosition: point, mouseButton: .left)?
                        .post(tap: .cghidEventTap)
                    try? await Task.sleep(nanoseconds: 120_000_000)
                }
                report["clicked"] = [point.x, point.y]
            case "key":
                report["sent"] = TextInserter.sendCommand(key: value("--key") ?? "s", toPid: nil)
                try? await Task.sleep(nanoseconds: 800_000_000)
            case "dump":
                let pid = pid_t(value("--pid") ?? "") ?? 0
                let root = AXUIElementCreateApplication(pid)
                AXUIElementSetMessagingTimeout(root, 2)
                var texts: [String] = []
                var queue: [(AXUIElement, Int)] = [(root, 0)]
                var visited = 0
                while !queue.isEmpty && visited < 6000 {
                    let (node, depth) = queue.removeFirst()
                    visited += 1
                    for attr in ["AXTitle", "AXValue", "AXDescription", "AXLabel"] {
                        if let t = node.string(attr), !t.isEmpty, !texts.contains(t) { texts.append(t) }
                    }
                    if depth < 40 { queue.append(contentsOf: node.children.map { ($0, depth + 1) }) }
                }
                report["texts"] = texts
                report["visited"] = visited
            case "read":
                report["value"] = field?.element.string("AXValue") ?? NSNull()
            case "windows":
                let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
                report["windows"] = list.filter { ($0[kCGWindowOwnerName as String] as? String) == "Fluent" }.map { w -> [String: Any] in
                    let b = w[kCGWindowBounds as String] as? [String: Any] ?? [:]
                    return ["layer": w[kCGWindowLayer as String] ?? 0, "bounds": b]
                }
            default:
                break
            }

            let data = (try? JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys])) ?? Data()
            if let out { try? data.write(to: out) } else { FileHandle.standardOutput.write(data) }
            exit(0)
        }
        app.run()
        exit(0)
    }

    @MainActor
    private static func describe(_ f: FocusedField?) -> Any {
        guard let f else { return NSNull() }
        var d: [String: Any] = [
            "role": f.traits.role ?? "", "subrole": f.traits.subrole ?? "",
            "kind": "\(f.kind)", "bundleID": f.bundleID ?? "", "app": f.appName ?? "",
            "valueSettable": f.traits.valueSettable, "selectedTextSettable": f.traits.selectedTextSettable,
        ]
        if let r = f.frame { d["frame"] = [r.minX, r.minY, r.width, r.height] }
        return d
    }
}
