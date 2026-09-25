import Foundation

#if !canImport(FoundationNetworking)
/// Streams microphone audio to `gemini-3.5-transcribe-live` over the Live API WebSocket while the
/// user is still talking, so the transcript is (almost) ready the moment they stop. A port of
/// Android's `GeminiLiveClient`: same setup message, manual turn boundaries (the user says exactly
/// when dictation starts and stops), same finalise-then-await flow.
///
/// Audio sent before the socket is ready is queued and flushed on `setupComplete`. If the socket
/// never gets ready, or fails, `finish()` returns nil and the caller falls back to the batch
/// request with the full recording, so a turn is never lost.
public final class GeminiLiveClient: @unchecked Sendable {
    public let url: URL
    private let apiKey: String
    private let mode: TranscriptionMode
    private let languageCodes: [String]
    private let vocabulary: [String]
    private let session: URLSession

    private let lock = NSLock()
    private var task: URLSessionWebSocketTask?
    private var ready = false
    private var closed = false
    private var activityStarted = false
    private var queued: [Data] = []
    private var finals = ""
    private var turnDone = false
    /// True only when the server said the turn is complete: a stall, error or dropped socket leaves
    /// it false, and the caller then uses the batch fallback rather than a partial transcript.
    private var completedNormally = false
    private var waiter: CheckedContinuation<String?, Never>?
    public private(set) var failure: TranscriptionError?
    /// Latest interim (not yet final) text, for display.
    public var onInterim: (@Sendable (String) -> Void)?

    public init(apiKey: String, mode: TranscriptionMode, languageCodes: [String], vocabulary: [String],
                url: URL = Constants.liveURL, session: URLSession = .shared) {
        self.apiKey = apiKey
        self.mode = mode
        self.languageCodes = languageCodes
        self.vocabulary = vocabulary
        self.url = url
        self.session = session
    }

    public var isReady: Bool { lock.withLock { ready && !closed } }

    /// Opens the socket and sends the setup message. Returns immediately; audio can be sent at once.
    public func connect() {
        var comps = URLComponents(url: url, resolvingAgainstBaseURL: false)!
        comps.queryItems = (comps.queryItems ?? []) + [URLQueryItem(name: "key", value: apiKey)]
        var request = URLRequest(url: comps.url!)
        request.timeoutInterval = Constants.liveConnectTimeout
        let task = session.webSocketTask(with: request)
        lock.withLock { self.task = task }
        task.resume()
        send(Self.json(setupMessage()))
        receive()
        // Give up on the socket if it is not ready in time; the batch fallback takes over.
        DispatchQueue.global().asyncAfter(deadline: .now() + Constants.liveConnectTimeout) { [weak self] in
            guard let self else { return }
            if !self.lock.withLock({ self.ready }) { self.fail(.connectionLost) }
        }
    }

    /// Streams 16 kHz mono PCM16 samples. Safe to call from the audio thread.
    public func send(pcm: [Int16]) {
        guard !pcm.isEmpty else { return }
        let data = pcm.withUnsafeBufferPointer { Data(buffer: $0) }   // little-endian on every Mac
        let flushNow: Bool = lock.withLock {
            if closed { return false }
            if !ready { queued.append(data); return false }
            return true
        }
        if flushNow { send(Self.json(audioMessage(data))) }
    }

    /// Ends the utterance and waits for the final transcript. Nil means "use the batch fallback".
    public func finish(timeout: TimeInterval = Constants.liveFinalizeTimeout) async -> String? {
        let (isReady, started, alreadyDone): (Bool, Bool, Bool) = lock.withLock {
            (ready && !closed, activityStarted, turnDone)
        }
        if alreadyDone { close(); return normalResult() }
        guard isReady else { close(); return nil }
        if started { send(Self.json(["realtimeInput": ["activityEnd": [String: Any]()]])) }
        send(Self.json(["realtimeInput": ["audioStreamEnd": true]]))

        _ = await withCheckedContinuation { (continuation: CheckedContinuation<String?, Never>) in
            let resumeNow: String?? = lock.withLock {
                if turnDone || closed { return .some(finals) }
                waiter = continuation
                return .none
            }
            if let text = resumeNow { continuation.resume(returning: text) }
            DispatchQueue.global().asyncAfter(deadline: .now() + timeout) { [weak self] in
                self?.completeTurn()   // stalled: give up, the batch fallback takes over
            }
        }
        let text = normalResult()
        close()
        return text
    }

    private func normalResult() -> String? {
        lock.withLock { completedNormally && !finals.isBlank ? finals : nil }
    }

    public func close() {
        let task: URLSessionWebSocketTask? = lock.withLock {
            if closed { return nil }
            closed = true
            queued.removeAll()
            return self.task
        }
        task?.cancel(with: .normalClosure, reason: nil)
        completeTurn()
    }

    // MARK: messages

