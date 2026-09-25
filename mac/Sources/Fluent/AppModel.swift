import AppKit
import AVFoundation
import FluentCore
import FluentMacKit
import Observation
import SwiftUI

/// App-wide state for the UI. Settings are mirrored into observable properties and written
/// straight back to UserDefaults, like the iPhone app's `AppModel`.
@MainActor
@Observable
final class AppModel {
    enum Tab: String, CaseIterable, Identifiable {
        case dictate, history, style, settings
        var id: String { rawValue }
        var title: String { rawValue.capitalized }
        var symbol: String {
            switch self {
            case .dictate: "mic.fill"
            case .history: "clock"
            case .style: "textformat"
            case .settings: "gearshape"
            }
        }
    }

    let store: FluentCore.Settings
    let mac: MacSettings
    let historyStore: HistoryStore
    let dictation: DictationController
    /// Set when launched with `--demo`: nothing is saved to the real settings.
    let demo: String?

    var tab: Tab = .dictate
    var termsAccepted: Bool
    var setupComplete: Bool { didSet { store.setupComplete = setupComplete } }
    var themeID: String { didSet { store.themeID = themeID } }
    var mode: TranscriptionMode { didSet { store.mode = mode } }
    var languageCode: String { didSet { store.languageCode = languageCode } }
    var vocabulary: [String] { didSet { store.vocabulary = vocabulary } }
    var historyEnabled: Bool { didSet { store.historyEnabled = historyEnabled } }
    var styleEnabled: Bool { didSet { store.styleEnabled = styleEnabled } }
    private(set) var styles: [StyleCategory: WritingStyle] = [:]

    var bubbleEnabled: Bool { didSet { mac.bubbleEnabled = bubbleEnabled } }
    var bubbleSize: Double { didSet { mac.bubbleSize = bubbleSize } }
    var bubbleOpacity: Double { didSet { mac.bubbleOpacity = bubbleOpacity } }
    var excludedApps: [String] { didSet { mac.excludedApps = excludedApps } }
    var snoozeChoice: SnoozeChoice { didSet { mac.snoozeChoice = snoozeChoice } }
    private(set) var snoozeUntil: Double
    var holdKey: HoldKey { didSet { mac.holdKey = holdKey; onShortcutsChanged?() } }
    var toggleShortcut: ToggleShortcut { didSet { mac.toggleShortcut = toggleShortcut; onShortcutsChanged?() } }
    var overlayThemeID: String { didSet { mac.overlayThemeID = overlayThemeID } }
    var soundsEnabled: Bool { didSet { mac.soundsEnabled = soundsEnabled } }
    private(set) var launchAtLogin: Bool
    var launchAtLoginError: String?

    private(set) var hasApiKey: Bool
    private(set) var maskedKey = ""
    private(set) var history: [Transcript] = []

    /// Live permission state, re-read every second while Fluent runs.
    private(set) var micStatus: AVAuthorizationStatus = .notDetermined
    private(set) var axTrusted = false

    var onShortcutsChanged: (() -> Void)?

    var palette: Palette { Palette.byID(themeID) }
    var overlayPalette: Palette {
        overlayThemeID == MacSettings.matchApp ? palette : Palette.byID(overlayThemeID)
    }
    var micGranted: Bool { micStatus == .authorized }
    var snoozed: Bool { snoozeUntil == -1 || snoozeUntil > Date().timeIntervalSince1970 }
    var ready: Bool { micGranted && axTrusted && hasApiKey }

    init(defaults: UserDefaults = .standard, demo: String? = nil) {
        self.demo = demo
        store = FluentCore.Settings(defaults: defaults)
        mac = MacSettings(defaults: defaults)
        let support = demo == nil
            ? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Fluent")
            : FileManager.default.temporaryDirectory.appendingPathComponent("Fluent-demo-\(UUID().uuidString)")
        historyStore = HistoryStore(directory: support)
        mac.clearRestartSnooze()

        termsAccepted = store.termsAccepted
        setupComplete = store.setupComplete
        themeID = Palette.byID(store.themeID).id
        mode = store.mode
        languageCode = store.languageCode
        vocabulary = store.vocabulary
        historyEnabled = store.historyEnabled
        styleEnabled = store.styleEnabled
        bubbleEnabled = mac.bubbleEnabled
        bubbleSize = mac.bubbleSize
        bubbleOpacity = mac.bubbleOpacity
        excludedApps = mac.excludedApps
        snoozeChoice = mac.snoozeChoice
        snoozeUntil = mac.snoozeUntil
        holdKey = mac.holdKey
        toggleShortcut = mac.toggleShortcut
        overlayThemeID = mac.overlayThemeID
        soundsEnabled = mac.soundsEnabled
        launchAtLogin = demo == nil ? LaunchAtLogin.enabled : false
        let key = demo == nil ? KeychainStore.load() : nil
        hasApiKey = key?.isEmpty == false
        maskedKey = Self.mask(key)
        dictation = DictationController(settings: store)
        for c in StyleCategory.allCases { styles[c] = store.style(for: c) }
        history = historyStore.all()
        dictation.model = self
        refreshPermissions()
    }

