import Foundation

/// What Fluent knows about one Accessibility element. Plain values, so the decision about whether
/// the bubble may appear is unit tested without a Mac.
public struct FieldTraits: Equatable, Sendable {
    public var role: String?
    public var subrole: String?
    /// AXValue can be written (`AXUIElementIsAttributeSettable`).
    public var valueSettable: Bool
    /// AXSelectedText can be written. Some web fields expose only this.
    public var selectedTextSettable: Bool
    /// AXFocused reads true (used when searching a window for the real field).
    public var focused: Bool
    /// AXEnabled; nil when the element does not say.
    public var enabled: Bool?

    public init(role: String?, subrole: String? = nil, valueSettable: Bool = false,
                selectedTextSettable: Bool = false, focused: Bool = false, enabled: Bool? = nil) {
        self.role = role
        self.subrole = subrole
        self.valueSettable = valueSettable
        self.selectedTextSettable = selectedTextSettable
        self.focused = focused
        self.enabled = enabled
    }
}

public enum FieldKind: Equatable, Sendable {
    /// An ordinary text box: the bubble may show and text can go in.
    case editable
    /// A password field. Fluent never shows, records or types here.
    case secure
    case notEditable
}

public enum FieldClassifier {
    public static let textRoles: Set<String> = ["AXTextField", "AXTextArea", "AXComboBox", "AXSearchField"]

    /// Controls whose AXValue may be settable but which are not places to type.
    public static let nonTextRoles: Set<String> = [
        "AXSlider", "AXCheckBox", "AXRadioButton", "AXPopUpButton", "AXMenuButton", "AXScrollBar",
        "AXIncrementor", "AXStepper", "AXColorWell", "AXDisclosureTriangle", "AXSplitter", "AXTabGroup",
        "AXLevelIndicator", "AXValueIndicator", "AXProgressIndicator", "AXDateField", "AXTimeField",
        "AXSwitch", "AXToggle", "AXButton", "AXMenuItem", "AXCell", "AXRow", "AXOutline", "AXTable",
        "AXList", "AXImage", "AXStaticText", "AXLink",
    ]

    public static func classify(_ t: FieldTraits) -> FieldKind {
        if t.role == "AXSecureTextField" || t.subrole == "AXSecureTextField" { return .secure }
        if t.enabled == false { return .notEditable }
        if let role = t.role, textRoles.contains(role) { return .editable }
        if let role = t.role, nonTextRoles.contains(role) { return .notEditable }
        // Web and Electron editors (contenteditable) come through as groups or web areas whose
        // value or selected text can be written.
        if t.valueSettable || t.selectedTextSettable { return .editable }
        return .notEditable
    }

    public static func isEditable(_ t: FieldTraits) -> Bool { classify(t) == .editable }
}

/// Which apps need a nudge before they show their Accessibility tree. Electron apps (Claude,
/// Slack, Discord, VS Code…) build it only when an assistive app sets `AXManualAccessibility`;
/// Chromium browsers want `AXEnhancedUserInterface`. The Android 1.8.2 fix for Claude is the
/// same lesson: some apps do not report their text box the normal way until asked.
public enum AccessibilityNudge {
    public static let chromiumBrowsers: Set<String> = [
        "com.google.Chrome", "com.google.Chrome.beta", "com.google.Chrome.dev", "com.google.Chrome.canary",
        "org.chromium.Chromium", "com.brave.Browser", "com.microsoft.edgemac", "com.vivaldi.Vivaldi",
        "com.operasoftware.Opera", "company.thebrowser.Browser", "company.thebrowser.dia", "ai.perplexity.comet",
    ]

    /// Setting `AXEnhancedUserInterface` makes some native apps animate windows oddly, so it is
    /// only sent to Chromium browsers. `AXManualAccessibility` is ignored by apps that do not know
    /// it, so every third-party app gets it.
    public static func attributes(for bundleID: String?) -> (manual: Bool, enhanced: Bool) {
        guard let id = bundleID, !id.isEmpty else { return (false, false) }
        if chromiumBrowsers.contains(id) { return (true, true) }
        if id.hasPrefix("com.apple.") { return (false, false) }
        return (true, false)
    }

    /// Nodes searched in the focused window when the reported focus is not a text box.
    public static let maxNodesSearched = 1_500
}
