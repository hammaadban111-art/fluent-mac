import Foundation

/// The small rules that make a keyboard feel like the iPhone's own: capitals at the start of a
/// sentence, double-space for a full stop, and cautious autocorrection. Pure functions over the
/// text before the cursor, so they are unit tested; the keyboard supplies spelling guesses.
public enum TypingRules {
    public enum AutoCapitalization: Sendable { case none, words, sentences, allCharacters }

    /// Whether the next letter typed should be a capital.
    public static func shouldCapitalize(before: String?, mode: AutoCapitalization) -> Bool {
        switch mode {
        case .none: return false
        case .allCharacters: return true
        case .words:
            guard let last = before?.last else { return true }
            return last.isWhitespace
        case .sentences:
            guard let before, !before.isEmpty else { return true }
            if before.last == "\n" { return true }
            let trimmed = before.trimmingTrailingWhitespace()
            guard trimmed.count < before.count else { return false }   // must follow a space
            guard let end = trimmed.last else { return true }
            return ".!?".contains(end)
        }
    }

    /// The word being typed: the letters (and apostrophes) right before the cursor.
    public static func currentWord(before: String?) -> String {
        guard let before else { return "" }
        let word = before.reversed().prefix { $0.isLetter || $0 == "'" || $0 == "’" }
        return String(word.reversed())
    }

    /// A second space right after a word becomes ". " (return true to delete one character and
    /// insert ". " instead of a space).
    public static func isDoubleSpacePeriod(before: String?) -> Bool {
        guard let before, before.hasSuffix(" "), !before.hasSuffix("  ") else { return false }
        let prior = before.dropLast()
        guard let ch = prior.last else { return false }
        return ch.isLetter || ch.isNumber || ch == ")" || ch == "\"" || ch == "”"
    }

    /// Missing apostrophes and the lone "i", fixed whether or not the spell checker knows them.
    public static let contractions: [String: String] = [
        "i": "I", "im": "I'm", "ive": "I've", "id": "I'd", "dont": "don't", "cant": "can't",
        "isnt": "isn't", "doesnt": "doesn't", "didnt": "didn't", "wasnt": "wasn't",
        "werent": "weren't", "couldnt": "couldn't", "shouldnt": "shouldn't", "wouldnt": "wouldn't",
        "havent": "haven't", "hasnt": "hasn't", "arent": "aren't", "thats": "that's",
        "whats": "what's", "theyre": "they're", "youre": "you're", "youve": "you've",
        "theyve": "they've", "weve": "we've", "shes": "she's", "hes": "he's", "itll": "it'll",
        "youll": "you'll", "theyll": "they'll", "wouldve": "would've", "couldve": "could've",
        "shouldve": "should've", "lets": "let's", "whos": "who's", "wheres": "where's",
    ]

    /// What to replace `word` with when a space or punctuation ends it, or nil to leave it.
    /// Only near-misses are corrected (one edit, two for long words), and never a word that
    /// looks deliberate: acronyms, words with digits, or anything the checker accepts.
    public static func correction(for word: String, misspelled: Bool, guesses: [String]) -> String? {
        let lower = word.lowercased()
        if let fixed = contractions[lower] {
            // "lets" and "hes" are also real words; only fix them when the checker agrees.
            if (lower == "lets" || lower == "hes" || lower == "shes" || lower == "id") && !misspelled { return nil }
            return fixed == word ? nil : matchCase(fixed, like: word)
        }
        guard misspelled, word.count >= 2,
              !word.contains(where: \.isNumber),
              word.filter(\.isUppercase).count < 2,
              let guess = guesses.first else { return nil }
        let allowed = word.count >= 7 ? 2 : 1
        guard editDistance(lower, guess.lowercased()) <= allowed else { return nil }
        return matchCase(guess, like: word)
    }

    /// Keeps a capital first letter the user typed.
    static func matchCase(_ replacement: String, like typed: String) -> String {
        guard let first = typed.first, first.isUppercase, let r = replacement.first, r.isLowercase else {
            return replacement
        }
        return r.uppercased() + replacement.dropFirst()
    }

    /// Damerau-Levenshtein (adjacent swaps count as one edit, as in "teh").
    public static func editDistance(_ a: String, _ b: String) -> Int {
        let a = Array(a), b = Array(b)
        if a.isEmpty { return b.count }
        if b.isEmpty { return a.count }
        var d = Array(repeating: Array(repeating: 0, count: b.count + 1), count: a.count + 1)
        for i in 0...a.count { d[i][0] = i }
        for j in 0...b.count { d[0][j] = j }
        for i in 1...a.count {
            for j in 1...b.count {
                let cost = a[i - 1] == b[j - 1] ? 0 : 1
                d[i][j] = min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + cost)
                if i > 1, j > 1, a[i - 1] == b[j - 2], a[i - 2] == b[j - 1] {
                    d[i][j] = min(d[i][j], d[i - 2][j - 2] + 1)
                }
            }
        }
        return d[a.count][b.count]
    }
}
