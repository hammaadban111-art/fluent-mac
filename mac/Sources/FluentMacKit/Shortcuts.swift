import Foundation

/// A key or key combination the user picked in Settings (Wispr Flow style: press the keys to record
/// them). macOS virtual key codes (Carbon `kVK_*`), left and right modifiers kept apart. At most one
/// ordinary key; the rest are modifiers. Either modifiers only (Right Option, Fn, ⌃⌥…) or modifiers
/// plus one key (⌃⌥ Space, F5…).
///
/// Modifier-only combinations are watched through `flagsChanged` and match their exact sides. Ones with
/// an ordinary key go through Carbon `RegisterEventHotKey`, which consumes the key, needs no
/// permission and matches either side of each modifier. Fn cannot be part of those (Carbon has no Fn bit).
public struct KeyCombo: Codable, Equatable, Hashable, Sendable {
    public static let leftCommand: UInt16 = 55, rightCommand: UInt16 = 54
    public static let leftShift: UInt16 = 56, rightShift: UInt16 = 60
    public static let leftOption: UInt16 = 58, rightOption: UInt16 = 61
    public static let leftControl: UInt16 = 59, rightControl: UInt16 = 62
    public static let fn: UInt16 = 63, capsLock: UInt16 = 57, escape: UInt16 = 53, space: UInt16 = 49

    public enum Group: Int, Comparable, Sendable {
        case control, option, shift, command, fn
        public static func < (a: Group, b: Group) -> Bool { a.rawValue < b.rawValue }
    }

    /// Sorted, sided modifier codes.
    public let modifiers: [UInt16]
    /// The one ordinary key, if any.
    public let key: UInt16?

    public init(modifiers: [UInt16], key: UInt16?) {
        let k = key.flatMap { Self.isModifier($0) || $0 == Self.capsLock ? nil : $0 }
        var mods = modifiers.filter(Self.isModifier)
        if k != nil {
            // With an ordinary key either side counts (Carbon can't tell), so store the left one; no Fn.
            mods = mods.filter { $0 != Self.fn }.map(Self.left)
        }
        mods = Array(Set(mods))
        self.modifiers = mods.sorted { Self.order($0) < Self.order($1) }
        self.key = k
    }

    public static let off = KeyCombo(modifiers: [], key: nil)
    public static let defaultHold = KeyCombo(modifiers: [rightOption], key: nil)
    public static let defaultToggle = KeyCombo(modifiers: [leftControl, leftOption], key: space)

    public var isOff: Bool { modifiers.isEmpty && key == nil }
    public var modifierOnly: Bool { key == nil && !modifiers.isEmpty }
    public var groups: Set<Group> { Set(modifiers.map(Self.group)) }

    // MARK: matching

    /// Modifier-only: whether the whole combination is down, given the modifier keys held.
    public func isDown(_ held: Set<UInt16>) -> Bool {
        modifierOnly && modifiers.allSatisfy(held.contains)
    }

    public func isPart(_ code: UInt16) -> Bool { modifiers.contains(code) }

    /// Carbon modifier mask for `RegisterEventHotKey`.
    public var carbonModifiers: UInt32 {
        var m: UInt32 = 0
        for g in groups {
            switch g {
            case .command: m |= 1 << 8
            case .shift: m |= 1 << 9
            case .option: m |= 1 << 11
            case .control: m |= 1 << 12
            case .fn: break
            }
        }
        return m
    }

    public static func isModifier(_ code: UInt16) -> Bool {
        [leftCommand, rightCommand, leftShift, rightShift, leftOption, rightOption, leftControl, rightControl, fn].contains(code)
    }

    public static func group(_ code: UInt16) -> Group {
        switch code {
        case leftControl, rightControl: .control
        case leftOption, rightOption: .option
        case leftShift, rightShift: .shift
        case leftCommand, rightCommand: .command
        default: .fn
        }
    }

    static func left(_ code: UInt16) -> UInt16 {
        switch code {
        case rightCommand: leftCommand
        case rightShift: leftShift
        case rightOption: leftOption
        case rightControl: leftControl
        default: code
        }
    }

