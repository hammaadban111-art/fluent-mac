import Foundation

/// How a finished transcript is dressed before it is inserted. A style only ever changes
/// capitals, punctuation and (for email) line layout. The words themselves are never rewritten.
public enum WritingStyle: String, CaseIterable, Codable, Sendable {
    case formal = "FORMAL"
    case casual = "CASUAL"
    case veryCasual = "VERY_CASUAL"
    case excited = "EXCITED"

    public var title: String {
        switch self {
        case .formal: "Formal."
        case .casual: "Casual"
        case .veryCasual: "very casual"
        case .excited: "Excited!"
        }
    }

    public var rule: String {
        switch self {
        case .formal: "Caps + Punctuation"
        case .casual: "Caps + Less punctuation"
        case .veryCasual: "No caps + No punctuation"
        case .excited: "Caps + Exclamation marks"
        }
    }
}

/// Where the user is writing. On iOS a keyboard cannot see which app it is typing into, so the
/// category is picked on the keyboard itself and remembered, instead of being read from the app.
public enum StyleCategory: String, CaseIterable, Codable, Sendable {
    case personal = "PERSONAL"
    case work = "WORK"
    case email = "EMAIL"
    case other = "OTHER"

    public var label: String {
        switch self {
        case .personal: "Personal"
        case .work: "Work"
        case .email: "Email"
        case .other: "Other"
        }
    }

    /// Finishes "How do you write your …?"
    public var question: String {
        switch self {
        case .personal: "personal messages"
        case .work: "work messages"
        case .email: "email"
        case .other: "other"
        }
    }

    public var appliesIn: String {
        switch self {
        case .personal: "Use this for WhatsApp, Messages, Telegram and friends"
        case .work: "Use this for Slack, Teams and other work chat"
        case .email: "Use this for Mail, Gmail and Outlook"
        case .other: "Use this everywhere else"
        }
    }

    public var styles: [WritingStyle] {
        switch self {
        case .personal, .work: [.formal, .casual, .veryCasual]
        case .email, .other: [.formal, .casual, .excited]
        }
    }

    public var defaultStyle: WritingStyle {
        self == .personal ? .casual : .formal
    }

    /// A typical smart-mode transcript, run through the real formatter for the previews.
    public var sample: String {
        switch self {
        case .personal: "Hey, are you free for lunch tomorrow? Let's do 12 if that works for you."
        case .work: "Hey, if you're free, let's chat about the great results."
        case .email: "Hi Alex, it was great talking with you today. Looking forward to our next chat. Best, Mary."
        case .other: "So far, I am enjoying the new workout routine. I am excited for tomorrow's workout, especially after a full night of rest."
        }
    }
}

/// Pure text transforms behind `WritingStyle`. A line-for-line port of the Android
/// `StyleFormatter`, checked by the same test cases.
public enum StyleFormatter {

