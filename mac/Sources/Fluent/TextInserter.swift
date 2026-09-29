import AppKit
import ApplicationServices
import Carbon
import FluentMacKit

enum InsertOutcome: Equatable {
    /// Written through Accessibility and confirmed by reading the field back.
    case accessibility(String)
    /// Pasted with ⌘V.
    case pasted(String)
    /// Not put in (a password field, or Fluent itself was in front); on the clipboard to paste by hand.
    case copied(String)
    case failed(String)
}

/// Puts a transcript where the user is typing, in any app, and leaves it on the clipboard too so it
/// can be pasted again (Wispr Flow does the same). Where Accessibility can read the field, the text is
/// written through it (spaced against the words around the caret) and read back to confirm; apps that
/// ignore that (WhatsApp, Electron, web editors) get ⌘V. Where Accessibility cannot see a text field at
/// all, the paste still goes to the app in front, as a person pasting would. Only password fields are
/// refused; the text is copied instead.
@MainActor
enum TextInserter {
    static var log: ((String) -> Void)?

    static func insert(_ text: String, fallback: FocusedField?, appPid: pid_t? = nil) async -> InsertOutcome {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return .failed("empty transcription") }

        // Fluent's own window in front (the user clicked it while talking): go back to the app the words are for.
        if NSWorkspace.shared.frontmostApplication?.processIdentifier == ProcessInfo.processInfo.processIdentifier {
            guard let pid = fallback?.pid ?? appPid, let app = NSRunningApplication(processIdentifier: pid) else {
                return copyOnly(trimmed, "Fluent is the app in front")
            }
            app.activate()
            try? await Task.sleep(nanoseconds: 150_000_000)
        }

        // The field focused now wins; the one captured when dictation started is the fallback, but only
        // while its app is still in front (⌘V always goes to the app in front).
        if fallback?.kind == .secure { return copyOnly(trimmed, "password field", concealed: true) }
        var field = FieldFinder.frontmost()
        let frontPid = NSWorkspace.shared.frontmostApplication?.processIdentifier
        if field?.kind != .editable, let fallback, fallback.kind == .editable, fallback.pid == frontPid,
           field == nil || field?.pid == fallback.pid {
            field = fallback
        }
        if field?.kind == .secure { return copyOnly(trimmed, "password field", concealed: true) }
        await waitForModifiersUp()
        guard let field, field.kind == .editable else {
            // Nothing Accessibility can write or check: paste at the app's own caret.
            log?("no editable field (role \(field?.traits.role ?? "none")), pasting anyway")
            return await paste(trimmed, keep: trimmed) ? .pasted(trimmed) : copyOnly(trimmed, "paste failed")
        }
        let el = field.element

        guard let reported = el.string("AXValue") else {
            // Nothing readable to space against or to confirm with: paste at the app's own caret.
            log?("value unreadable (role \(field.traits.role ?? "?")), pasting")
            return await paste(trimmed, keep: trimmed) ? .pasted(trimmed) : copyOnly(trimmed, "paste failed")
        }

        let sel = el.range("AXSelectedTextRange")
        let selStart = sel.map { $0.location } ?? -1
        let selEnd = sel.map { $0.location + $0.length } ?? -1
        let existingKind = InsertionRules.classifyExisting(
            text: reported, hint: el.string("AXPlaceholderValue"), showingHintFlag: false,
            selectionStart: selStart, selectionEnd: selEnd)

        if existingKind == .uncertain {
            // Could be a placeholder or real text: pasting lands at the app's real caret either way,
            // then the spacing is fixed if the paste joined onto a word.
            log?("existing text uncertain, paste then tidy")
            if await paste(trimmed, keep: trimmed) {
                tidyAfterPaste(el, piece: trimmed)
                return .pasted(trimmed)
            }
        }

        let existing = existingKind == .placeholder ? "" : reported
        let length = (existing as NSString).length
        var start = sel != nil && existingKind != .placeholder ? selStart : length
        var end = sel != nil && existingKind != .placeholder ? selEnd : length
        if start > end { swap(&start, &end) }
        start = min(max(0, start), length)
        end = min(max(start, end), length)
        let piece = InsertionRules.spacedInsertion(existing: existing, start: start, end: end, text: trimmed)
        let expected = InsertionRules.splice(existing, start: start, end: end, piece: piece).text

        if el.isSettable("AXSelectedText") {
            var range = CFRange(location: existingKind == .placeholder ? 0 : start,
                                length: existingKind == .placeholder ? (reported as NSString).length : end - start)
            if let value = AXValueCreate(.cfRange, &range) { el.set("AXSelectedTextRange", value) }
            let status = el.set("AXSelectedText", piece as CFString)
            log?("AXSelectedText set -> \(status.rawValue)")
            var changed = false
            for wait in [0, 60, 100, 150, 250, 350] as [UInt64] {
                try? await Task.sleep(nanoseconds: wait * 1_000_000)
                let after = el.string("AXValue")
                if InsertionRules.landed(before: reported, after: after, expected: expected) {
                    copy(trimmed)
                    return .accessibility(piece)
                }
                changed = changed || (after != nil && after != reported)
            }
            if changed {
                // The field changed, just not exactly as expected (the app reformats, or is still
                // updating): the text went in, and pasting now would put it in twice.
                log?("field changed differently than expected after the Accessibility write")
                copy(trimmed)
                return .accessibility(piece)
            }
            log?("field did not change after the Accessibility write")
        } else {
            log?("AXSelectedText not settable")
        }

