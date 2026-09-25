import Foundation

/// The key held for push-to-talk. Values are macOS virtual key codes (Carbon `kVK_*`).
public enum HoldKey: String, CaseIterable, Codable, Sendable {
    case rightOption, fn, rightCommand, rightControl, off

    public var keyCode: UInt16? {
        switch self {
        case .rightOption: 61
        case .fn: 63
        case .rightCommand: 54
        case .rightControl: 62
        case .off: nil
        }
    }

    public var label: String {
        switch self {
        case .rightOption: "Right Option ⌥"
        case .fn: "Fn / Globe"
        case .rightCommand: "Right Command ⌘"
        case .rightControl: "Right Control ⌃"
        case .off: "Off"
        }
    }

    public static func forKeyCode(_ code: UInt16) -> HoldKey? {
        allCases.first { $0.keyCode == code }
    }
}

/// A press-to-start, press-again-to-stop shortcut. Carbon modifier masks, so it can be registered
/// with `RegisterEventHotKey`, which needs no permission at all.
public struct ToggleShortcut: Codable, Equatable, Hashable, Sendable, Identifiable {
    public var keyCode: UInt32
    public var carbonModifiers: UInt32
    public var label: String
    public var id: String { label }

    // Carbon modifier bits.
    public static let cmdKey: UInt32 = 1 << 8
    public static let shiftKey: UInt32 = 1 << 9
    public static let optionKey: UInt32 = 1 << 11
    public static let controlKey: UInt32 = 1 << 12

    public static let space: UInt32 = 49
    public static let d: UInt32 = 2

    public static let presets: [ToggleShortcut] = [
        ToggleShortcut(keyCode: space, carbonModifiers: controlKey | optionKey, label: "⌃⌥ Space"),
        ToggleShortcut(keyCode: space, carbonModifiers: optionKey, label: "⌥ Space"),
        ToggleShortcut(keyCode: d, carbonModifiers: controlKey | optionKey, label: "⌃⌥ D"),
        ToggleShortcut(keyCode: space, carbonModifiers: cmdKey | shiftKey, label: "⇧⌘ Space"),
    ]
    public static let off = ToggleShortcut(keyCode: 0, carbonModifiers: 0, label: "Off")
    public static let `default` = presets[0]

    public var isOff: Bool { self == .off }

    public static func byLabel(_ label: String?) -> ToggleShortcut {
        if label == off.label { return .off }
        return presets.first { $0.label == label } ?? .default
    }
}

/// Push-to-talk timing. A modifier tapped and released quickly, or used together with another
/// key (⌥E for an accent, ⌘C…), is ordinary typing and must never start a dictation.
public struct HoldGesture: Sendable {
    public static let holdDelay: TimeInterval = 0.28

    public enum Event: Sendable { case down(at: TimeInterval), otherKey, up(at: TimeInterval) }
    public enum Action: Equatable, Sendable { case none, armTimer, cancelTimer, stopAndInsert }

    public private(set) var pressedAt: TimeInterval?
    public private(set) var spoiled = false
    public private(set) var dictating = false

    public init() {}

    public mutating func handle(_ e: Event) -> Action {
        switch e {
        case .down(let t):
            pressedAt = t; spoiled = false; dictating = false
            return .armTimer
        case .otherKey:
            guard pressedAt != nil, !dictating else { return .none }
            spoiled = true
            return .cancelTimer
        case .up:
            defer { pressedAt = nil; spoiled = false; dictating = false }
            return dictating ? .stopAndInsert : .cancelTimer
        }
    }

    /// Called when the hold timer fires. True means start recording now.
    public mutating func timerFired() -> Bool {
        guard pressedAt != nil, !spoiled, !dictating else { return false }
        dictating = true
        return true
    }
}
