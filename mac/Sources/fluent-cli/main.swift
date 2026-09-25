import FluentCore
import FluentMacKit
import Foundation

// fluent-cli: sends a WAV to Gemini through FluentCore's client, exactly as the app does, and
// prints the (styled) transcript. Used by CI against a mock server and, with a key, against Gemini.
//
//   FLUENT_GEMINI_KEY=… fluent-cli transcribe <file.wav> [--endpoint URL] [--verbatim]
//                                  [--language en-US] [--vocab "a,b"] [--app <bundle id>]
//
// The key is read from the environment only, so it never appears in a process list or a log.

func fail(_ message: String, _ code: Int32 = 2) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(code)
}

var args = Array(CommandLine.arguments.dropFirst())

#if !canImport(FoundationNetworking)
// fluent-cli live <file.wav> [--url wss://…] [--pace 1] [--verbatim] [--language CODE]
// Streams a 16 kHz mono PCM16 WAV through GeminiLiveClient in 100 ms chunks, paced like a person
// talking (--pace 1 = real time), then stops and prints how long the transcript took after stop.
if args.first == "live", args.count >= 2 {
    func opt(_ name: String) -> String? {
        guard let i = args.firstIndex(of: name), i + 1 < args.count else { return nil }
        return args[i + 1]
    }
    guard let wav = FileManager.default.contents(atPath: args[1]), wav.count > 44 else { fail("cannot read \(args[1])") }
    let pcm: [Int16] = wav.dropFirst(44).withUnsafeBytes { Array($0.bindMemory(to: Int16.self)) }
    let key = ProcessInfo.processInfo.environment["FLUENT_GEMINI_KEY"] ?? ""
    let url = opt("--url").flatMap(URL.init(string:)) ?? Constants.liveURL
    let pace = Double(opt("--pace") ?? "1") ?? 1
    let client = GeminiLiveClient(apiKey: key, mode: args.contains("--verbatim") ? .verbatim : .smart,
                                  languageCodes: opt("--language").map { [$0] } ?? [], vocabulary: [], url: url)
    let done = DispatchSemaphore(value: 0)
    nonisolated(unsafe) var report: [String: Any] = [:]
    Task {
        client.connect()
        let chunk = Int(Constants.sampleRate / 10)
        var i = 0
        while i < pcm.count {
            client.send(pcm: Array(pcm[i..<min(pcm.count, i + chunk)]))
            i += chunk
            if pace > 0 { try? await Task.sleep(nanoseconds: UInt64(0.1 / pace * 1_000_000_000)) }
        }
        let stopped = Date()
        let text = await client.finish()
        report = ["route": text == nil ? "fallback-needed" : "live", "text": text ?? "",
                  "stop_to_text_ms": Int(Date().timeIntervalSince(stopped) * 1000),
                  "audio_seconds": Double(pcm.count) / Constants.sampleRate,
                  "failure": client.failure.map { "\($0)" } ?? ""]
        done.signal()
    }
    done.wait()
    let data = try! JSONSerialization.data(withJSONObject: report, options: [.sortedKeys])
    print(String(data: data, encoding: .utf8)!)
    exit(report["route"] as? String == "live" ? 0 : 1)
}
#endif

guard args.first == "transcribe", args.count >= 2 else {
    fail("usage: fluent-cli transcribe <file.wav> [--endpoint URL] [--verbatim] [--language CODE] [--vocab a,b] [--app BUNDLE_ID]")
}
let path = args[1]
func option(_ name: String) -> String? {
    guard let i = args.firstIndex(of: name), i + 1 < args.count else { return nil }
    return args[i + 1]
}
let key = ProcessInfo.processInfo.environment["FLUENT_GEMINI_KEY"] ?? ""
let endpoint = option("--endpoint").flatMap(URL.init(string:)) ?? Constants.interactionsURL
let mode: TranscriptionMode = args.contains("--verbatim") ? .verbatim : .smart
let languages = option("--language").map { [$0] } ?? []
let vocab = option("--vocab").map { $0.split(separator: ",").map { String($0).trimmingCharacters(in: .whitespaces) } } ?? []
guard let wav = FileManager.default.contents(atPath: path) else { fail("cannot read \(path)") }

let client = GeminiClient(apiKey: key, endpoint: endpoint)
let done = DispatchSemaphore(value: 0)
nonisolated(unsafe) var outcome: Result<String, TranscriptionError> = .failure(.unknown("not run"))
Task {
    outcome = await client.transcribe(wav: wav, mode: mode, languageCodes: languages, vocabulary: vocab)
    done.signal()
}
done.wait()

switch outcome {
case .success(let text):
    if let app = option("--app") {
        let settings = Settings(defaults: .standard)
        print(settings.applyStyle(text, category: AppCategories.category(for: app)))
    } else {
        print(text)
    }
case .failure(let error):
    fail("error: \(error.userMessage) [\(error)]", 1)
}
