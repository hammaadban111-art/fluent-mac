import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Transcription through the unary Interactions endpoint with `gemini-3.5-transcribe`, the same
/// request the Android app sends as its fallback. The iOS app records a whole turn and sends it
/// in one request, which suits the keyboard handoff: nothing streams while the user is switching
/// between apps.
public struct GeminiClient: Sendable {
    public let apiKey: String
    public let session: URLSession
    /// The Interactions endpoint. Only tests point it anywhere else (a local mock server).
    public let endpoint: URL
    public let modelsBase: URL

    public init(apiKey: String, session: URLSession = .shared,
                endpoint: URL = Constants.interactionsURL, modelsBase: URL = Constants.modelsURL) {
        self.apiKey = apiKey
        self.session = session
        self.endpoint = endpoint
        self.modelsBase = modelsBase
    }

    public func transcribe(
        wav: Data,
        mode: TranscriptionMode,
        languageCodes: [String],
        vocabulary: [String]
    ) async -> Result<String, TranscriptionError> {
        guard !apiKey.isEmpty else { return .failure(.noApiKey) }
        let request = Self.transcribeRequest(apiKey: apiKey, wav: wav, mode: mode,
                                             languageCodes: languageCodes, vocabulary: vocabulary,
                                             endpoint: endpoint)
        do {
            let (data, response) = try await session.data(for: request)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            guard (200..<300).contains(code) else { return .failure(Self.mapHTTPError(code: code, body: data)) }
            guard let text = Self.extractText(data)?.trimmingCharacters(in: .whitespacesAndNewlines),
                  !text.isEmpty else { return .failure(.noSpeech) }
            return .success(text)
        } catch {
            return .failure(Self.mapTransportError(error))
        }
    }

    /// Validates a key cheaply by fetching the model; used by Settings → Test connection.
    public func testConnection() async -> Result<String, TranscriptionError> {
        guard !apiKey.isEmpty else { return .failure(.noApiKey) }
        var request = URLRequest(url: modelsBase.appendingPathComponent(Constants.batchModel))
        request.setValue(apiKey, forHTTPHeaderField: "x-goog-api-key")
        request.timeoutInterval = 20
        do {
            let (data, response) = try await session.data(for: request)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            return (200..<300).contains(code) ? .success(Constants.batchModel)
                : .failure(Self.mapHTTPError(code: code, body: data))
        } catch {
            return .failure(Self.mapTransportError(error))
        }
    }

    // MARK: request

    public static func transcribeRequest(
        apiKey: String, wav: Data, mode: TranscriptionMode,
        languageCodes: [String], vocabulary: [String],
        endpoint: URL = Constants.interactionsURL
    ) -> URLRequest {
        var config: [String: Any] = [
            "mode": mode == .smart ? "smart" as Any : ["type": "verbatim"] as Any,
            "language_codes": languageCodes,
        ]
        if !vocabulary.isEmpty { config["custom_vocabulary"] = Array(vocabulary.prefix(1000)) }
        let body: [String: Any] = [
            "model": Constants.batchModel,
            "input": [["type": "audio", "data": wav.base64EncodedString(), "mime_type": "audio/wav"]],
            "generation_config": ["transcription_config": config],
        ]
        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue(apiKey, forHTTPHeaderField: "x-goog-api-key")
        request.setValue(Constants.apiRevision, forHTTPHeaderField: "Api-Revision")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: body, options: [.sortedKeys])
        request.timeoutInterval = Constants.batchTimeout
        return request
    }

    // MARK: response

    /// The Interactions response carries the transcript as text content inside `steps`.
    /// Falls back to `output_text` and to the classic `candidates` shape so a server-side
    /// response-format change cannot silently drop a transcript.
    public static func extractText(_ data: Data) -> String? {
        guard let root = rootObject(data) else { return nil }

        if let text = root["output_text"] as? String, !text.trimmingCharacters(in: .whitespaces).isEmpty {
            return text
        }
        if let steps = root["steps"] as? [[String: Any]] {
            let texts = steps.flatMap { ($0["content"] as? [[String: Any]]) ?? [] }
                .filter { ($0["type"] as? String) == "text" }
                .compactMap { $0["text"] as? String }
            let joined = texts.joined(separator: " ")
            if !joined.trimmingCharacters(in: .whitespaces).isEmpty { return joined }
        }
        if let candidates = root["candidates"] as? [[String: Any]],
           let parts = (candidates.first?["content"] as? [String: Any])?["parts"] as? [[String: Any]] {
            let joined = parts.compactMap { $0["text"] as? String }.joined()
            if !joined.trimmingCharacters(in: .whitespaces).isEmpty { return joined }
        }
        return nil
    }

    public static func mapHTTPError(code: Int, body: Data) -> TranscriptionError {
        let error = rootObject(body)?["error"] as? [String: Any]
        let message = (error?["message"] as? String) ?? ""
        let status = (error?["status"] as? String) ?? ""
        let reasons = ((error?["details"] as? [[String: Any]]) ?? []).compactMap { $0["reason"] as? String }

        let keyInvalid = reasons.contains { $0.localizedCaseInsensitiveContains("API_KEY") }
            || message.localizedCaseInsensitiveContains("API key not valid")
            || message.localizedCaseInsensitiveContains("API_KEY_INVALID")
        let quota = code == 429 || status == "RESOURCE_EXHAUSTED"
            || message.localizedCaseInsensitiveContains("quota")
            || message.localizedCaseInsensitiveContains("rate limit")

        if keyInvalid { return .invalidApiKey }
        if quota { return .quotaExceeded }
        if code == 401 || code == 403 || status == "PERMISSION_DENIED" { return .invalidApiKey }
        return .server(code: code, detail: message)
    }

    public static func mapTransportError(_ error: Error) -> TranscriptionError {
        guard let urlError = error as? URLError else { return .unknown(error.localizedDescription) }
        switch urlError.code {
        case .notConnectedToInternet, .cannotFindHost, .dnsLookupFailed, .dataNotAllowed,
             .internationalRoamingOff, .cannotConnectToHost:
            return .noNetwork
        case .timedOut, .networkConnectionLost:
            return .connectionLost
        default:
            return .unknown(urlError.localizedDescription)
        }
    }

    /// Bodies come back either as a bare object or wrapped in a single-element array depending on
    /// the endpoint, so both shapes are unwrapped here.
    private static func rootObject(_ data: Data) -> [String: Any]? {
        let parsed = try? JSONSerialization.jsonObject(with: data)
        if let object = parsed as? [String: Any] { return object }
        return (parsed as? [Any])?.first as? [String: Any]
    }
}
