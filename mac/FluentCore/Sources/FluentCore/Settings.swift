import Foundation

public let languageOptions: [(code: String, name: String)] = [
    (Settings.autoLanguage, "Detect automatically"),
    ("en-US", "English (United States)"),
    ("en-GB", "English (United Kingdom)"),
    ("es-ES", "Spanish (Spain)"),
    ("es-419", "Spanish (Latin America)"),
    ("fr-FR", "French"),
    ("de-DE", "German"),
    ("it-IT", "Italian"),
    ("pt-BR", "Portuguese (Brazil)"),
    ("nl-NL", "Dutch"),
    ("hi-IN", "Hindi"),
    ("ar-EG", "Arabic"),
    ("ja-JP", "Japanese"),
    ("ko-KR", "Korean"),
    ("zh-CN", "Chinese (Simplified)"),
    ("ru-RU", "Russian"),
    ("tr-TR", "Turkish"),
    ("pl-PL", "Polish"),
    ("sv-SE", "Swedish"),
    ("id-ID", "Indonesian"),
    ("vi-VN", "Vietnamese"),
    ("ur-PK", "Urdu"),
]

/// User settings, stored in the App Group's UserDefaults so the keyboard reads the same values.
/// The API key is not here: it lives in the Keychain (see the app's KeychainStore).
public struct Settings {
    public static let autoLanguage = "auto"
    public static let untilTurnedOff = 0

    public let defaults: UserDefaults

    public init(defaults: UserDefaults? = UserDefaults(suiteName: Constants.appGroup)) {
        self.defaults = defaults ?? .standard
    }

    private enum Key {
        static let setupComplete = "setup_complete"
        static let mode = "mode"
        static let language = "language"
        static let vocabulary = "vocabulary"
        static let history = "history_enabled"
        static let theme = "theme"
        static let styleEnabled = "style_enabled"
        static let keyboardCategory = "keyboard_category"
        static let sessionMinutes = "session_minutes"
        static let haptics = "haptics"
        static let autocorrect = "autocorrect"
        static let autoCapitalize = "auto_capitalize"
        static let keyboardSeenAt = "keyboard_seen_at"
        static let termsVersion = "terms_accepted_version"
        static let termsAcceptedAt = "terms_accepted_at"
        static func style(_ c: StyleCategory) -> String { "style_\(c.rawValue.lowercased())" }
    }

    public var setupComplete: Bool {
        get { defaults.bool(forKey: Key.setupComplete) }
        nonmutating set { defaults.set(newValue, forKey: Key.setupComplete) }
    }

    public var mode: TranscriptionMode {
        get { defaults.string(forKey: Key.mode).flatMap(TranscriptionMode.init(rawValue:)) ?? .smart }
        nonmutating set { defaults.set(newValue.rawValue, forKey: Key.mode) }
    }

    public var languageCode: String {
        get { defaults.string(forKey: Key.language) ?? Self.autoLanguage }
        nonmutating set { defaults.set(newValue, forKey: Key.language) }
    }

    /// What Gemini is told: nothing for automatic detection, otherwise the one chosen language.
    public var languageCodes: [String] {
        languageCode == Self.autoLanguage ? [] : [languageCode]
    }

    public var vocabulary: [String] {
        get { defaults.stringArray(forKey: Key.vocabulary) ?? [] }
        nonmutating set { defaults.set(newValue, forKey: Key.vocabulary) }
    }

    /// Off by default, as on Android: transcripts are only kept when the user asks for it.
    public var historyEnabled: Bool {
        get { defaults.bool(forKey: Key.history) }
        nonmutating set { defaults.set(newValue, forKey: Key.history) }
    }

    public var themeID: String {
        get { defaults.string(forKey: Key.theme) ?? "aurora" }
        nonmutating set { defaults.set(newValue, forKey: Key.theme) }
    }

    public var styleEnabled: Bool {
        get { defaults.object(forKey: Key.styleEnabled) as? Bool ?? true }
        nonmutating set { defaults.set(newValue, forKey: Key.styleEnabled) }
    }

    public var hapticsEnabled: Bool {
        get { defaults.object(forKey: Key.haptics) as? Bool ?? true }
        nonmutating set { defaults.set(newValue, forKey: Key.haptics) }
    }

    public var autocorrectEnabled: Bool {
        get { defaults.object(forKey: Key.autocorrect) as? Bool ?? true }
        nonmutating set { defaults.set(newValue, forKey: Key.autocorrect) }
    }

    public var autoCapitalizeEnabled: Bool {
        get { defaults.object(forKey: Key.autoCapitalize) as? Bool ?? true }
        nonmutating set { defaults.set(newValue, forKey: Key.autoCapitalize) }
    }

    /// The category the keyboard last used; it sends it with every dictation.
    public var keyboardCategory: StyleCategory {
        get { defaults.string(forKey: Key.keyboardCategory).flatMap(StyleCategory.init(rawValue:)) ?? .personal }
        nonmutating set { defaults.set(newValue.rawValue, forKey: Key.keyboardCategory) }
    }

    /// How long the microphone stays ready for the keyboard after the last dictation.
    /// `Settings.untilTurnedOff` keeps it ready until the user ends it (or iOS stops the app).
    public var sessionMinutes: Int {
        get { (defaults.object(forKey: Key.sessionMinutes) as? Int) ?? 5 }
        nonmutating set { defaults.set(newValue, forKey: Key.sessionMinutes) }
    }

    /// Written by the keyboard each time it opens with Full Access on. A keyboard without Full
    /// Access cannot write to the App Group at all, so a recent value means both steps are done.
    public var keyboardSeenAt: Date? {
        get { defaults.object(forKey: Key.keyboardSeenAt) as? Date }
        nonmutating set { defaults.set(newValue, forKey: Key.keyboardSeenAt) }
    }

    public func style(for category: StyleCategory) -> WritingStyle {
        guard let raw = defaults.string(forKey: Key.style(category)),
              let style = WritingStyle(rawValue: raw),
              category.styles.contains(style) else { return category.defaultStyle }
        return style
    }

    public func setStyle(_ style: WritingStyle, for category: StyleCategory) {
        defaults.set(style.rawValue, forKey: Key.style(category))
    }

    public var termsAccepted: Bool {
        defaults.string(forKey: Key.termsVersion) == Constants.termsVersion
    }

    public func acceptTerms(now: Date = Date()) {
        defaults.set(Constants.termsVersion, forKey: Key.termsVersion)
        defaults.set(now.timeIntervalSince1970, forKey: Key.termsAcceptedAt)
    }

    /// Verbatim mode promises the words exactly as spoken, so styles apply in Smart mode only.
    public func applyStyle(_ transcript: String, category: StyleCategory) -> String {
        guard styleEnabled, mode == .smart else { return transcript }
        let styled = StyleFormatter.format(transcript, style: style(for: category), category: category)
        return styled.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? transcript : styled
    }
}
