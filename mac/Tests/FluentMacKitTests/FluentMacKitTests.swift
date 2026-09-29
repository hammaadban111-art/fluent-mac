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
    @Test func combosLabelAndRoundTrip() {
        #expect(KeyCombo.defaultHold.label == "Right Option")
        #expect(KeyCombo.defaultToggle.label == "⌃⌥ Space")
        #expect(KeyCombo(modifiers: [KeyCombo.fn], key: nil).label == "Fn")
        #expect(KeyCombo(modifiers: [], key: 96).label == "F5")
        #expect(KeyCombo(modifiers: [KeyCombo.leftCommand, KeyCombo.rightShift], key: 2).label == "⇧⌘ D")
        #expect(KeyCombo(modifiers: [KeyCombo.rightCommand, KeyCombo.leftCommand], key: nil).label == "Left Command + Right Command")
        for c in [KeyCombo.defaultHold, .defaultToggle, .off, KeyCombo(modifiers: [KeyCombo.fn, KeyCombo.leftShift], key: nil)] {
            #expect(KeyCombo.parse(c.code) == c)
        }
        #expect(KeyCombo.parse("nonsense") == nil)
        #expect(KeyCombo.parse("0+1") == nil)   // two ordinary keys
        // Carbon has no Fn bit, so Fn is dropped from combinations with an ordinary key.
        #expect(KeyCombo(modifiers: [KeyCombo.fn, KeyCombo.leftOption], key: 49).modifiers == [KeyCombo.leftOption])
        #expect(KeyCombo.defaultToggle.carbonModifiers == (1 << 12 | 1 << 11))
        #expect(KeyCombo(modifiers: [], key: 0).warning != nil)   // a lone letter
        #expect(KeyCombo(modifiers: [KeyCombo.leftCommand], key: 0).warning == nil)
    }

    @Test func legacySettingsCarryOver() {
        #expect(KeyCombo.fromLegacyHold("rightOption") == .defaultHold)
        #expect(KeyCombo.fromLegacyHold("fn").label == "Fn")
        #expect(KeyCombo.fromLegacyHold("off").isOff)
        #expect(KeyCombo.fromLegacyToggle("⌃⌥ D").label == "⌃⌥ D")
        #expect(KeyCombo.fromLegacyToggle("⇧⌘ Space").label == "⇧⌘ Space")
        #expect(KeyCombo.fromLegacyToggle(nil) == .defaultToggle)
        let d = UserDefaults(suiteName: "fluent.mac.test.\(UUID().uuidString)")!
        d.set("rightCommand", forKey: "hold_key")
        d.set("Off", forKey: "toggle_shortcut")
        let m = MacSettings(defaults: d)
        #expect(m.holdKey.label == "Right Command")
        #expect(m.toggleShortcut.isOff)
    }

    @Test func recorderCapturesCombos() {
        var r = KeyComboRecorder()
        do { let v = r.modifier(KeyCombo.leftControl, isDown: true); #expect(v == .continue) }
        do { let v = r.modifier(KeyCombo.leftOption, isDown: true); #expect(v == .continue) }
        do { let v = r.key(2); #expect(v == .done(KeyCombo(modifiers: [KeyCombo.leftControl, KeyCombo.leftOption], key: 2))) }
        r = KeyComboRecorder()
        _ = r.modifier(KeyCombo.rightOption, isDown: true)
        _ = r.modifier(KeyCombo.rightCommand, isDown: true)
        do { let v = r.modifier(KeyCombo.rightCommand, isDown: false); #expect(v == .continue) }
        do { let v = r.modifier(KeyCombo.rightOption, isDown: false); #expect(v == .done(KeyCombo(modifiers: [KeyCombo.rightOption, KeyCombo.rightCommand], key: nil))) }
        r = KeyComboRecorder()
        do { let v = r.key(KeyCombo.escape); #expect(v == .cancelled) }
        r = KeyComboRecorder()
        #expect(r.key(100) == .done(KeyCombo(modifiers: [], key: 100)))   // F8 alone
    }

    @Test func engineHoldsOnRightOptionAndIgnoresShortcuts() {
        var e = HotkeyEngine()
        do { let v = e.modifier(KeyCombo.rightOption, down: true, at: 0); #expect(v == [.armHoldTimer]) }
        do { let v = e.holdTimerFired(); #expect(v) }
        do { let v = e.modifier(KeyCombo.rightOption, down: false, at: 2); #expect(v == [.stopHold]) }
        // ⌥E: the other key spoils the hold.
        do { let v = e.modifier(KeyCombo.rightOption, down: true, at: 3); #expect(v == [.armHoldTimer]) }
        do { let v = e.otherKey(14, dictationLive: false); #expect(v == [.cancelHoldTimer]) }
        do { let v = e.holdTimerFired(); #expect(!v) }
        do { let v = e.modifier(KeyCombo.rightOption, down: false, at: 3.2); #expect(v == [.cancelHoldTimer]) }
        // Left Option is not Right Option.
        do { let v = e.modifier(KeyCombo.leftOption, down: true, at: 4); #expect(v == []) }
    }

    @Test func engineCarbonHoldAndToggle() {
        var e = HotkeyEngine(hold: KeyCombo(modifiers: [], key: 100), toggle: .defaultToggle)
        do { let v = e.hotKey(.hold, pressed: true, at: 0); #expect(v == [.armHoldTimer]) }
        #expect(e.hotKey(.hold, pressed: true, at: 0.1) == [])   // no double start
        do { let v = e.holdTimerFired(); #expect(v) }
        do { let v = e.hotKey(.hold, pressed: false, at: 2); #expect(v == [.stopHold]) }
        do { let v = e.hotKey(.toggle, pressed: true, at: 3); #expect(v == [.toggle]) }
        do { let v = e.hotKey(.toggle, pressed: false, at: 3.1); #expect(v == []) }
        do { let v = e.otherKey(KeyCombo.escape, dictationLive: true); #expect(v == [.cancel]) }
        e.active = false
        do { let v = e.hotKey(.toggle, pressed: true, at: 4); #expect(v == []) }
    }

    @Test func engineModifierTapToggles() {
        var e = HotkeyEngine(hold: .off, toggle: KeyCombo(modifiers: [KeyCombo.rightCommand], key: nil))
        _ = e.modifier(KeyCombo.rightCommand, down: true, at: 0)
        do { let v = e.modifier(KeyCombo.rightCommand, down: false, at: 0.2); #expect(v == [.toggle]) }
        // ⌘C is not a tap.
        _ = e.modifier(KeyCombo.rightCommand, down: true, at: 1)
        _ = e.otherKey(8, dictationLive: false)
        do { let v = e.modifier(KeyCombo.rightCommand, down: false, at: 1.2); #expect(v == []) }
        // Held too long is not a tap.
        _ = e.modifier(KeyCombo.rightCommand, down: true, at: 2)
        do { let v = e.modifier(KeyCombo.rightCommand, down: false, at: 3); #expect(v == []) }
    }

    @Test func sameModifiersTapTogglesHoldTalks() {
        var e = HotkeyEngine(hold: .defaultHold, toggle: .defaultHold)
        _ = e.modifier(KeyCombo.rightOption, down: true, at: 0)
        do { let v = e.modifier(KeyCombo.rightOption, down: false, at: 0.1); #expect(v == [.cancelHoldTimer, .toggle]) }
        _ = e.modifier(KeyCombo.rightOption, down: true, at: 1)
        do { let v = e.holdTimerFired(); #expect(v) }
        do { let v = e.modifier(KeyCombo.rightOption, down: false, at: 3); #expect(v == [.stopHold]) }
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

    @Test func shortcutSettingsPersist() {
        let m = MacSettings(defaults: UserDefaults(suiteName: "fluent.mac.test.\(UUID().uuidString)")!)
        #expect(m.toggleShortcut == .defaultToggle)
        #expect(m.holdKey == .defaultHold)
        m.toggleShortcut = KeyCombo(modifiers: [KeyCombo.leftControl, KeyCombo.leftOption], key: 2)
        #expect(m.toggleShortcut.label == "⌃⌥ D")
        m.holdKey = KeyCombo(modifiers: [], key: 96)
        #expect(m.holdKey.label == "F5")
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

struct TranscriptRaceTests {
    static func live(_ text: String?, _ ms: UInt64) -> @Sendable () async -> String? {
        { try? await Task.sleep(nanoseconds: ms * 1_000_000); return text }
    }
    static func batch(_ text: String?, _ ms: UInt64) -> @Sendable () async -> Result<String, TranscriptionError> {
        { try? await Task.sleep(nanoseconds: ms * 1_000_000); return text.map { .success($0) } ?? .failure(.connectionLost) }
    }

    @Test func fastLiveWins() async {
        let (r, route) = await TranscriptRace.run(live: Self.live("hello", 20), batch: Self.batch("batch", 10), grace: 0.3)
        #expect((try? r.get()) == "hello")
        #expect(route == "live")
    }

    @Test func failedLiveFallsBackToBatch() async {
        let (r, route) = await TranscriptRace.run(live: Self.live(nil, 10), batch: Self.batch("batch", 10), grace: 0.3)
        #expect((try? r.get()) == "batch")
        #expect(route == "batch")
        let (r2, _) = await TranscriptRace.run(live: nil, batch: Self.batch("only", 10))
        #expect((try? r2.get()) == "only")
    }

    @Test func slowLiveStartsBatchAndFirstAnswerWins() async {
        let start = Date()
        let (r, route) = await TranscriptRace.run(live: Self.live("late", 3000), batch: Self.batch("batch", 100), grace: 0.2)
        #expect((try? r.get()) == "batch")
        #expect(route.hasPrefix("batch"))
        #expect(Date().timeIntervalSince(start) < 1.5)
        let (r2, route2) = await TranscriptRace.run(live: Self.live("live", 400), batch: Self.batch("batch", 3000), grace: 0.2)
        #expect((try? r2.get()) == "live")
        #expect(route2 == "live")
    }

    @Test func batchErrorWaitsForLateLive() async {
        let (r, route) = await TranscriptRace.run(live: Self.live("late", 600), batch: Self.batch(nil, 50), grace: 0.2)
        #expect((try? r.get()) == "late")
        #expect(route == "live")
        let (r2, _) = await TranscriptRace.run(live: Self.live(nil, 600), batch: Self.batch(nil, 50), grace: 0.2)
        #expect((try? r2.get()) == nil)
    }
}

struct HoldSpoilTests {
    @Test func carbonToggleSpoilsAModifierHold() {
        var e = HotkeyEngine(hold: .defaultHold, toggle: .defaultToggle)
        do { let v = e.modifier(KeyCombo.rightOption, down: true, at: 0); #expect(v == [.armHoldTimer]) }
        do { let v = e.hotKey(.toggle, pressed: true, at: 0.1); #expect(v == [.cancelHoldTimer, .toggle]) }
        do { let v = e.holdTimerFired(); #expect(!v) }
    }
}

struct ReviewFixTests {
    @Test func sidesDoNotMatterWithAnOrdinaryKey() {
        #expect(KeyCombo(modifiers: [KeyCombo.rightControl], key: 49) == KeyCombo(modifiers: [KeyCombo.leftControl], key: 49))
        #expect(KeyCombo(modifiers: [KeyCombo.rightOption], key: nil) != KeyCombo(modifiers: [KeyCombo.leftOption], key: nil))
    }

    @Test func shiftThenRightOptionIsNotAHold() {
        var e = HotkeyEngine(hold: .defaultHold, toggle: .off)
        do { let v = e.modifier(KeyCombo.leftShift, down: true, at: 0); #expect(v == []) }
        do { let v = e.modifier(KeyCombo.rightOption, down: true, at: 0.1); #expect(v == []) }
        do { let v = e.holdTimerFired(); #expect(!v) }
    }
}
