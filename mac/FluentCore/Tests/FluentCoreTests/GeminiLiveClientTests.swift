#if !canImport(FoundationNetworking)
import Foundation
import Testing
@testable import FluentCore

/// The Live client's wire format and turn handling. The socket itself is exercised end to end
/// against scripts/mock_gemini_live.py (fluent-cli live).
struct GeminiLiveClientTests {
    private func client(_ mode: TranscriptionMode = .smart, languages: [String] = [], vocab: [String] = []) -> GeminiLiveClient {
        GeminiLiveClient(apiKey: "k", mode: mode, languageCodes: languages, vocabulary: vocab,
                         url: URL(string: "ws://127.0.0.1:9/none")!)
    }

    @Test func setupMatchesAndroid() throws {
        let setup = try #require(client(.verbatim, languages: ["en-US"], vocab: ["Fluent"]).setupMessage()["setup"] as? [String: Any])
        #expect(setup["model"] as? String == "models/gemini-3.5-transcribe-live")
        let vad = (setup["realtimeInputConfig"] as? [String: Any])?["automaticActivityDetection"] as? [String: Any]
        #expect(vad?["disabled"] as? Bool == true)
        let t = try #require(setup["inputAudioTranscription"] as? [String: Any])
        #expect(t["mode"] as? String == "VERBATIM")
        #expect(t["languageCodes"] as? [String] == ["en-US"])
        #expect(t["customVocabulary"] as? [String] == ["Fluent"])
        #expect(((setup["generationConfig"] as? [String: Any])?["responseModalities"] as? [String]) == ["TEXT"])
    }

    @Test func audioIsBase64Pcm16At16k() throws {
        let pcm: [Int16] = [1, -2, 300]
        let data = pcm.withUnsafeBufferPointer { Data(buffer: $0) }
        let audio = try #require((client().audioMessage(data)["realtimeInput"] as? [String: Any])?["audio"] as? [String: Any])
        #expect(audio["mimeType"] as? String == "audio/pcm;rate=16000")
        #expect(Data(base64Encoded: audio["data"] as? String ?? "") == data)
        #expect(data == Data([1, 0, 0xFE, 0xFF, 0x2C, 0x01]))   // little-endian PCM16
    }

    @Test func finishWithoutASocketMeansFallback() async {
        let c = client()
        #expect(await c.finish(timeout: 0.2) == nil)
    }

    @Test func serverErrorBecomesInvalidKey() {
        let c = client()
        c.handle(Data(#"{"serverContent":{"inputTranscription":{"text":"hello"}}}"#.utf8))
        c.handle(Data(#"{"serverContent":{"inputTranscription":{"text":" there"}}}"#.utf8))
        c.handle(Data(#"{"error":{"code":403,"message":"API key not valid"}}"#.utf8))
        #expect(c.failure == .invalidApiKey)
    }
}
#endif
