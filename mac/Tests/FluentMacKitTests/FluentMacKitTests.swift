import FluentCore
import Foundation
#if canImport(CoreGraphics)
import CoreGraphics
#endif
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import Testing
@testable import FluentMacKit

/// Android's spacing and placeholder cases (VoiceAccessibilityService companion + PlaceholderTest).
struct InsertionRulesTests {
    @Test func addsSpaceAfterAWordAndBeforeTheNext() {
        #expect(InsertionRules.spacedInsertion(existing: "Hello", start: 5, end: 5, text: "world") == " world")
        #expect(InsertionRules.spacedInsertion(existing: "Hello ", start: 6, end: 6, text: "world") == "world")
        #expect(InsertionRules.spacedInsertion(existing: "", start: 0, end: 0, text: "  hi there  ") == "hi there")
        #expect(InsertionRules.spacedInsertion(existing: "done", start: 0, end: 0, text: "All") == "All ")
        #expect(InsertionRules.spacedInsertion(existing: "ab", start: 1, end: 1, text: "X") == " X ")
    }

    @Test func punctuationRules() {
        #expect(InsertionRules.spacedInsertion(existing: "ok", start: 2, end: 2, text: ", then") == ", then")
        #expect(InsertionRules.spacedInsertion(existing: "wait.", start: 4, end: 4, text: "here") == " here")
        #expect(InsertionRules.spacedInsertion(existing: "a;b", start: 1, end: 1, text: "x") == " x ")
        #expect(InsertionRules.spacedInsertion(existing: "line\n", start: 5, end: 5, text: "next") == "next")
        #expect(InsertionRules.spacedInsertion(existing: "x", start: 1, end: 1, text: "   ") == "")
    }

    @Test func selectionIsReplacedAndOffsetsAreUTF16() {
        // "👋" is two UTF-16 units, as AXSelectedTextRange counts: offset 5 is between 👋 and "there".
        let s = "Hi 👋there"
        #expect(InsertionRules.spacedInsertion(existing: s, start: 5, end: 5, text: "you") == " you ")
        #expect(InsertionRules.spacedInsertion(existing: s, start: 3, end: 3, text: "you") == "you ")
        let spliced = InsertionRules.splice("Hello big world", start: 6, end: 9, piece: "small")
        #expect(spliced.text == "Hello small world")
        #expect(spliced.caret == 11)
        #expect(InsertionRules.splice("abc", start: 9, end: 2, piece: "Z").text == "abZ")
    }

    @Test func respacesAfterPaste() {
        let r = InsertionRules.respaceAfterPaste(after: "Hellohow are you", piece: "how are you")
        #expect(r?.text == "Hello how are you")
        #expect(r?.caret == 17)
        #expect(InsertionRules.respaceAfterPaste(after: "Hello how are you", piece: "how are you") == nil)
        #expect(InsertionRules.respaceAfterPaste(after: "fine.Thanks", piece: "fine.")?.text == "fine. Thanks")
        #expect(InsertionRules.respaceAfterPaste(after: "Yes,", piece: "Yes") == nil)
        #expect(InsertionRules.respaceAfterPaste(after: "nothing here", piece: "absent") == nil)
    }

    @Test func placeholderClassification() {
        typealias R = InsertionRules
        #expect(R.classifyExisting(text: "", hint: "Message", showingHintFlag: false, selectionStart: 0, selectionEnd: 0) == .empty)
        #expect(R.classifyExisting(text: "Message", hint: "Message", showingHintFlag: false, selectionStart: 0, selectionEnd: 0) == .placeholder)
        #expect(R.classifyExisting(text: "Message", hint: nil, showingHintFlag: true, selectionStart: 3, selectionEnd: 3) == .placeholder)
        #expect(R.classifyExisting(text: "Message", hint: nil, showingHintFlag: false, selectionStart: -1, selectionEnd: -1) == .uncertain)
        #expect(R.classifyExisting(text: "Message", hint: nil, showingHintFlag: false, selectionStart: 0, selectionEnd: 0) == .uncertain)
        #expect(R.classifyExisting(text: "Hello", hint: "Message", showingHintFlag: false, selectionStart: 5, selectionEnd: 5) == .real)
        #expect(R.classifyExisting(text: "Message", hint: "Message", showingHintFlag: false, selectionStart: 7, selectionEnd: 7) == .real)
    }

