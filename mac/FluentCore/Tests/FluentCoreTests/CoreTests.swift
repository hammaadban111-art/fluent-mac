import Foundation
import Testing
@testable import FluentCore

struct GeminiClientTests {
    @Test func requestMatchesAndroidShape() throws {
        let wav = WAV.encode(pcm16: [0, 1, -1])
        let req = GeminiClient.transcribeRequest(apiKey: "k", wav: wav, mode: .verbatim,
                                                 languageCodes: ["hi-IN"], vocabulary: ["Fluent"])
        #expect(req.httpMethod == "POST")
        #expect(req.url == Constants.interactionsURL)
        #expect(req.value(forHTTPHeaderField: "x-goog-api-key") == "k")
        #expect(req.value(forHTTPHeaderField: "Api-Revision") == Constants.apiRevision)
        let body = try #require(JSONSerialization.jsonObject(with: req.httpBody!) as? [String: Any])
        #expect(body["model"] as? String == "gemini-3.5-transcribe")
        let input = try #require((body["input"] as? [[String: Any]])?.first)
        #expect(input["mime_type"] as? String == "audio/wav")
        #expect(Data(base64Encoded: input["data"] as! String) == wav)
        let tc = try #require((body["generation_config"] as? [String: Any])?["transcription_config"] as? [String: Any])
        #expect((tc["mode"] as? [String: String]) == ["type": "verbatim"])
        #expect(tc["language_codes"] as? [String] == ["hi-IN"])
        #expect(tc["custom_vocabulary"] as? [String] == ["Fluent"])
    }

    @Test func smartModeIsAPlainStringAndEmptyVocabularyIsOmitted() throws {
        let req = GeminiClient.transcribeRequest(apiKey: "k", wav: Data(), mode: .smart, languageCodes: [], vocabulary: [])
        let body = try #require(JSONSerialization.jsonObject(with: req.httpBody!) as? [String: Any])
        let tc = try #require((body["generation_config"] as? [String: Any])?["transcription_config"] as? [String: Any])
        #expect(tc["mode"] as? String == "smart")
        #expect(tc["custom_vocabulary"] == nil)
        #expect(tc["language_codes"] as? [String] == [])
    }

    @Test func extractsTextFromEveryResponseShape() {
        let steps = #"{"steps":[{"content":[{"type":"thought","text":"x"},{"type":"text","text":"Hello"}]},{"content":[{"type":"text","text":"world"}]}]}"#
        #expect(GeminiClient.extractText(Data(steps.utf8)) == "Hello world")
        #expect(GeminiClient.extractText(Data(#"[{"output_text":"Hi there"}]"#.utf8)) == "Hi there")
        let cands = #"{"candidates":[{"content":{"parts":[{"text":"A"},{"text":"B"}]}}]}"#
        #expect(GeminiClient.extractText(Data(cands.utf8)) == "AB")
        #expect(GeminiClient.extractText(Data(#"{"steps":[]}"#.utf8)) == nil)
        #expect(GeminiClient.extractText(Data("not json".utf8)) == nil)
    }

    @Test func mapsErrors() {
        let badKey = #"{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}"#
        #expect(GeminiClient.mapHTTPError(code: 400, body: Data(badKey.utf8)) == .invalidApiKey)
        let reason = #"[{"error":{"code":400,"message":"x","details":[{"reason":"API_KEY_INVALID"}]}}]"#
        #expect(GeminiClient.mapHTTPError(code: 400, body: Data(reason.utf8)) == .invalidApiKey)
        #expect(GeminiClient.mapHTTPError(code: 429, body: Data()) == .quotaExceeded)
        #expect(GeminiClient.mapHTTPError(code: 403, body: Data()) == .invalidApiKey)
        #expect(GeminiClient.mapHTTPError(code: 500, body: Data(#"{"error":{"message":"boom"}}"#.utf8))
            == .server(code: 500, detail: "boom"))
        #expect(GeminiClient.mapTransportError(URLError(.notConnectedToInternet)) == .noNetwork)
        #expect(GeminiClient.mapTransportError(URLError(.timedOut)) == .connectionLost)
    }
}

