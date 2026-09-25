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
