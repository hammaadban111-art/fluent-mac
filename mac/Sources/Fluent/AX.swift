import AppKit
import ApplicationServices
import FluentMacKit

/// Thin, failure-tolerant wrappers over the C Accessibility API.
extension AXUIElement {
    static let systemWide = AXUIElementCreateSystemWide()

    func attribute(_ name: String) -> CFTypeRef? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(self, name as CFString, &value) == .success else { return nil }
        return value
    }

    func string(_ name: String) -> String? { attribute(name) as? String }

    func bool(_ name: String) -> Bool? { (attribute(name) as? NSNumber)?.boolValue }

    func element(_ name: String) -> AXUIElement? {
        guard let v = attribute(name), CFGetTypeID(v) == AXUIElementGetTypeID() else { return nil }
        return unsafeBitCast(v, to: AXUIElement.self)
    }

    var children: [AXUIElement] {
        guard let list = attribute("AXChildren") as? [AnyObject] else { return [] }
        return list.compactMap { CFGetTypeID($0) == AXUIElementGetTypeID() ? unsafeBitCast($0, to: AXUIElement.self) : nil }
    }

    func range(_ name: String) -> CFRange? {
        guard let v = attribute(name), CFGetTypeID(v) == AXValueGetTypeID() else { return nil }
        var r = CFRange()
        return AXValueGetValue(unsafeBitCast(v, to: AXValue.self), .cfRange, &r) ? r : nil
    }

    var frame: CGRect? {
        guard let p = attribute("AXPosition"), CFGetTypeID(p) == AXValueGetTypeID(),
              let s = attribute("AXSize"), CFGetTypeID(s) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero, size = CGSize.zero
        guard AXValueGetValue(unsafeBitCast(p, to: AXValue.self), .cgPoint, &point),
              AXValueGetValue(unsafeBitCast(s, to: AXValue.self), .cgSize, &size) else { return nil }
        return CGRect(origin: point, size: size)
    }

    func isSettable(_ name: String) -> Bool {
        var settable: DarwinBoolean = false
        return AXUIElementIsAttributeSettable(self, name as CFString, &settable) == .success && settable.boolValue
    }

    @discardableResult
    func set(_ name: String, _ value: CFTypeRef) -> AXError {
        AXUIElementSetAttributeValue(self, name as CFString, value)
    }

    var pid: pid_t? {
        var p: pid_t = 0
        return AXUIElementGetPid(self, &p) == .success ? p : nil
    }

    var traits: FieldTraits {
        FieldTraits(role: string("AXRole"), subrole: string("AXSubrole"),
                    valueSettable: isSettable("AXValue"), selectedTextSettable: isSettable("AXSelectedText"),
                    focused: bool("AXFocused") ?? false, enabled: bool("AXEnabled"))
    }
}

enum AccessibilityAccess {
    static var trusted: Bool { AXIsProcessTrusted() }

    /// Shows the system "allow Fluent to control this computer" prompt once, and opens the pane.
    static func request() {
        _ = AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": true] as CFDictionary)
    }
}

/// The text box the user is in, as last seen.
struct FocusedField {
    var element: AXUIElement
    var traits: FieldTraits
    var kind: FieldKind
    /// Accessibility coordinates (top-left origin).
    var frame: CGRect?
    var pid: pid_t
    var bundleID: String?
    var appName: String?
}

/// Finds the focused text box of the frontmost app, the way Android's `safeFindFocus` does: ask
/// for the focused element, and when the answer is not editable (Electron and Chromium apps often
/// answer with a container) search the focused window for the element that really has focus.
@MainActor
enum FieldFinder {
    private static var nudged: Set<pid_t> = []

    static func frontmost() -> FocusedField? {
        guard let app = NSWorkspace.shared.frontmostApplication else { return nil }
        return focused(in: app)
    }

    static func focused(in app: NSRunningApplication) -> FocusedField? {
        let pid = app.processIdentifier
        let appElement = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(appElement, 0.35)
        nudge(appElement, pid: pid, bundleID: app.bundleIdentifier)

        let direct = appElement.element("AXFocusedUIElement") ?? systemFocus(for: pid)
        var element = direct
        var traits = direct?.traits
        if traits.map(FieldClassifier.classify) != .editable && traits.map(FieldClassifier.classify) != .secure {
            if let window = appElement.element("AXFocusedWindow"), let found = searchFocusedEditable(window) {
                element = found
                traits = found.traits
            }
        }
        guard let element, let traits else { return nil }
        return FocusedField(element: element, traits: traits, kind: FieldClassifier.classify(traits),
                            frame: element.frame, pid: pid, bundleID: app.bundleIdentifier,
                            appName: app.localizedName)
    }

    private static func systemFocus(for pid: pid_t) -> AXUIElement? {
        guard let el = AXUIElement.systemWide.element("AXFocusedUIElement"), el.pid == pid else { return nil }
        return el
    }

    /// Electron and Chromium build their Accessibility tree only when an assistive app asks.
    static func nudge(_ appElement: AXUIElement, pid: pid_t, bundleID: String?) {
        guard !nudged.contains(pid) else { return }
        nudged.insert(pid)
        let wanted = AccessibilityNudge.attributes(for: bundleID)
        if wanted.manual { appElement.set("AXManualAccessibility", kCFBooleanTrue) }
        if wanted.enhanced { appElement.set("AXEnhancedUserInterface", kCFBooleanTrue) }
    }

    static func searchFocusedEditable(_ root: AXUIElement) -> AXUIElement? {
        var queue = [root]
        var visited = 0
        while !queue.isEmpty && visited < AccessibilityNudge.maxNodesSearched {
            let node = queue.removeFirst()
            visited += 1
            if node.bool("AXFocused") == true {
                let kind = FieldClassifier.classify(node.traits)
                if kind == .editable || kind == .secure { return node }
            }
            queue.append(contentsOf: node.children)
        }
        return nil
    }
}
