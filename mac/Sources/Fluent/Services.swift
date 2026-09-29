import AppKit
import Carbon
import Foundation
import FluentMacKit
import Security
import ServiceManagement

/// The Gemini API key, kept in the login Keychain (never in UserDefaults or a file).
enum KeychainStore {
    private static let service = "com.hammaad.fluent.mac"
    private static let account = "gemini_api_key"

    private static var base: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service,
         kSecAttrAccount as String: account]
    }

    static func load() -> String? {
        var query = base
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    @discardableResult
    static func save(_ key: String) -> Bool {
        let data = Data(key.utf8)
        let status = SecItemUpdate(base as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if status == errSecItemNotFound {
            var add = base
            add[kSecValueData as String] = data
            add[kSecAttrLabel as String] = "Fluent – Gemini API key"
            return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
        }
        return status == errSecSuccess
    }

    static func delete() {
        SecItemDelete(base as CFDictionary)
    }
}

@MainActor
enum SystemSettings {
    static func open(_ pane: String) {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)") {
            NSWorkspace.shared.open(url)
        }
    }
    static func microphone() { open("Privacy_Microphone") }
    static func accessibility() { open("Privacy_Accessibility") }
}

enum LaunchAtLogin {
    static var enabled: Bool { SMAppService.mainApp.status == .enabled }

    /// Registers Fluent as a login item. Returns an error message when macOS refuses (for example
    /// when the app is not in an Applications folder).
    @discardableResult
    static func set(_ on: Bool) -> String? {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            return nil
        } catch {
            return error.localizedDescription
        }
    }
}

@MainActor
enum Sounds {
    static func play(_ name: String) {
        NSSound(named: NSSound.Name(name))?.play()
    }
}

/// Shortcuts with an ordinary key (⌃⌥ Space, F5…), through Carbon's `RegisterEventHotKey`: no
/// permission needed, the key is consumed, and both press and release arrive (no auto-repeat), so
/// they serve hold-to-talk as well as start/stop.
final class GlobalHotKeys {
    enum Slot: UInt32 { case hold = 1, toggle = 2 }

    private var refs: [Slot: EventHotKeyRef] = [:]
    private var handler: EventHandlerRef?
    /// Called on the main queue with the slot and whether it was pressed (false: released).
    var action: ((Slot, Bool) -> Void)?

    /// Registers `combo` (it must have an ordinary key) for `slot`. False when another app owns it.
    @discardableResult
    func register(_ slot: Slot, _ combo: KeyCombo) -> Bool {
        unregister(slot)
        guard let key = combo.key else { return true }
        installHandler()
        var ref: EventHotKeyRef?
        let id = EventHotKeyID(signature: OSType(0x464C_4E54), id: slot.rawValue)   // 'FLNT'
        let status = RegisterEventHotKey(UInt32(key), combo.carbonModifiers, id, GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let ref else { return false }
        refs[slot] = ref
        return true
    }

    func unregister(_ slot: Slot) {
        if let ref = refs.removeValue(forKey: slot) { UnregisterEventHotKey(ref) }
    }

    func unregisterAll() {
        unregister(.hold)
        unregister(.toggle)
    }

    private func installHandler() {
        guard handler == nil else { return }
        var specs = [EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed)),
                     EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyReleased))]
        let me = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(GetApplicationEventTarget(), { _, event, userData in
            guard let event, let userData else { return noErr }
            var id = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &id)
            guard let slot = Slot(rawValue: id.id) else { return noErr }
            let pressed = GetEventKind(event) == UInt32(kEventHotKeyPressed)
            let hotKeys = Unmanaged<GlobalHotKeys>.fromOpaque(userData).takeUnretainedValue()
            DispatchQueue.main.async { hotKeys.action?(slot, pressed) }
            return noErr
        }, 2, &specs, me, &handler)
    }
}