    static func order(_ code: UInt16) -> Int {
        group(code).rawValue * 2 + ([rightControl, rightOption, rightShift, rightCommand].contains(code) ? 1 : 0)
    }

    /// The device-dependent bit of `NSEvent.modifierFlags.rawValue` that says this modifier is down
    /// (IOLLEvent.h `NX_DEVICE*KEYMASK`); Fn uses the Function flag.
    public static func deviceBit(_ code: UInt16) -> UInt? {
        switch code {
        case leftControl: 0x1
        case leftShift: 0x2
        case rightShift: 0x4
        case leftCommand: 0x8
        case rightCommand: 0x10
        case leftOption: 0x20
        case rightOption: 0x40
        case rightControl: 0x2000
        case fn: 0x80_0000
        default: nil
        }
    }

    // MARK: text

    /// Stored form: codes joined with "+", or "off".
    public var code: String {
        isOff ? "off" : (modifiers + (key.map { [$0] } ?? [])).map(String.init).joined(separator: "+")
    }

    public static func parse(_ code: String?) -> KeyCombo? {
        guard let code, !code.isEmpty else { return nil }
        if code == "off" { return .off }
        var mods: [UInt16] = []
        var key: UInt16?
        for part in code.split(separator: "+") {
            guard let c = UInt16(part), c < 256 else { return nil }
            if isModifier(c) { mods.append(c) } else if key == nil { key = c } else { return nil }
        }
        let combo = KeyCombo(modifiers: mods, key: key)
        return combo.isOff ? nil : combo
    }

    /// "Right Option", "Fn", "⌃⌥ Space". `name` names ordinary keys (the app passes one that reads the
    /// current keyboard layout); the default knows US letters and the special keys.
    public func label(name: (UInt16) -> String = KeyCombo.keyName) -> String {
        if isOff { return "Off" }
        guard let key else { return modifiers.map(Self.modifierName).joined(separator: " + ") }
        let symbols = groups.sorted().map { g -> String in
            switch g {
            case .control: "⌃"
            case .option: "⌥"
            case .shift: "⇧"
            case .command: "⌘"
            case .fn: "fn"
            }
        }.joined()
        return symbols.isEmpty ? name(key) : "\(symbols) \(name(key))"
    }

    public var label: String { label() }

    static func modifierName(_ code: UInt16) -> String {
        switch code {
        case leftControl: "Left Control"
        case rightControl: "Right Control"
        case leftOption: "Left Option"
        case rightOption: "Right Option"
        case leftShift: "Left Shift"
        case rightShift: "Right Shift"
        case leftCommand: "Left Command"
        case rightCommand: "Right Command"
        default: "Fn"
        }
    }

    public static func keyName(_ code: UInt16) -> String {
        if let s = specialNames[code] { return s }
        if let s = usLetters[code] { return s }
        return "Key \(code)"
    }

    public static let specialNames: [UInt16: String] = [
        49: "Space", 36: "Return", 48: "Tab", 51: "Delete", 117: "Forward Delete", 53: "Esc",
        122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6", 98: "F7", 100: "F8", 101: "F9",
        109: "F10", 103: "F11", 111: "F12", 105: "F13", 107: "F14", 113: "F15", 106: "F16", 64: "F17",
        79: "F18", 80: "F19", 90: "F20",
        123: "←", 124: "→", 125: "↓", 126: "↑", 115: "Home", 119: "End", 116: "Page Up", 121: "Page Down",
        114: "Help", 71: "Clear", 76: "Keypad Enter", 65: "Keypad .", 67: "Keypad *", 69: "Keypad +",
        75: "Keypad /", 78: "Keypad -", 81: "Keypad =", 82: "Keypad 0", 83: "Keypad 1", 84: "Keypad 2",
        85: "Keypad 3", 86: "Keypad 4", 87: "Keypad 5", 88: "Keypad 6", 89: "Keypad 7", 91: "Keypad 8",
        92: "Keypad 9", 10: "§",
    ]

