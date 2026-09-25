import Foundation

/// The spacing and placeholder rules from Android's `VoiceAccessibilityService` companion, ported
/// line for line. Positions are UTF-16 offsets, which is what both Android and the macOS
/// Accessibility API (`AXSelectedTextRange`) count in.
public enum InsertionRules {

    /// What a field's reported text really is. `uncertain` means it could be a placeholder or real
    /// text, so the caller must insert in a way that is correct either way (paste, then tidy).
    public enum ExistingText: Equatable, Sendable { case empty, placeholder, real, uncertain }

    public static func classifyExisting(text: String, hint: String?, showingHintFlag: Bool,
                                        selectionStart: Int, selectionEnd: Int) -> ExistingText {
        if text.isEmpty { return .empty }
        if isPlaceholder(text: text, hint: hint, showingHintFlag: showingHintFlag,
                         selectionStart: selectionStart, selectionEnd: selectionEnd) { return .placeholder }
        // No caret reported, or a caret parked at the very start of non-empty text: both are what an
        // empty field showing its placeholder looks like in apps that hide the hint.
        if selectionStart < 0 || selectionEnd < 0 { return .uncertain }
        if selectionStart == 0 && selectionEnd == 0 { return .uncertain }
        return .real
    }

    /// True when `text` is the field's placeholder rather than something the user typed.
    public static func isPlaceholder(text: String, hint: String?, showingHintFlag: Bool,
                                     selectionStart: Int, selectionEnd: Int) -> Bool {
        if text.isEmpty { return false }
        if showingHintFlag { return true }
        guard let hint, !hint.isEmpty else { return false }
        return text == hint && selectionStart <= 0 && selectionEnd <= 0
    }

    /// The text to insert at `start..<end` of `existing`: trimmed, with a space in front when the
    /// caret sits right after a word and a space after when a word follows.
    public static func spacedInsertion(existing: String, start: Int, end: Int, text: String) -> String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let first = trimmed.unicodeScalars.first else { return "" }
        let units = Array(existing.utf16)
        let before: UInt16? = (start - 1 >= 0 && start - 1 < units.count) ? units[start - 1] : nil
        let after: UInt16? = (end >= 0 && end < units.count) ? units[end] : nil

        let needsLeadingSpace = before.map { !isWhitespace($0) } ?? false
            && !",.!?;:".unicodeScalars.contains(first)
        let needsTrailingSpace = after.map { !isWhitespace($0) && !",.!?".utf16.contains($0) } ?? false

        return (needsLeadingSpace ? " " : "") + trimmed + (needsTrailingSpace ? " " : "")
    }

    /// After pasting `piece` into a field whose text is now `after`, returns the text with a space
    /// added where the paste joined onto a neighbouring word, plus the caret (UTF-16) just after
    /// the piece. Nil when nothing needs changing.
    public static func respaceAfterPaste(after: String, piece: String) -> (text: String, caret: Int)? {
        let a = after as NSString
        let at = a.range(of: piece, options: .backwards)
        guard at.location != NSNotFound, !piece.isEmpty else { return nil }
        let end = at.location + at.length
        let before: UInt16? = at.location > 0 ? a.character(at: at.location - 1) : nil
        let next: UInt16? = end < a.length ? a.character(at: end) : nil
        let lead = before.map { !isWhitespace($0) } ?? false
        let trail = next.map { !isWhitespace($0) && !",.!?;:".utf16.contains($0) } ?? false
        if !lead && !trail { return nil }
        let fixed = a.substring(to: at.location) + (lead ? " " : "") + piece + (trail ? " " : "")
            + a.substring(from: end)
        return (fixed, at.location + (lead ? 1 : 0) + (piece as NSString).length)
    }

    /// `existing` with `piece` put in place of `start..<end` (UTF-16), clamped like Android does.
    public static func splice(_ existing: String, start: Int, end: Int, piece: String) -> (text: String, caret: Int) {
        let s = existing as NSString
        var lo = min(start, end), hi = max(start, end)
        lo = max(0, min(lo, s.length)); hi = max(lo, min(hi, s.length))
        return (s.substring(to: lo) + piece + s.substring(from: hi), lo + (piece as NSString).length)
    }

    /// Whether an Accessibility write really changed the field, judged from the value read back.
    /// A value that changed at all counts as landed (apps may auto-format), so a paste never
    /// doubles text the app did accept; only an unchanged value means the write was ignored.
    public static func landed(before: String, after: String?, expected: String) -> Bool {
        guard let after else { return false }
        if after == expected { return true }
        return after != before
    }

    static func isWhitespace(_ unit: UInt16) -> Bool {
        guard let scalar = Unicode.Scalar(unit) else { return false }
        return scalar.properties.isWhitespace
    }
}
