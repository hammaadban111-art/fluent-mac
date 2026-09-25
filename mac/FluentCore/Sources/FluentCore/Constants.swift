import Foundation

public enum Constants {
    // Identity. The App Group is how the app and the keyboard share settings, state and results.
    public static let appGroup = "group.com.hammaad.fluent"
    public static let urlScheme = "fluent"

    // Audio
    public static let sampleRate: Double = 16_000
    public static let maxRecordingSeconds: TimeInterval = 9 * 60
    public static let minRecordingSeconds: TimeInterval = 0.35

    // Gemini
    public static let batchModel = "gemini-3.5-transcribe"
    public static let liveModel = "gemini-3.5-transcribe-live"
    public static let interactionsURL = URL(string: "https://generativelanguage.googleapis.com/v1beta/interactions")!
    public static let modelsURL = URL(string: "https://generativelanguage.googleapis.com/v1beta/models/")!
    public static let apiRevision = "2026-05-20"
    public static let batchTimeout: TimeInterval = 60
    public static let liveURL = URL(string: "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent")!
    /// Give the socket 6 s to come up and 6 s to finalise after stop (normally well under a second);
    /// past either, the whole recording goes through the batch request instead.
    public static let liveConnectTimeout: TimeInterval = 6
    public static let liveFinalizeTimeout: TimeInterval = 6

    // Legal. Bump termsVersion whenever the Terms change materially: everyone is asked to accept again.
    public static let termsVersion = "2026-09-24"
    public static let termsURL = URL(string: "https://fluent-voice-v2.vercel.app/terms")!
    public static let privacyURL = URL(string: "https://fluent-voice-v2.vercel.app/privacy")!
    public static let apiKeyURL = URL(string: "https://aistudio.google.com/apikey")!
}

public enum TranscriptionMode: String, CaseIterable, Codable, Sendable {
    case smart = "SMART"
    case verbatim = "VERBATIM"

    public var label: String { self == .smart ? "Smart" : "Verbatim" }
    public var detail: String {
        self == .smart
            ? "Removes filler words, fixes self-corrections, adds punctuation"
            : "Writes exactly what you said"
    }
}

public enum TranscriptionError: Error, Equatable, Sendable {
    case noApiKey
    case invalidApiKey
    case quotaExceeded
    case noNetwork
    case connectionLost
    case micUnavailable
    case micPermission
    case noSpeech
    case tooShort
    case server(code: Int, detail: String)
    case unknown(String)

    public var userMessage: String {
        switch self {
        case .noApiKey: "Add your Gemini API key in Settings first."
        case .invalidApiKey: "Gemini rejected the API key. Check it in Settings."
        case .quotaExceeded: "Gemini quota or rate limit reached. Try again shortly."
        case .noNetwork: "No internet connection."
        case .connectionLost: "Lost the connection to Gemini."
        case .micUnavailable: "Microphone is unavailable. Another app may be using it."
        case .micPermission: "Microphone permission was revoked."
        case .noSpeech: "Didn't catch any speech."
        case .tooShort: "That was too short to transcribe."
        case .server(let code, _): "Gemini returned an error (\(code))."
        case .unknown: "Transcription failed."
        }
    }
}