    static let usLetters: [UInt16: String] = [
        0: "A", 11: "B", 8: "C", 2: "D", 14: "E", 3: "F", 5: "G", 4: "H", 34: "I", 38: "J", 40: "K",
        37: "L", 46: "M", 45: "N", 31: "O", 35: "P", 12: "Q", 15: "R", 1: "S", 17: "T", 32: "U", 9: "V",
        13: "W", 7: "X", 16: "Y", 6: "Z", 29: "0", 18: "1", 19: "2", 20: "3", 21: "4", 23: "5", 22: "6",
        26: "7", 28: "8", 25: "9", 27: "-", 24: "=", 33: "[", 30: "]", 42: "\\", 41: ";", 39: "'",
        43: ",", 47: ".", 44: "/", 50: "`",
    ]

    /// Why this combination is a poor choice, or nil. It still works; Settings shows the note.
    public var warning: String? {
        if isOff { return nil }
        if let key, groups.isEmpty, Self.usLetters[key] != nil || [49, 36, 48, 51].contains(key) {
            return "\(Self.keyName(key)) on its own will stop typing in other apps while Fluent uses it."
        }
        if modifiers == [Self.fn] {
            return "Using Fn: set System Settings › Keyboard › “Press 🌐 key to” to “Do Nothing” so macOS does not open its own dictation or emoji picker."
        }
        return nil
    }

    // MARK: settings written by Fluent 1.0, before any key could be picked

    public static func fromLegacyHold(_ raw: String?) -> KeyCombo {
        switch raw {
        case "fn": KeyCombo(modifiers: [fn], key: nil)
        case "rightCommand": KeyCombo(modifiers: [rightCommand], key: nil)
        case "rightControl": KeyCombo(modifiers: [rightControl], key: nil)
        case "off": .off
        default: .defaultHold
        }
    }

    public static func fromLegacyToggle(_ label: String?) -> KeyCombo {
        switch label {
        case "⌥ Space": KeyCombo(modifiers: [leftOption], key: space)
        case "⌃⌥ D": KeyCombo(modifiers: [leftControl, leftOption], key: 2)
        case "⇧⌘ Space": KeyCombo(modifiers: [leftShift, leftCommand], key: space)
        case "Off": .off
        default: .defaultToggle
        }
    }
}

/// Turns the keys pressed in Settings into a `KeyCombo`. Modifiers are collected while held; the first
/// ordinary key completes the combination, or, with modifiers only, letting go of all of them does.
/// Esc on its own cancels. Caps Lock is ignored (macOS reports it as a lock, not a key).
public struct KeyComboRecorder: Sendable {
    public enum Result: Equatable, Sendable { case `continue`, done(KeyCombo), cancelled }

    private var down: Set<UInt16> = []
    private var peak: Set<UInt16> = []

    public init() {}

    public mutating func modifier(_ code: UInt16, isDown: Bool) -> Result {
        guard KeyCombo.isModifier(code) else { return .continue }
        if isDown {
            down.insert(code); peak.insert(code)
            return .continue
        }
        down.remove(code)
        if down.isEmpty, !peak.isEmpty { return .done(KeyCombo(modifiers: Array(peak), key: nil)) }
        return .continue
    }