    public static func format(_ text: String, style: WritingStyle, category: StyleCategory) -> String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return trimmed }
        if category == .email, style != .veryCasual, !trimmed.contains("\n"),
           let parts = parseEmail(trimmed) {
            return formatEmail(parts, style: style)
        }
        return styleBody(trimmed, style: style)
    }

    /// Applies `style` line by line so any line breaks the transcript already has survive.
    public static func styleBody(_ text: String, style: WritingStyle) -> String {
        text.split(separator: "\n", omittingEmptySubsequences: false).map { raw in
            let line = String(raw)
            return line.trimmingCharacters(in: .whitespaces).isEmpty
                ? line
                : styleLine(line.trimmingCharacters(in: .whitespaces), style: style)
        }.joined(separator: "\n")
    }

    private static func styleLine(_ line: String, style: WritingStyle) -> String {
        switch style {
        case .formal: line
        case .casual: dropFinalPeriod(dropIntroCommas(line))
        case .veryCasual: stripPunctuation(lowercaseSentenceStarts(line))
        case .excited: exclaimFinal(line)
        }
    }

    // MARK: casual

    private static let introComma = regex(
        "(^|[.!?]\\s+)(Hey|Hi|Hello|So|Well|Okay|OK|Ok|Oh|Yeah|Yes|No|Alright|Anyway|Also|" +
            "Honestly|Actually|Basically|Plus|Um|Uh|Right|Sure),(?=\\s)",
        caseInsensitive: true
    )

    /// "Hey, are you free" becomes "Hey are you free"; "Hi Alex," is left alone.
    private static func dropIntroCommas(_ line: String) -> String {
        replace(introComma, in: line, with: "$1$2")
    }

    /// Dotted abbreviations such as "p.m." or "U.S." keep their final dot.
    private static let dottedAbbreviation = regex("(?:\\b[A-Za-z]\\.){2,}$")

    private static func dropFinalPeriod(_ line: String) -> String {
        if !line.hasSuffix(".") || line.hasSuffix("..") { return line }
        if matches(dottedAbbreviation, line) { return line }
        return String(line.dropLast()).trimmingTrailingWhitespace()
    }

    // MARK: very casual

    private static func lowercaseSentenceStarts(_ line: String) -> String {
        var out = Array(line)
        var atStart = true
        var i = 0
        while i < out.count {
            let ch = out[i]
            if atStart && ch.isLetter {
                var end = i
                while end < out.count && (out[end].isLetter || out[end].isNumber || out[end] == "'" || out[end] == "’") {
                    end += 1
                }
                let word = String(out[i..<end])
                if !keepsCapital(word) { out[i] = Character(ch.lowercased()) }
                atStart = false
                i = end
                continue
            }
            if ch == "." || ch == "!" || ch == "?" {
                atStart = i + 1 >= out.count || out[i + 1].isWhitespace
            } else if !ch.isWhitespace && !"\"'“‘(".contains(ch) {
                atStart = false
            }
            i += 1
        }
        return String(out)
    }

    /// "I", "I'm" and acronyms like "NASA" or "OK" stay as they are.
    private static func keepsCapital(_ word: String) -> Bool {
        if word == "I" || word.hasPrefix("I'") || word.hasPrefix("I’") { return true }
        return word.filter(\.isUppercase).count >= 2
    }

    /// Drops sentence punctuation but keeps question marks, apostrophes, and anything inside a
    /// token such as "12:30", "3.5", "1,000" or "fluent.app".
    private static let looseMarks = regex("(?<=\\S)[.,;:!…]+(?=\\s|$)")
    private static let doubleSpace = regex(" {2,}")

    private static func stripPunctuation(_ line: String) -> String {
        replace(doubleSpace, in: replace(looseMarks, in: line, with: ""), with: " ")
            .trimmingCharacters(in: .whitespaces)
    }

    // MARK: excited

    /// Turns the closing full stop into an exclamation mark. Questions stay questions.
    private static func exclaimFinal(_ line: String) -> String {
        if !line.hasSuffix(".") || line.hasSuffix("..") { return line }
        if matches(dottedAbbreviation, line) { return line }
        return String(line.dropLast()) + "!"
    }

    // MARK: email

    public struct EmailParts: Equatable, Sendable {
        public var greeting: String?
        public var body: String
        public var closing: String?
        public var name: String?
    }

    private static let nameWord = "(?!I\\b)[A-Z][\\w'’-]*\\.?"

    private static let greetingRegex = regex(
        "^((?:Hi|Hello|Hey|Dear|Hiya|Greetings|Good (?:morning|afternoon|evening))" +
            "(?:\\s+(?:there|all|everyone|team|folks|guys|\(nameWord))){0,3})\\s*[,.!:]\\s+"
    )

    private static let closingRegex = regex(
        "(?<=[.!?])\\s+(Best regards|Best wishes|Kind regards|Warm regards|Regards|Many thanks|" +
            "Thanks again|Thank you|Thanks|All the best|Yours sincerely|Yours truly|Yours faithfully|" +
            "Sincerely|Cheers|Talk soon|Take care|Warmly|Best)\\s*[,.!]?\\s+" +
            "(\(nameWord)(?:\\s+\(nameWord)){0,2})\\s*[.!]?$"
    )

    /// Splits a one-paragraph email into greeting, body and sign-off. Nil when it has neither.
    public static func parseEmail(_ text: String) -> EmailParts? {
        var rest = text
        var greeting: String?
        if let m = firstMatch(greetingRegex, rest) {
            greeting = group(m, 1, in: rest).trimmingTrailing(".")
            rest = String(rest[Range(m.range, in: rest)!.upperBound...])
        }
        var closing: String?
        var name: String?
        if let m = firstMatch(closingRegex, rest) {
            closing = group(m, 1, in: rest)
            name = group(m, 2, in: rest).trimmingTrailing(".")
            rest = String(rest[..<Range(m.range, in: rest)!.lowerBound])
        }
        if greeting == nil && closing == nil { return nil }
        return EmailParts(greeting: greeting, body: rest.trimmingCharacters(in: .whitespacesAndNewlines),
                          closing: closing, name: name)
    }

    private static func formatEmail(_ parts: EmailParts, style: WritingStyle) -> String {
        let body = styleBody(parts.body, style: style)
        if style == .casual {
            let opening: String
            if let greeting = parts.greeting {
                opening = body.isEmpty ? "\(greeting)," : "\(greeting), \(lowercaseFirst(body))"
            } else {
                opening = body
            }
            let signOff = parts.closing.map { "\($0), \(parts.name ?? "")" }
            return [opening.isEmpty ? nil : opening, signOff].compactMap { $0 }.joined(separator: "\n\n")
        }
        let capitalized = capitalizeFirst(body)
        return [
            parts.greeting.map { "\($0)," },
            capitalized.isEmpty ? nil : capitalized,
            parts.closing.map { "\($0),\n\(parts.name ?? "")" },
        ].compactMap { $0 }.joined(separator: "\n\n")
    }

    private static func capitalizeFirst(_ s: String) -> String {
        guard let first = s.first else { return s }
        return first.uppercased() + s.dropFirst()
    }

    private static func lowercaseFirst(_ s: String) -> String {
        guard let first = s.first else { return s }
        let word = String(s.prefix { !$0.isWhitespace })
            .trimmingCharacters(in: CharacterSet(charactersIn: ",.!?"))
        return keepsCapital(word) ? s : first.lowercased() + s.dropFirst()
    }

    // MARK: regex helpers (NSRegularExpression, because Swift Regex has no lookbehind)

    private static func regex(_ pattern: String, caseInsensitive: Bool = false) -> NSRegularExpression {
        // The patterns are constants, so a failure here is a programming error caught by the tests.
        try! NSRegularExpression(pattern: pattern, options: caseInsensitive ? [.caseInsensitive] : [])
    }

    private static func fullRange(_ s: String) -> NSRange { NSRange(s.startIndex..., in: s) }

    private static func replace(_ re: NSRegularExpression, in s: String, with template: String) -> String {
        re.stringByReplacingMatches(in: s, range: fullRange(s), withTemplate: template)
    }

    private static func matches(_ re: NSRegularExpression, _ s: String) -> Bool {
        re.firstMatch(in: s, range: fullRange(s)) != nil
    }

    private static func firstMatch(_ re: NSRegularExpression, _ s: String) -> NSTextCheckingResult? {
        re.firstMatch(in: s, range: fullRange(s))
    }

    private static func group(_ m: NSTextCheckingResult, _ i: Int, in s: String) -> String {
        guard let r = Range(m.range(at: i), in: s) else { return "" }
        return String(s[r])
    }
}

extension String {
    func trimmingTrailingWhitespace() -> String {
        var s = self
        while let last = s.last, last.isWhitespace { s.removeLast() }
        return s
    }

    func trimmingTrailing(_ ch: Character) -> String {
        var s = self
        while s.last == ch { s.removeLast() }
        return s
    }
}
