import AppKit

// A test-only app for CI: one text box that refuses Accessibility writes, like some web and custom
// editors do. Fluent's insertion has to notice that nothing changed and fall back to ⌘V. Every
// change to the text is written to the file given as the first argument, so the test checks what
// really landed without going through Accessibility at all.

final class StubbornTextView: NSTextView {
    // Accessibility may read this view but never write to it.
    override func setAccessibilityValue(_ accessibilityValue: Any?) {}
    override func setAccessibilitySelectedText(_ accessibilitySelectedText: String?) {}
    override func isAccessibilitySelectorAllowed(_ selector: Selector) -> Bool {
        if selector == #selector(setAccessibilityValue(_:)) || selector == #selector(setAccessibilitySelectedText(_:)) {
            return false
        }
        return super.isAccessibilitySelectorAllowed(selector)
    }
}

final class HostDelegate: NSObject, NSApplicationDelegate, NSTextViewDelegate {
    let output: URL
    var window: NSWindow!
    var textView: StubbornTextView!

    init(output: URL) { self.output = output }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let menu = NSMenu()
        let appItem = NSMenuItem(); menu.addItem(appItem)
        let appMenu = NSMenu(); appItem.submenu = appMenu
        appMenu.addItem(withTitle: "Quit", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let editItem = NSMenuItem(); menu.addItem(editItem)
        let edit = NSMenu(title: "Edit"); editItem.submenu = edit
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        NSApp.mainMenu = menu

        window = NSWindow(contentRect: NSRect(x: 200, y: 300, width: 560, height: 260),
                          styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "Paste-only field"
        let scroll = NSScrollView(frame: window.contentView!.bounds.insetBy(dx: 16, dy: 16))
        scroll.autoresizingMask = [.width, .height]
        textView = StubbornTextView(frame: scroll.bounds)
        textView.isRichText = false
        textView.font = .systemFont(ofSize: 16)
        textView.string = "Pasted:"
        textView.delegate = self
        scroll.documentView = textView
        window.contentView!.addSubview(scroll)
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(textView)
        textView.setSelectedRange(NSRange(location: 7, length: 0))
        NSApp.activate(ignoringOtherApps: true)
        save()
    }

    func textDidChange(_ notification: Notification) { save() }

    func save() { try? Data(textView.string.utf8).write(to: output) }
}

let output = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "/tmp/paste-only.txt")
let app = NSApplication.shared
app.setActivationPolicy(.regular)
let hostDelegate = HostDelegate(output: output)
app.delegate = hostDelegate
app.run()