    public mutating func key(_ code: UInt16) -> Result {
        if code == KeyCombo.escape, down.isEmpty { return .cancelled }
        if code == KeyCombo.capsLock { return .continue }
        return .done(KeyCombo(modifiers: Array(down), key: code))
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

/// Everything the Mac hotkey code decides, with no AppKit so it can be tested: hold-to-talk on `hold`,
/// start/stop on `toggle`, Esc to cancel. Fed three kinds of events: modifier changes (`flagsChanged`),
/// other key presses (the key monitors), and the Carbon hot keys for combinations with an ordinary key.
///
/// A modifier-only start/stop fires on a quick clean tap. When hold and start/stop are the same
/// modifiers, a tap starts or stops and a hold talks.
public struct HotkeyEngine: Sendable {
    public enum Signal: Equatable, Sendable { case armHoldTimer, cancelHoldTimer, stopHold, toggle, cancel }
    public enum HotKey: Sendable { case hold, toggle }

    public static let tapWindow: TimeInterval = 0.5

    public var hold: KeyCombo
    public var toggle: KeyCombo
    public var active = true
    public private(set) var gesture = HoldGesture()
    public private(set) var held: Set<UInt16> = []
    private var tapStart: TimeInterval?
    private var tapClean = false

    public init(hold: KeyCombo = .defaultHold, toggle: KeyCombo = .defaultToggle) {
        self.hold = hold
        self.toggle = toggle
    }

    /// A modifier went down or up (`flagsChanged`).
    public mutating func modifier(_ code: UInt16, down: Bool, at t: TimeInterval) -> [Signal] {
        guard KeyCombo.isModifier(code) else { return [] }
        let wasHeld = held.contains(code)
        if down { held.insert(code) } else { held.remove(code) }
        if down == wasHeld { return [] }
        guard active else { return [] }
        var out: [Signal] = []

        if toggle.modifierOnly {
            if down && toggle.isPart(code) && toggle.isDown(held) && held.count == toggle.modifiers.count {
                tapStart = t; tapClean = true
            } else if down {
                tapClean = false
            }
        }

        if hold.modifierOnly {
            if hold.isPart(code) {
                let isDown = hold.isDown(held)
                // Nothing else may be held (⇧ then Right Option is a shortcut, not a hold).
                if isDown && held.count == hold.modifiers.count && down && gesture.pressedAt == nil {
                    add(gesture.handle(.down(at: t)), to: &out)
                } else if !isDown && gesture.pressedAt != nil {
                    releaseHold(at: t, into: &out)
                }
            } else if down && gesture.pressedAt != nil {
                add(gesture.handle(.otherKey), to: &out)   // ⌥⇧… is a shortcut, not a hold
            }
        }

        if !down, toggle.isPart(code) { releaseTap(at: t, into: &out) }
        return out
    }

    /// Any other key went down (seen by the key monitors; Carbon hot keys never get here).
    public mutating func otherKey(_ code: UInt16, dictationLive: Bool) -> [Signal] {
        tapClean = false
        guard active else { return [] }
        if code == KeyCombo.escape && dictationLive { return [.cancel] }
        var out: [Signal] = []
        if hold.modifierOnly, gesture.pressedAt != nil { add(gesture.handle(.otherKey), to: &out) }
        return out
    }

    /// A Carbon hot key (a combination with an ordinary key) was pressed or released.
    public mutating func hotKey(_ which: HotKey, pressed: Bool, at t: TimeInterval) -> [Signal] {
        guard active else { return [] }
        switch which {
        case .toggle:
            guard pressed else { return [] }
            var out: [Signal] = []
            // ⌃⌥ Space pressed while Right Option is the hold key: a shortcut, not a hold.
            if hold.modifierOnly, gesture.pressedAt != nil { add(gesture.handle(.otherKey), to: &out) }
            tapClean = false
            return out + [.toggle]
        case .hold:
            var out: [Signal] = []
            if pressed, gesture.pressedAt == nil { add(gesture.handle(.down(at: t)), to: &out) }
            else if !pressed, gesture.pressedAt != nil { releaseHold(at: t, into: &out) }
            return out
        }
    }

    /// The hold timer fired: true means start recording now.
    public mutating func holdTimerFired() -> Bool { active && gesture.timerFired() }

    /// Forget held keys, e.g. after recording a new combination.
    public mutating func reset() {
        held = []
        tapStart = nil
        if gesture.pressedAt != nil { _ = gesture.handle(.up(at: 0)) }
    }

    private mutating func releaseHold(at t: TimeInterval, into out: inout [Signal]) {
        let wasDictating = gesture.dictating
        add(gesture.handle(.up(at: t)), to: &out)
        if wasDictating { tapStart = nil }   // a hold that talked is not also a tap
    }

    private mutating func releaseTap(at t: TimeInterval, into out: inout [Signal]) {
        guard let start = tapStart else { return }
        tapStart = nil
        let window = toggle == hold ? HoldGesture.holdDelay : Self.tapWindow
        if tapClean && t - start <= window && !gesture.dictating { out.append(.toggle) }
    }

    private func add(_ a: HoldGesture.Action, to out: inout [Signal]) {
        switch a {
        case .armTimer: out.append(.armHoldTimer)
        case .cancelTimer: out.append(.cancelHoldTimer)
        case .stopAndInsert: out.append(.stopHold)
        case .none: break
        }
    }
}