struct WAVTests {
    @Test func headerIsValid() {
        let d = WAV.encode(pcm16: [1, -2, 300])
        #expect(d.count == 44 + 6)
        #expect(String(decoding: d[0..<4], as: UTF8.self) == "RIFF")
        #expect(String(decoding: d[8..<12], as: UTF8.self) == "WAVE")
        func u32(_ o: Int) -> UInt32 { d[o..<o + 4].enumerated().reduce(0) { $0 | UInt32($1.element) << (8 * $1.offset) } }
        #expect(u32(4) == 36 + 6)
        #expect(u32(24) == 16_000)
        #expect(u32(28) == 32_000)
        #expect(u32(40) == 6)
        #expect(d[44] == 1 && d[45] == 0)
        #expect(d[46] == 0xFE && d[47] == 0xFF)   // -2 little-endian
    }
}

struct HandoffTests {
    func fresh() -> Handoff {
        let name = "fluent.test.\(UUID().uuidString)"
        return Handoff(defaults: UserDefaults(suiteName: name)!)
    }

    @Test func staleHeartbeatReadsAsOff() {
        let h = fresh()
        let t0 = Date()
        #expect(h.snapshot(now: t0).state == .off)
        h.publish(SessionSnapshot(state: .recording, dictationID: "a", heartbeat: t0), notify: false)
        #expect(h.snapshot(now: t0.addingTimeInterval(1)).state == .recording)
        #expect(h.snapshot(now: t0.addingTimeInterval(10)).state == .off)
    }

    @Test func deliveryIsTakenOnceAndOnlyWhileFresh() {
        let h = fresh()
        let t0 = Date()
        h.deliver(Delivery(dictationID: "a", text: "hi", error: nil, at: t0))
        #expect(h.takeDelivery(now: t0)?.text == "hi")
        #expect(h.takeDelivery(now: t0) == nil)
        h.deliver(Delivery(dictationID: "b", text: "old", error: nil, at: t0))
        #expect(h.takeDelivery(now: t0.addingTimeInterval(600)) == nil)
    }

    @Test func commandRoundTrips() {
        let h = fresh()
        let c = Command(action: .start, category: .email)
        h.send(c)
        #expect(h.latestCommand()?.id == c.id)
        #expect(h.latestCommand()?.category == .email)
    }

    @Test func spacing() {
        #expect(InsertionSpacing.prepare("hello", before: "Hi") == " hello")
        #expect(InsertionSpacing.prepare("hello", before: "Hi ") == "hello")
        #expect(InsertionSpacing.prepare("hello", before: nil) == "hello")
        #expect(InsertionSpacing.prepare("hello", before: "") == "hello")
        #expect(InsertionSpacing.prepare(", then", before: "ok") == ", then")
        #expect(InsertionSpacing.prepare("hello", before: "(") == "hello")
        #expect(InsertionSpacing.prepare("hello", before: "line\n") == "hello")
    }
}

struct SettingsTests {
    @Test func defaultsAndStyleRules() {
        let s = Settings(defaults: UserDefaults(suiteName: "fluent.test.\(UUID().uuidString)")!)
        #expect(s.mode == .smart)
        #expect(s.languageCodes == [])
        #expect(s.historyEnabled == false)
        #expect(s.style(for: .personal) == .casual)
        #expect(s.style(for: .email) == .formal)
        s.setStyle(.veryCasual, for: .email)            // not offered for email: ignored
        #expect(s.style(for: .email) == .formal)
        s.setStyle(.veryCasual, for: .personal)
        #expect(s.applyStyle("Hey, you there?", category: .personal) == "hey you there?")
        s.mode = .verbatim
        #expect(s.applyStyle("Hey, you there?", category: .personal) == "Hey, you there?")
        s.languageCode = "hi-IN"
        #expect(s.languageCodes == ["hi-IN"])
        #expect(!s.termsAccepted)
        s.acceptTerms()
        #expect(s.termsAccepted)
    }
}

struct HistoryTests {
    @Test func addDeleteAndCap() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        let store = HistoryStore(directory: dir)
        let a = Transcript(text: "one two three", durationSeconds: 1)
        store.add(a)
        store.add(Transcript(text: "second", durationSeconds: 1))
        #expect(store.all().map(\.text) == ["second", "one two three"])
        #expect(a.wordCount == 3)
        store.delete(a.id)
        #expect(store.all().count == 1)
        for i in 0..<(HistoryStore.limit + 5) { store.add(Transcript(text: "\(i)", durationSeconds: 0)) }
        #expect(store.all().count == HistoryStore.limit)
        #expect(HistoryStore(directory: dir).all().first?.text == "\(HistoryStore.limit + 4)")
    }
}