    // MARK: permissions

    /// Demo screenshots of a set-up Mac: shows the app as it looks once permissions and a key
    /// are in place. Only `--demo` uses it; the CI runner has no microphone or key of its own.
    private var permissionsFrozen = false

    func pretendReady() {
        permissionsFrozen = true
        micStatus = .authorized
        axTrusted = true
        hasApiKey = true
        maskedKey = "AIza••••••••x9Qk"
    }

    func refreshPermissions() {
        if permissionsFrozen { return }
        micStatus = Recorder.permission
        axTrusted = AccessibilityAccess.trusted
        if snoozeUntil > 0, !snoozed { snoozeUntil = 0; mac.clearSnooze() }
    }

    func requestMicrophone() {
        if micStatus == .notDetermined {
            Task {
                _ = await Recorder.requestPermission()
                refreshPermissions()
            }
        } else {
            SystemSettings.microphone()
        }
    }

    func requestAccessibility() {
        AccessibilityAccess.request()
        SystemSettings.accessibility()
    }

    // MARK: terms, key

    func acceptTerms() {
        store.acceptTerms()
        termsAccepted = true
    }

    func saveApiKey(_ key: String) {
        let trimmed = key.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { KeychainStore.delete() } else { KeychainStore.save(trimmed) }
        hasApiKey = !trimmed.isEmpty
        maskedKey = Self.mask(trimmed)
    }

    func testApiKey() async -> Result<String, TranscriptionError> {
        await GeminiClient(apiKey: KeychainStore.load() ?? "").testConnection()
    }

    private static func mask(_ key: String?) -> String {
        guard let key, key.count > 8 else { return key == nil || key!.isEmpty ? "" : "••••" }
        return String(key.prefix(4)) + String(repeating: "•", count: 8) + String(key.suffix(4))
    }

    // MARK: style

    func style(for c: StyleCategory) -> WritingStyle { styles[c] ?? c.defaultStyle }

    func setStyle(_ s: WritingStyle, for c: StyleCategory) {
        store.setStyle(s, for: c)
        styles[c] = store.style(for: c)
    }

    // MARK: snooze, login

    func snooze(_ choice: SnoozeChoice? = nil) {
        mac.snooze(choice ?? snoozeChoice)
        snoozeUntil = mac.snoozeUntil
    }

    func resume() {
        mac.clearSnooze()
        snoozeUntil = 0
    }

    var snoozeLabel: String {
        if snoozeUntil == -1 { return "Snoozed until Fluent restarts" }
        let until = Date(timeIntervalSince1970: snoozeUntil)
        return "Snoozed until \(until.formatted(date: .omitted, time: .shortened))"
    }

    func setLaunchAtLogin(_ on: Bool) {
        launchAtLoginError = LaunchAtLogin.set(on)
        launchAtLogin = LaunchAtLogin.enabled
        mac.launchAtLogin = launchAtLogin
    }

    func exclude(_ bundleID: String) {
        if !excludedApps.contains(bundleID) { excludedApps.append(bundleID) }
    }

    // MARK: history

    func record(_ t: Transcript) {
        guard historyEnabled else { return }
        historyStore.add(t)
        history.insert(t, at: 0)
    }

    func delete(_ t: Transcript) {
        historyStore.delete(t.id)
        history.removeAll { $0.id == t.id }
    }

    func clearHistory() {
        historyStore.clear()
        history = []
    }

    var wordsToday: Int {
        history.filter { Calendar.current.isDateInToday($0.createdAt) }.reduce(0) { $0 + $1.wordCount }
    }

    var dictationsToday: Int {
        history.filter { Calendar.current.isDateInToday($0.createdAt) }.count
    }

    /// Only used by `--demo history`, so the History screenshot has something to show.
    func seedDemoHistory() {
        let now = Date()
        let samples = [
            ("Hey team, the new build is up. Can everyone test the onboarding flow before Friday?", 0.0, 6.2),
            ("Hi Sam,\n\nThanks for the notes. I'll send the updated deck tomorrow morning.\n\nBest,\nHammaad", 3600.0, 8.4),
            ("Pick up oat milk, coffee beans and something for dinner", 7200.0, 3.1),
            ("Remind me to book the dentist for next week", 90_000.0, 2.7),
        ]
        for (text, ago, secs) in samples.reversed() {
            let t = Transcript(text: text, createdAt: now.addingTimeInterval(-ago), durationSeconds: secs)
            historyStore.add(t)
        }
        history = historyStore.all()
    }
}