    @Test func landedOnlyWhenTheValueMoved() {
        #expect(InsertionRules.landed(before: "a", after: "a b", expected: "a b"))
        #expect(InsertionRules.landed(before: "a", after: "a B", expected: "a b"))    // app auto-formatted
        #expect(!InsertionRules.landed(before: "a", after: "a", expected: "a b"))     // ignored → paste
        #expect(!InsertionRules.landed(before: "a", after: nil, expected: "a b"))
    }
}

struct FieldDetectionTests {
    @Test func ordinaryTextRolesAreEditable() {
        for role in ["AXTextField", "AXTextArea", "AXComboBox"] {
            #expect(FieldClassifier.classify(FieldTraits(role: role)) == .editable)
        }
        #expect(FieldClassifier.classify(FieldTraits(role: "AXTextField", subrole: "AXSearchField")) == .editable)
    }

    @Test func passwordsAreNeverEditable() {
        #expect(FieldClassifier.classify(FieldTraits(role: "AXSecureTextField", valueSettable: true)) == .secure)
        #expect(FieldClassifier.classify(FieldTraits(role: "AXTextField", subrole: "AXSecureTextField", valueSettable: true)) == .secure)
    }

    @Test func webAndElectronEditorsCountWhenWritable() {
        // Slack / Claude / Discord message boxes: contenteditable, reported as a group.
        #expect(FieldClassifier.classify(FieldTraits(role: "AXGroup", valueSettable: true)) == .editable)
        #expect(FieldClassifier.classify(FieldTraits(role: "AXWebArea", selectedTextSettable: true)) == .editable)
        #expect(FieldClassifier.classify(FieldTraits(role: "AXGroup")) == .notEditable)
        #expect(FieldClassifier.classify(FieldTraits(role: "AXWebArea")) == .notEditable)
    }

    @Test func settableControlsThatAreNotTextAreIgnored() {
        #expect(FieldClassifier.classify(FieldTraits(role: "AXSlider", valueSettable: true)) == .notEditable)
        #expect(FieldClassifier.classify(FieldTraits(role: "AXCheckBox", valueSettable: true)) == .notEditable)
        #expect(FieldClassifier.classify(FieldTraits(role: "AXButton")) == .notEditable)
        #expect(FieldClassifier.classify(FieldTraits(role: "AXTextField", enabled: false)) == .notEditable)
        #expect(FieldClassifier.classify(FieldTraits(role: nil)) == .notEditable)
    }

    @Test func accessibilityNudges() {
        #expect(AccessibilityNudge.attributes(for: "com.google.Chrome") == (true, true))
        #expect(AccessibilityNudge.attributes(for: "com.anthropic.claudefordesktop") == (true, false))
        #expect(AccessibilityNudge.attributes(for: "com.tinyspeck.slackmacgap") == (true, false))
        #expect(AccessibilityNudge.attributes(for: "com.apple.TextEdit") == (false, false))
        #expect(AccessibilityNudge.attributes(for: nil) == (false, false))
    }
}

struct StyleCategoryTests {
    @Test func macAppsMapLikeAndroidPackages() {
        #expect(AppCategories.category(for: "com.apple.MobileSMS") == .personal)
        #expect(AppCategories.category(for: "net.whatsapp.WhatsApp") == .personal)
        #expect(AppCategories.category(for: "ru.keepcoder.Telegram") == .personal)
        #expect(AppCategories.category(for: "com.tinyspeck.slackmacgap") == .work)
        #expect(AppCategories.category(for: "com.microsoft.teams2") == .work)
        #expect(AppCategories.category(for: "com.apple.mail") == .email)
        #expect(AppCategories.category(for: "com.microsoft.Outlook") == .email)
        #expect(AppCategories.category(for: "com.apple.Notes") == .other)
        #expect(AppCategories.category(for: nil) == .other)
    }

    @Test func noBundleIsInTwoCategories() {
        #expect(AppCategories.personal.isDisjoint(with: AppCategories.work))
        #expect(AppCategories.personal.isDisjoint(with: AppCategories.email))
        #expect(AppCategories.work.isDisjoint(with: AppCategories.email))
    }