        // Some fields (web inputs, custom editors, WhatsApp) ignore Accessibility writes.
        if await paste(piece, keep: trimmed) {
            return .pasted(piece)
        }
        return copyOnly(trimmed, "paste failed")
    }

    private static func copyOnly(_ text: String, _ why: String, concealed: Bool = false) -> InsertOutcome {
        copy(text, concealed: concealed)
        log?("copied only: \(why)")
        return .copied(why)
    }

    /// Leaves `text` on the clipboard as an ordinary copy (clipboard managers keep it), or, for a password
    /// field, marked concealed so clipboard managers skip it.
    static func copy(_ text: String, concealed: Bool = false) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(text, forType: .string)
        if concealed { pb.setData(Data(), forType: NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType")) }
    }

    /// ⌘V must not mix with keys the user is still holding (the shortcut that stopped the dictation).
    private static func waitForModifiersUp() async {
        let mask: CGEventFlags = [.maskCommand, .maskAlternate, .maskControl, .maskShift, .maskSecondaryFn]
        for _ in 0..<30 {
            if CGEventSource.flagsState(.combinedSessionState).intersection(mask).isEmpty { return }
            try? await Task.sleep(nanoseconds: 50_000_000)
        }
    }

    private static func tidyAfterPaste(_ el: AXUIElement, piece: String) {
        guard let after = el.string("AXValue"),
              let fixed = InsertionRules.respaceAfterPaste(after: after, piece: piece),
              el.isSettable("AXValue") else { return }
        el.set("AXValue", fixed.text as CFString)
        var caret = CFRange(location: fixed.caret, length: 0)
        if let value = AXValueCreate(.cfRange, &caret) { el.set("AXSelectedTextRange", value) }
    }

    // MARK: paste

    /// Puts `piece` on the clipboard and presses ⌘V in the app in front. Afterwards the clipboard holds
    /// `keep` (the transcript without the spacing added for this spot), so the user can paste it again.
    static func paste(_ piece: String, keep: String) async -> Bool {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(piece, forType: .string)
        let same = piece == keep
        // Clipboard managers skip items marked transient; only the spaced copy is transient.
        if !same { pb.setData(Data(), forType: NSPasteboard.PasteboardType("org.nspasteboard.TransientType")) }
        let ours = pb.changeCount

        let ok = sendCommand(key: "v", toPid: nil)
        log?("⌘V posted: \(ok)")
        try? await Task.sleep(nanoseconds: 250_000_000)
        if !same {
            // Apps read the clipboard a moment after ⌘V; swapping it sooner could paste the wrong text.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                MainActor.assumeIsolated {
                    if pb.changeCount == ours { copy(keep) }
                }
            }
        }
        return ok
    }

    /// Posts ⌘<key> using the key that types `key` on the current keyboard layout.
    static func sendCommand(key: String, toPid pid: pid_t?) -> Bool {
        let code = keyCode(for: key)
        let source = CGEventSource(stateID: .combinedSessionState)
        guard let down = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: true),
              let up = CGEvent(keyboardEventSource: source, virtualKey: code, keyDown: false) else { return false }
        down.flags = .maskCommand
        up.flags = .maskCommand
        if let pid {
            down.postToPid(pid)
            up.postToPid(pid)
        } else {
            down.post(tap: .cghidEventTap)
            up.post(tap: .cghidEventTap)
        }
        return true
    }

    /// The character a key types on the current keyboard layout, for naming shortcut keys.
    static func character(for code: UInt16) -> String? {
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let raw = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        let data = Unmanaged<CFData>.fromOpaque(raw).takeUnretainedValue() as Data
        return data.withUnsafeBytes { buffer -> String? in
            guard let layout = buffer.baseAddress?.assumingMemoryBound(to: UCKeyboardLayout.self) else { return nil }
            var dead: UInt32 = 0
            var length = 0
            var chars = [UniChar](repeating: 0, count: 4)
            let status = UCKeyTranslate(layout, code, UInt16(kUCKeyActionDisplay), 0, UInt32(LMGetKbdType()),
                                        OptionBits(1 << kUCKeyTranslateNoDeadKeysBit), &dead, 4, &length, &chars)
            return status == noErr && length > 0 ? String(utf16CodeUnits: chars, count: length) : nil
        }
    }

    static func keyCode(for character: String) -> CGKeyCode {
        let fallback: CGKeyCode = character == "v" ? 9 : 0
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let raw = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return fallback }
        let data = Unmanaged<CFData>.fromOpaque(raw).takeUnretainedValue() as Data
        return data.withUnsafeBytes { buffer -> CGKeyCode in
            guard let layout = buffer.baseAddress?.assumingMemoryBound(to: UCKeyboardLayout.self) else { return fallback }
            for code in 0..<128 {
                var dead: UInt32 = 0
                var length = 0
                var chars = [UniChar](repeating: 0, count: 4)
                let status = UCKeyTranslate(layout, UInt16(code), UInt16(kUCKeyActionDisplay), 0,
                                            UInt32(LMGetKbdType()), OptionBits(1 << kUCKeyTranslateNoDeadKeysBit),
                                            &dead, 4, &length, &chars)
                if status == noErr, length > 0, String(utf16CodeUnits: chars, count: length) == character {
                    return CGKeyCode(code)
                }
            }
            return fallback
        }
    }
}
