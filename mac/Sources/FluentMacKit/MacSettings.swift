import FluentCore
import Foundation
#if canImport(CoreGraphics)
import CoreGraphics
#endif

/// Snooze choices, as Android's `SnoozeDuration`. `untilRestart` lasts until Fluent quits.
public enum SnoozeChoice: Int, CaseIterable, Sendable {
    case fifteen = 15, thirty = 30, hour = 60, untilRestart = -1

    public var label: String {
        switch self {
        case .fifteen: "15 minutes"
        case .thirty: "30 minutes"
        case .hour: "1 hour"
        case .untilRestart: "Until Fluent restarts"
        }
    }
}

/// Mac-only settings, next to FluentCore's `Settings` (mode, language, vocabulary, style, theme,
/// history, terms), in the same UserDefaults. Keys follow Android's `SettingsRepository`.
public struct MacSettings {
    public let defaults: UserDefaults

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    private enum Key {
        static let bubbleEnabled = "bubble_enabled"
        static let bubbleSize = "bubble_size"
        static let bubbleOpacity = "bubble_opacity"
        static let bubbleOffsetX = "bubble_offset_x"
        static let bubbleOffsetY = "bubble_offset_y"
        static let excluded = "excluded_bundle_ids"
        static let snoozeMinutes = "snooze_minutes"
        static let snoozeUntil = "snooze_until"
        static let holdKey = "hold_key"
        static let toggleShortcut = "toggle_shortcut"
        static let overlayTheme = "overlay_theme"
        static let sounds = "sounds"
        static let launchAtLogin = "launch_at_login"
    }

    public static let matchApp = "match"
    /// Snoozed "until restart": stored as this marker plus the launch it belongs to.
    static let untilRestart: Double = -1

    /// On by default on the Mac: the bubble is the point of the app once permissions are granted.
    public var bubbleEnabled: Bool {
        get { defaults.object(forKey: Key.bubbleEnabled) as? Bool ?? true }
        nonmutating set { defaults.set(newValue, forKey: Key.bubbleEnabled) }
    }

    /// Bubble diameter in points (Android: 44…76 dp, default 56). Macs sit closer, so 34…60.
    public var bubbleSize: Double {
        get { min(60, max(34, defaults.object(forKey: Key.bubbleSize) as? Double ?? 42)) }
        nonmutating set { defaults.set(min(60, max(34, newValue)), forKey: Key.bubbleSize) }
    }

    public var bubbleOpacity: Double {
        get { min(1, max(0.35, defaults.object(forKey: Key.bubbleOpacity) as? Double ?? 0.95)) }
        nonmutating set { defaults.set(min(1, max(0.35, newValue)), forKey: Key.bubbleOpacity) }
    }

    /// Where the user dragged the bubble, relative to its automatic spot next to the field.
    public var bubbleOffset: CGSize {
        get { CGSize(width: defaults.double(forKey: Key.bubbleOffsetX), height: defaults.double(forKey: Key.bubbleOffsetY)) }
        nonmutating set {
            defaults.set(Double(newValue.width), forKey: Key.bubbleOffsetX)
            defaults.set(Double(newValue.height), forKey: Key.bubbleOffsetY)
        }
    }

    /// Bundle ids where the bubble never shows (Android: excluded packages).
    public var excludedApps: [String] {
        get { defaults.stringArray(forKey: Key.excluded) ?? [] }
        nonmutating set { defaults.set(Array(Set(newValue)).sorted(), forKey: Key.excluded) }
    }

    public func isExcluded(_ bundleID: String?) -> Bool {
        guard let bundleID else { return false }
        return excludedApps.contains(bundleID)
    }

    public var snoozeChoice: SnoozeChoice {
        get { SnoozeChoice(rawValue: defaults.object(forKey: Key.snoozeMinutes) as? Int ?? 30) ?? .thirty }
        nonmutating set { defaults.set(newValue.rawValue, forKey: Key.snoozeMinutes) }
    }

    /// Seconds since 1970; 0 when not snoozed; -1 for "until restart".
    public var snoozeUntil: Double {
        get { defaults.double(forKey: Key.snoozeUntil) }
        nonmutating set { defaults.set(newValue, forKey: Key.snoozeUntil) }
    }

    public func snooze(_ choice: SnoozeChoice, now: Date = Date()) {
        snoozeUntil = choice == .untilRestart ? Self.untilRestart
            : now.addingTimeInterval(Double(choice.rawValue) * 60).timeIntervalSince1970
    }

    public func clearSnooze() { snoozeUntil = 0 }

    public func snoozeActive(now: Date = Date()) -> Bool {
        snoozeUntil == Self.untilRestart || snoozeUntil > now.timeIntervalSince1970
    }

    /// "Until restart" ends when Fluent starts again.
    public func clearRestartSnooze() {
        if snoozeUntil == Self.untilRestart { snoozeUntil = 0 }
    }

    public var holdKey: HoldKey {
        get { defaults.string(forKey: Key.holdKey).flatMap(HoldKey.init(rawValue:)) ?? .rightOption }
        nonmutating set { defaults.set(newValue.rawValue, forKey: Key.holdKey) }
    }

    public var toggleShortcut: ToggleShortcut {
        get { ToggleShortcut.byLabel(defaults.string(forKey: Key.toggleShortcut)) }
        nonmutating set { defaults.set(newValue.label, forKey: Key.toggleShortcut) }
    }

    /// Palette for the bubble and capsule; `matchApp` follows the app theme (Android 1.6b).
    public var overlayThemeID: String {
        get { defaults.string(forKey: Key.overlayTheme) ?? Self.matchApp }
        nonmutating set { defaults.set(newValue, forKey: Key.overlayTheme) }
    }

    /// Short start/stop sounds, the Mac's stand-in for Android's haptics.
    public var soundsEnabled: Bool {
        get { defaults.object(forKey: Key.sounds) as? Bool ?? true }
        nonmutating set { defaults.set(newValue, forKey: Key.sounds) }
    }

    public var launchAtLogin: Bool {
        get { defaults.bool(forKey: Key.launchAtLogin) }
        nonmutating set { defaults.set(newValue, forKey: Key.launchAtLogin) }
    }

    /// Whether the bubble may be shown for a field in `bundleID` right now (Android's
    /// `syncBubbleVisibility`, minus the overlay permission the Mac does not have).
    public func bubbleAllowed(termsAccepted: Bool, field: FieldKind?, bundleID: String?,
                              ownBundleID: String?, now: Date = Date()) -> Bool {
        bubbleEnabled && termsAccepted && !snoozeActive(now: now) && field == .editable
            && !isExcluded(bundleID) && (bundleID == nil || bundleID != ownBundleID)
    }
}