    @Test func styleOnlyTouchesCapsAndPunctuation() {
        let s = Settings(defaults: UserDefaults(suiteName: "fluent.mac.test.\(UUID().uuidString)")!)
        s.setStyle(.veryCasual, for: .work)
        let out = s.applyStyle("Hey, the build is green. Ship it!", category: AppCategories.category(for: "com.tinyspeck.slackmacgap"))
        #expect(out == "hey the build is green ship it")
        let words = { (t: String) in t.lowercased().filter { $0.isLetter || $0 == " " } }
        #expect(words(out) == words("Hey, the build is green. Ship it!"))
        let mail = s.applyStyle("Hi Sam, the report is attached. Thanks, Hammaad.", category: .email)
        #expect(mail == "Hi Sam,\n\nThe report is attached.\n\nThanks,\nHammaad")
    }
}

struct ShortcutTests {
    @Test func holdKeyCodes() {
        #expect(HoldKey.rightOption.keyCode == 61)
        #expect(HoldKey.fn.keyCode == 63)
        #expect(HoldKey.forKeyCode(54) == .rightCommand)
        #expect(HoldKey.off.keyCode == nil)
    }

    @Test func holdStartsOnlyAfterTheDelayAndWithoutOtherKeys() {
        var g = HoldGesture()
        var fired = false
        #expect(g.handle(.down(at: 0)) == .armTimer)
        fired = g.timerFired(); #expect(fired)
        #expect(g.handle(.up(at: 2)) == .stopAndInsert)

        // ⌥E for an accent: the other key spoils the hold.
        #expect(g.handle(.down(at: 0)) == .armTimer)
        #expect(g.handle(.otherKey) == .cancelTimer)
        fired = g.timerFired(); #expect(!fired)
        #expect(g.handle(.up(at: 0.1)) == .cancelTimer)

        // A quick tap: released before the timer.
        #expect(g.handle(.down(at: 0)) == .armTimer)
        #expect(g.handle(.up(at: 0.1)) == .cancelTimer)
        fired = g.timerFired(); #expect(!fired)

        // Typing while already dictating does not cancel the dictation.
        #expect(g.handle(.down(at: 0)) == .armTimer)
        fired = g.timerFired(); #expect(fired)
        #expect(g.handle(.otherKey) == .none)
        #expect(g.handle(.up(at: 3)) == .stopAndInsert)
    }

    @Test func togglePresetsRoundTrip() {
        let m = MacSettings(defaults: UserDefaults(suiteName: "fluent.mac.test.\(UUID().uuidString)")!)
        #expect(m.toggleShortcut == .default)
        #expect(m.holdKey == .rightOption)
        m.toggleShortcut = ToggleShortcut.presets[2]
        #expect(m.toggleShortcut.label == "⌃⌥ D")
        m.toggleShortcut = .off
        #expect(m.toggleShortcut.isOff)
    }
}

struct MacSettingsTests {
    func fresh() -> MacSettings { MacSettings(defaults: UserDefaults(suiteName: "fluent.mac.test.\(UUID().uuidString)")!) }

    @Test func snooze() {
        let m = fresh()
        let t0 = Date(timeIntervalSince1970: 1_000_000)
        #expect(!m.snoozeActive(now: t0))
        m.snooze(.fifteen, now: t0)
        #expect(m.snoozeActive(now: t0.addingTimeInterval(14 * 60)))
        #expect(!m.snoozeActive(now: t0.addingTimeInterval(16 * 60)))
        m.snooze(.untilRestart, now: t0)
        #expect(m.snoozeActive(now: t0.addingTimeInterval(1e7)))
        m.clearRestartSnooze()
        #expect(!m.snoozeActive(now: t0))
    }

