import AppKit
import ApplicationServices
import Carbon
import FluentMacKit

enum InsertOutcome: Equatable {
    /// Written through Accessibility and confirmed by reading the field back.
    case accessibility(String)
    /// The field ignored Accessibility, so the text went in with ⌘V (clipboard restored after).
    case pasted(String)
    case failed(String)
}

/// Puts a transcript at the caret of the focused text box, the way Android's
/// `VoiceAccessibilityService.insertAtCursor` does: write it through Accessibility, read the
/// field back to confirm, and paste when the app ignored the write.
@MainActor
enum TextInserter {
    static var log: ((String) -> Void)?

    static func insert(_ text: String, fallback: FocusedField?) async -> InsertOutcome {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return .failed("empty transcription") }

        // The field focused now wins; the one captured when dictation started is the fallback.
        var field = FieldFinder.frontmost()
        if field?.kind != .editable, let fallback, fallback.kind == .editable,
           field == nil || field?.pid == fallback.pid {
            field = fallback
        }
        guard let field else { return .failed("no focused field") }
        if field.kind == .secure { return .failed("refusing to type into a password field") }
        guard field.kind == .editable else { return .failed("focused field is not editable") }
        let el = field.element

        guard let reported = el.string("AXValue") else {
            // Nothing readable to space against or to confirm with: paste at the app's own caret.
            log?("value unreadable (role \(field.traits.role ?? "?")), pasting")
            return await paste(trimmed, pid: field.pid) ? .pasted(trimmed) : .failed("paste failed")
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
            if await paste(trimmed, pid: field.pid) {
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
            for _ in 0..<5 {
                if InsertionRules.landed(before: reported, after: el.string("AXValue"), expected: expected) {
                    return .accessibility(piece)
                }
                try? await Task.sleep(nanoseconds: 60_000_000)
            }
            log?("field did not change after the Accessibility write")
        } else {
            log?("AXSelectedText not settable")
        }

        // Some fields (web inputs, custom editors) ignore Accessibility writes.
        if await paste(piece, pid: field.pid) {
            return .pasted(piece)
        }
        return .failed("the field rejected both direct insertion and paste")
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

    /// Clipboard paste. Whatever the user had copied is put back afterwards, so Fluent never
    /// quietly eats it (Android: `pasteFallback`).
    static func paste(_ piece: String, pid: pid_t) async -> Bool {
        let pb = NSPasteboard.general
        let saved = snapshot(pb)
        pb.clearContents()
        pb.setString(piece, forType: .string)
        // Clipboard managers skip items marked transient.
        pb.setData(Data(), forType: NSPasteboard.PasteboardType("org.nspasteboard.TransientType"))
        let ours = pb.changeCount

        let front = NSWorkspace.shared.frontmostApplication?.processIdentifier == pid
        let ok = sendCommand(key: "v", toPid: front ? nil : pid)
        log?("⌘V posted: \(ok) (target frontmost: \(front))")
        try? await Task.sleep(nanoseconds: 350_000_000)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
            MainActor.assumeIsolated {
                if pb.changeCount == ours { restore(saved, to: pb) }
            }
        }
        return ok
    }

    private static func snapshot(_ pb: NSPasteboard) -> [[(NSPasteboard.PasteboardType, Data)]] {
        (pb.pasteboardItems ?? []).map { item in
            item.types.compactMap { type in item.data(forType: type).map { (type, $0) } }
        }
    }

    private static func restore(_ saved: [[(NSPasteboard.PasteboardType, Data)]], to pb: NSPasteboard) {
        pb.clearContents()
        guard !saved.isEmpty else { return }
        let items: [NSPasteboardItem] = saved.map { entries in
            let item = NSPasteboardItem()
            for (type, data) in entries { item.setData(data, forType: type) }
            return item
        }
        pb.writeObjects(items)
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
