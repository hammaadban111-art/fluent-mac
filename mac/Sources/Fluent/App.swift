import AppKit
import FluentCore
import FluentMacKit
import SwiftUI

/// Options read from the command line before anything starts. `--demo <screen>` is only used by
/// CI to take screenshots; normal launches pass nothing.
struct LaunchOptions {
    var demo: String?
    var theme: String?
    var snapDir: URL?
    var at: CGPoint?

    init(_ args: [String]) {
        func value(_ name: String) -> String? {
            guard let i = args.firstIndex(of: name), i + 1 < args.count else { return nil }
            return args[i + 1]
        }
        demo = value("--demo")
        theme = value("--theme")
        snapDir = value("--snap-dir").map { URL(fileURLWithPath: $0) }
        if let s = value("--at")?.split(separator: ","), s.count == 2, let x = Double(s[0]), let y = Double(s[1]) {
            at = CGPoint(x: x, y: y)
        }
    }

    nonisolated(unsafe) static var current = LaunchOptions([])
}

struct FluentApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        MenuBarExtra {
            MenuBarContent().environment(AppDelegate.model)
        } label: {
            MenuBarIcon().environment(AppDelegate.model)
        }
    }
}

struct MenuBarIcon: View {
    @Environment(AppModel.self) private var model
    var body: some View {
        let phase = model.dictation.phase
        Image(systemName: phase.isLive ? "waveform.circle.fill" : model.snoozed ? "moon.zzz" : "waveform")
            .accessibilityLabel("Fluent")
    }
}

struct MenuBarContent: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let d = model.dictation
        Text(status)
        Divider()
        Button(d.phase.isLive ? "Stop and insert" : "Start dictation") {
            d.toggle(source: .menu, target: FieldFinder.frontmost())
        }
        .disabled(!model.termsAccepted)
        if d.phase.isLive {
            Button("Cancel dictation") { d.cancel() }
        }
        Text("Hold \(model.holdKey.label) · \(model.toggleShortcut.label)")
        Divider()
        if model.snoozed {
            Button("Resume Fluent") { model.resume() }
        } else {
            Menu("Snooze") {
                ForEach(SnoozeChoice.allCases, id: \.self) { c in
                    Button(c.label) { model.snooze(c) }
                }
            }
        }
        Toggle("Floating bubble", isOn: Binding(get: { model.bubbleEnabled }, set: { model.bubbleEnabled = $0 }))
        Divider()
        Button("Open Fluent…") { AppDelegate.showMainWindow() }
        Button("Settings…") { AppDelegate.showMainWindow(tab: .settings) }
            .keyboardShortcut(",")
        Divider()
        Button("Quit Fluent") { NSApp.terminate(nil) }
            .keyboardShortcut("q")
    }

    private var status: String {
        if !model.termsAccepted { return "Open Fluent to get started" }
        if !model.micGranted { return "Microphone permission needed" }
        if !model.axTrusted { return "Accessibility permission needed" }
        if !model.hasApiKey { return "Gemini API key needed" }
        if model.snoozed { return model.snoozeLabel }
        return "Fluent is ready"
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    static let model: AppModel = {
        let o = LaunchOptions.current
        guard let demo = o.demo else { return AppModel() }
        let suite = "com.hammaad.fluent.mac.demo"
        UserDefaults().removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        return AppModel(defaults: defaults, demo: demo)
    }()

    static var overlays: OverlayController?
    static var hotkeys: HotkeyManager?
    static var window: NSWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        let model = Self.model
        let overlays = OverlayController(model: model)
        Self.overlays = overlays
        if let demo = LaunchOptions.current.demo {
            Demo.run(demo, model: model, overlays: overlays)
            return
        }
        overlays.start()
        let hotkeys = HotkeyManager(model: model)
        hotkeys.fieldProvider = { [weak overlays] in
            let front = NSWorkspace.shared.frontmostApplication
            if front?.bundleIdentifier == Bundle.main.bundleIdentifier { return nil }
            return FieldFinder.frontmost() ?? overlays?.currentField
        }
        hotkeys.install()
        Self.hotkeys = hotkeys
        Self.showMainWindow()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        Self.showMainWindow()
        return true
    }

    static func showMainWindow(tab: AppModel.Tab? = nil) {
        if let tab { model.tab = tab }
        if window == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 980, height: 700),
                             styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                             backing: .buffered, defer: false)
            w.title = "Fluent"
            w.titlebarAppearsTransparent = true
            w.titleVisibility = .hidden
            w.isReleasedWhenClosed = false
            w.minSize = NSSize(width: 860, height: 620)
            w.contentView = NSHostingView(rootView: RootView().environment(model))
            w.center()
            w.setFrameAutosaveName("FluentMainWindow")
            window = w
        }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate()
    }
}
