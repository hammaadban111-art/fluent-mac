import Foundation
import Testing
@testable import FluentCore

struct TypingRulesTests {
    typealias T = TypingRules

    @Test func capitalisesAtSentenceStarts() {
        #expect(T.shouldCapitalize(before: nil, mode: .sentences))
        #expect(T.shouldCapitalize(before: "", mode: .sentences))
        #expect(T.shouldCapitalize(before: "Done. ", mode: .sentences))
        #expect(T.shouldCapitalize(before: "Really?  ", mode: .sentences))
        #expect(T.shouldCapitalize(before: "line\n", mode: .sentences))
        #expect(!T.shouldCapitalize(before: "Done.", mode: .sentences))      // no space yet
        #expect(!T.shouldCapitalize(before: "hello ", mode: .sentences))
        #expect(!T.shouldCapitalize(before: "3.5", mode: .sentences))
        #expect(!T.shouldCapitalize(before: "", mode: .none))
        #expect(T.shouldCapitalize(before: "new ", mode: .words))
        #expect(!T.shouldCapitalize(before: "new", mode: .words))
    }

    @Test func findsTheWordBeingTyped() {
        #expect(T.currentWord(before: "Hello wor") == "wor")
        #expect(T.currentWord(before: "I dont") == "dont")
        #expect(T.currentWord(before: "it's") == "it's")
        #expect(T.currentWord(before: "end. ") == "")
        #expect(T.currentWord(before: nil) == "")
    }

    @Test func doubleSpaceMakesAFullStop() {
        #expect(T.isDoubleSpacePeriod(before: "hello "))
        #expect(T.isDoubleSpacePeriod(before: "at 12 "))
        #expect(!T.isDoubleSpacePeriod(before: "hello"))
        #expect(!T.isDoubleSpacePeriod(before: "hello. "))
        #expect(!T.isDoubleSpacePeriod(before: "hello  "))
        #expect(!T.isDoubleSpacePeriod(before: " "))
    }

    @Test func correctsOnlyNearMisses() {
        #expect(T.correction(for: "teh", misspelled: true, guesses: ["the"]) == "the")
        #expect(T.correction(for: "Teh", misspelled: true, guesses: ["the"]) == "The")
        #expect(T.correction(for: "helo", misspelled: true, guesses: ["hello"]) == "hello")
        #expect(T.correction(for: "recieved", misspelled: true, guesses: ["received"]) == "received")
        #expect(T.correction(for: "xqzv", misspelled: true, guesses: ["quiz"]) == nil)       // too far
        #expect(T.correction(for: "hello", misspelled: false, guesses: []) == nil)
        #expect(T.correction(for: "NASA", misspelled: true, guesses: ["Nasal"]) == nil)       // acronym
        #expect(T.correction(for: "b2b", misspelled: true, guesses: ["bob"]) == nil)          // digits
        #expect(T.correction(for: "a", misspelled: true, guesses: ["as"]) == nil)             // too short
    }

    @Test func fixesContractionsAndLoneI() {
        #expect(T.correction(for: "i", misspelled: false, guesses: []) == "I")
        #expect(T.correction(for: "dont", misspelled: true, guesses: []) == "don't")
        #expect(T.correction(for: "Im", misspelled: true, guesses: []) == "I'm")
        #expect(T.correction(for: "Dont", misspelled: true, guesses: []) == "Don't")
        #expect(T.correction(for: "lets", misspelled: false, guesses: []) == nil)             // real word
        #expect(T.correction(for: "I", misspelled: false, guesses: []) == nil)
    }

    @Test func editDistance() {
        #expect(T.editDistance("teh", "the") == 1)
        #expect(T.editDistance("kitten", "sitting") == 3)
        #expect(T.editDistance("", "abc") == 3)
    }
}

struct SnapshotTimerTests {
    @Test func timerRunsOnWhileRecordingAndStopsWhenPaused() {
        let t0 = Date()
        let rec = SessionSnapshot(state: .recording, elapsed: 10, heartbeat: t0)
        #expect(rec.elapsed(at: t0.addingTimeInterval(2)) == 12)
        let paused = SessionSnapshot(state: .recording, elapsed: 10, paused: true, heartbeat: t0)
        #expect(paused.elapsed(at: t0.addingTimeInterval(2)) == 10)
    }
}