    @Test func bubbleVisibilityRules() {
        let m = fresh()
        let own = "com.hammaad.fluent.mac"
        #expect(m.bubbleAllowed(termsAccepted: true, field: .editable, bundleID: "com.apple.TextEdit", ownBundleID: own))
        #expect(!m.bubbleAllowed(termsAccepted: false, field: .editable, bundleID: "com.apple.TextEdit", ownBundleID: own))
        #expect(!m.bubbleAllowed(termsAccepted: true, field: .secure, bundleID: "com.apple.Safari", ownBundleID: own))
        #expect(!m.bubbleAllowed(termsAccepted: true, field: .notEditable, bundleID: "com.apple.Safari", ownBundleID: own))
        #expect(!m.bubbleAllowed(termsAccepted: true, field: .editable, bundleID: own, ownBundleID: own))
        m.excludedApps = ["com.apple.Terminal"]
        #expect(!m.bubbleAllowed(termsAccepted: true, field: .editable, bundleID: "com.apple.Terminal", ownBundleID: own))
        m.snooze(.hour)
        #expect(!m.bubbleAllowed(termsAccepted: true, field: .editable, bundleID: "com.apple.TextEdit", ownBundleID: own))
        m.clearSnooze()
        m.bubbleEnabled = false
        #expect(!m.bubbleAllowed(termsAccepted: true, field: .editable, bundleID: "com.apple.TextEdit", ownBundleID: own))
    }

    @Test func sizesAreClamped() {
        let m = fresh()
        m.bubbleSize = 200
        #expect(m.bubbleSize == 60)
        m.bubbleOpacity = 0
        #expect(m.bubbleOpacity == 0.35)
    }
}

struct BubblePlacementTests {
    let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)

    @Test func oneLineFieldGetsTheBubbleOnItsRight() {
        let p = BubblePlacement.origin(field: CGRect(x: 100, y: 200, width: 300, height: 24), bubble: 40, screen: screen)
        #expect(p == CGPoint(x: 408, y: 192))
    }

    @Test func tallFieldGetsItInsideTheBottomRight() {
        let p = BubblePlacement.origin(field: CGRect(x: 0, y: 50, width: 800, height: 600), bubble: 40, screen: screen)
        #expect(p == CGPoint(x: 746, y: 596))
    }

    @Test func staysOnScreenAndHonoursTheDragOffset() {
        let edge = BubblePlacement.origin(field: CGRect(x: 1200, y: 880, width: 240, height: 20), bubble: 40, screen: screen)
        #expect(edge.x + 40 <= 1436 && edge.y + 40 <= 896)
        let moved = BubblePlacement.origin(field: CGRect(x: 100, y: 200, width: 300, height: 24), bubble: 40,
                                           screen: screen, offset: CGSize(width: -20, height: 30))
        #expect(moved == CGPoint(x: 388, y: 222))
    }

    @Test func flipsToAppKit() {
        let r = BubblePlacement.toAppKit(CGRect(x: 10, y: 20, width: 40, height: 40), mainHeight: 900)
        #expect(r == CGRect(x: 10, y: 840, width: 40, height: 40))
    }
}

/// The Gemini request, built and sent by FluentCore's client to a real local HTTP server.
struct GeminiMockServerTests {
    let ok = #"{"steps":[{"content":[{"type":"text","text":"Hello from the mock server."}]}]}"#

    @Test func sendsTheAndroidRequestAndReadsTheTranscript() async throws {
        let server = try MockHTTPServer(body: ok)
        defer { server.stop() }
        let wav = WAV.encode(pcm16: [0, 100, -100, 32767])
        let client = GeminiClient(apiKey: "test-key-123", endpoint: server.baseURL.appendingPathComponent("v1beta/interactions"))
        let result = await client.transcribe(wav: wav, mode: .smart, languageCodes: ["en-US"], vocabulary: ["Hammaad", "Fluent"])
        #expect(result == .success("Hello from the mock server."))

        let req = try #require(server.requests.first)
        #expect(req.requestLine == "POST /v1beta/interactions HTTP/1.1")
        #expect(req.headers["x-goog-api-key"] == "test-key-123")
        #expect(req.headers["api-revision"] == Constants.apiRevision)
        #expect(req.headers["content-type"] == "application/json")
        let body = try #require(JSONSerialization.jsonObject(with: req.body) as? [String: Any])
        #expect(body["model"] as? String == "gemini-3.5-transcribe")
        let input = try #require((body["input"] as? [[String: Any]])?.first)
        #expect(input["type"] as? String == "audio")
        #expect(input["mime_type"] as? String == "audio/wav")
        #expect(Data(base64Encoded: input["data"] as? String ?? "") == wav)
        let tc = try #require((body["generation_config"] as? [String: Any])?["transcription_config"] as? [String: Any])
        #expect(tc["mode"] as? String == "smart")
        #expect(tc["language_codes"] as? [String] == ["en-US"])
        #expect(tc["custom_vocabulary"] as? [String] == ["Hammaad", "Fluent"])
    }