    func setupMessage() -> [String: Any] {
        var transcription: [String: Any] = [
            "mode": mode == .smart ? "SMART" : "VERBATIM",
            "languageCodes": languageCodes,
        ]
        if !vocabulary.isEmpty { transcription["customVocabulary"] = Array(vocabulary.prefix(1000)) }
        return ["setup": [
            "model": "models/\(Constants.liveModel)",
            "generationConfig": ["responseModalities": ["TEXT"]],
            "realtimeInputConfig": ["automaticActivityDetection": ["disabled": true]],
            "inputAudioTranscription": transcription,
        ]]
    }

    func audioMessage(_ pcm: Data) -> [String: Any] {
        ["realtimeInput": ["audio": ["data": pcm.base64EncodedString(),
                                     "mimeType": "audio/pcm;rate=\(Int(Constants.sampleRate))"]]]
    }

    // MARK: socket

    private func send(_ text: String) {
        guard let task = lock.withLock({ closed ? nil : self.task }) else { return }
        task.send(.string(text)) { [weak self] error in
            if error != nil { self?.fail(.connectionLost) }
        }
    }

    private func receive() {
        guard let task = lock.withLock({ closed ? nil : self.task }) else { return }
        task.receive { [weak self] result in
            guard let self else { return }
            switch result {
            case .success(.string(let s)): self.handle(Data(s.utf8)); self.receive()
            case .success(.data(let d)): self.handle(d); self.receive()
            case .success: self.receive()
            case .failure: self.fail(self.httpFailure(task) ?? .connectionLost)
            }
        }
    }

    private func httpFailure(_ task: URLSessionWebSocketTask) -> TranscriptionError? {
        guard let code = (task.response as? HTTPURLResponse)?.statusCode, code >= 300 else { return nil }
        switch code {
        case 400, 401, 403: return .invalidApiKey
        case 429: return .quotaExceeded
        default: return .server(code: code, detail: "")
        }
    }

    /// Handles one server message. Internal so tests can feed recorded messages.
    func handle(_ data: Data) {
        guard let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else { return }

        if root["setupComplete"] != nil { becameReady() }

        if let error = root["error"] as? [String: Any] {
            let code = (error["code"] as? Int) ?? Int("\(error["code"] ?? "")") ?? 0
            let message = (error["message"] as? String) ?? ""
            fail(code == 429 || message.localizedCaseInsensitiveContains("quota") ? .quotaExceeded
                 : [400, 401, 403].contains(code) ? .invalidApiKey : .server(code: code, detail: message))
            return
        }

        guard let server = root["serverContent"] as? [String: Any] else { return }
        if let interim = (server["interimInputTranscription"] as? [String: Any])?["text"] as? String, !interim.isEmpty {
            onInterim?(interim)
        }
        if let chunk = (server["inputTranscription"] as? [String: Any])?["text"] as? String, !chunk.isEmpty {
            lock.withLock {
                if !finals.isEmpty && !finals.hasSuffix(" ") { finals += " " }
                finals += chunk.trimmingCharacters(in: .whitespaces)
            }
        }
        if root["generationComplete"] != nil || server["generationComplete"] != nil || server["turnComplete"] != nil {
            lock.withLock { if !closed { completedNormally = true } }
            completeTurn()
        }
    }

    /// On `setupComplete`: open the turn, then drain the queue. New audio keeps queueing until the
    /// queue is empty, so chunks always go out in the order they were recorded.
    private func becameReady() {
        let first: Bool = lock.withLock {
            if activityStarted || closed { return false }
            activityStarted = true
            return true
        }
        guard first else { return }
        send(Self.json(["realtimeInput": ["activityStart": [String: Any]()]]))
        while true {
            let batch: [Data] = lock.withLock {
                if closed { return [] }
                let q = queued
                queued.removeAll()
                if q.isEmpty { ready = true }
                return q
            }
            if batch.isEmpty { break }
            for chunk in batch { send(Self.json(audioMessage(chunk))) }
        }
    }

    private func fail(_ error: TranscriptionError) {
        let task: URLSessionWebSocketTask? = lock.withLock {
            if failure == nil { failure = error }
            if closed { return nil }
            closed = true
            return self.task
        }
        task?.cancel(with: .abnormalClosure, reason: nil)
        completeTurn()
    }

    private func completeTurn() {
        let (waiter, text): (CheckedContinuation<String?, Never>?, String) = lock.withLock {
            turnDone = true
            let w = self.waiter
            self.waiter = nil
            return (w, finals)
        }
        waiter?.resume(returning: text)
    }

    private static func json(_ object: [String: Any]) -> String {
        (try? JSONSerialization.data(withJSONObject: object)).flatMap { String(data: $0, encoding: .utf8) } ?? "{}"
    }
}

private extension String {
    var isBlank: Bool { trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
}
#endif