    @Test func verbatimModeIsAnObject() async throws {
        let server = try MockHTTPServer(body: ok)
        defer { server.stop() }
        let client = GeminiClient(apiKey: "k", endpoint: server.baseURL.appendingPathComponent("v1beta/interactions"))
        _ = await client.transcribe(wav: WAV.encode(pcm16: [1]), mode: .verbatim, languageCodes: [], vocabulary: [])
        let req = try #require(server.requests.first)
        let body = try #require(JSONSerialization.jsonObject(with: req.body) as? [String: Any])
        let tc = try #require((body["generation_config"] as? [String: Any])?["transcription_config"] as? [String: Any])
        #expect((tc["mode"] as? [String: String]) == ["type": "verbatim"])
        #expect(tc["custom_vocabulary"] == nil)
    }

    @Test func serverErrorsBecomeTheAndroidMessages() async throws {
        let bad = try MockHTTPServer(status: 400, body: #"{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}"#)
        defer { bad.stop() }
        let r1 = await GeminiClient(apiKey: "k", endpoint: bad.baseURL).transcribe(wav: Data(), mode: .smart, languageCodes: [], vocabulary: [])
        #expect(r1 == .failure(.invalidApiKey))
        #expect(TranscriptionError.invalidApiKey.userMessage == "Gemini rejected the API key. Check it in Settings.")

        let busy = try MockHTTPServer(status: 429, body: "{}")
        defer { busy.stop() }
        let r2 = await GeminiClient(apiKey: "k", endpoint: busy.baseURL).transcribe(wav: Data(), mode: .smart, languageCodes: [], vocabulary: [])
        #expect(r2 == .failure(.quotaExceeded))

        let empty = try MockHTTPServer(body: #"{"steps":[]}"#)
        defer { empty.stop() }
        let r3 = await GeminiClient(apiKey: "k", endpoint: empty.baseURL).transcribe(wav: Data(), mode: .smart, languageCodes: [], vocabulary: [])
        #expect(r3 == .failure(.noSpeech))

        let r4 = await GeminiClient(apiKey: "", endpoint: empty.baseURL).transcribe(wav: Data(), mode: .smart, languageCodes: [], vocabulary: [])
        #expect(r4 == .failure(.noApiKey))
    }

    @Test func testConnectionHitsTheModel() async throws {
        let server = try MockHTTPServer(body: #"{"name":"models/gemini-3.5-transcribe"}"#)
        defer { server.stop() }
        let client = GeminiClient(apiKey: "k", modelsBase: server.baseURL.appendingPathComponent("v1beta/models"))
        #expect(await client.testConnection() == .success("gemini-3.5-transcribe"))
        #expect(server.requests.first?.requestLine == "GET /v1beta/models/gemini-3.5-transcribe HTTP/1.1")
    }

    @Test func errorMessagesMatchAndroidConstants() {
        #expect(TranscriptionError.noApiKey.userMessage == "Add your Gemini API key in Settings first.")
        #expect(TranscriptionError.quotaExceeded.userMessage == "Gemini quota or rate limit reached. Try again shortly.")
        #expect(TranscriptionError.noNetwork.userMessage == "No internet connection.")
        #expect(TranscriptionError.connectionLost.userMessage == "Lost the connection to Gemini.")
        #expect(TranscriptionError.micUnavailable.userMessage == "Microphone is unavailable. Another app may be using it.")
        #expect(TranscriptionError.micPermission.userMessage == "Microphone permission was revoked.")
        #expect(TranscriptionError.noSpeech.userMessage == "Didn't catch any speech.")
        #expect(TranscriptionError.tooShort.userMessage == "That was too short to transcribe.")
        #expect(TranscriptionError.server(code: 500, detail: "").userMessage == "Gemini returned an error (500).")
        #expect(TranscriptionError.unknown("x").userMessage == "Transcription failed.")
        #expect(Constants.termsVersion == "2026-09-24")
    }
}
