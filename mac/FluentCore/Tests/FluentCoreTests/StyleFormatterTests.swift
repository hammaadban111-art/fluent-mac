import Testing
@testable import FluentCore

/// The Android StyleFormatterTest cases, unchanged, so both platforms format identically.
struct StyleFormatterTests {
    func fmt(_ text: String, _ style: WritingStyle, _ category: StyleCategory = .other) -> String {
        StyleFormatter.format(text, style: style, category: category)
    }

    let lunch = "Hey, are you free for lunch tomorrow? Let's do 12 if that works for you."

    @Test func formalLeavesSmartTranscriptAlone() {
        #expect(fmt(lunch, .formal, .personal) == lunch)
    }

    @Test func casualDropsIntroCommaAndFinalPeriod() {
        #expect(fmt(lunch, .casual, .personal) == "Hey are you free for lunch tomorrow? Let's do 12 if that works for you")
    }

    @Test func veryCasualLowercasesAndStripsButKeepsQuestions() {
        #expect(fmt(lunch, .veryCasual, .personal) == "hey are you free for lunch tomorrow? let's do 12 if that works for you")
    }

    @Test func veryCasualKeepsPronounAcronymsAndInTokenPunctuation() {
        #expect(fmt("I think NASA said 3.5 at 12:30 on fluent.app, and 1,000 people came.", .veryCasual)
            == "I think NASA said 3.5 at 12:30 on fluent.app and 1,000 people came")
        #expect(fmt("Ok, see you there! Wait, what?!", .veryCasual) == "ok see you there wait what?")
    }

    @Test func excitedOnlyChangesClosingFullStop() {
        #expect(fmt("So far, I am enjoying it. I am excited for tomorrow.", .excited)
            == "So far, I am enjoying it. I am excited for tomorrow!")
        #expect(fmt("Are you coming?", .excited) == "Are you coming?")
        #expect(fmt("No punctuation here", .excited) == "No punctuation here")
    }

    @Test func abbreviationsAndEllipsesKeepTheirDots() {
        #expect(fmt("See you at 5 p.m.", .casual) == "See you at 5 p.m.")
        #expect(fmt("See you at 5 p.m.", .excited) == "See you at 5 p.m.")
        #expect(fmt("Well...", .casual) == "Well...")
    }

    @Test func casualKeepsNumbersAndNamedGreetings() {
        #expect(fmt("Hi Alex, it costs 1,000 dollars.", .casual, .work) == "Hi Alex, it costs 1,000 dollars")
    }

    let email = "Hi Alex, it was great talking with you today. Looking forward to our next chat. Best, Mary."

    @Test func formalEmailIsLaidOut() {
        #expect(fmt(email, .formal, .email)
            == "Hi Alex,\n\nIt was great talking with you today. Looking forward to our next chat.\n\nBest,\nMary")
    }

    @Test func excitedEmailExclaimsBodyNotSignOff() {
        #expect(fmt(email, .excited, .email)
            == "Hi Alex,\n\nIt was great talking with you today. Looking forward to our next chat!\n\nBest,\nMary")
    }

    @Test func casualEmailKeepsGreetingInline() {
        #expect(fmt(email, .casual, .email)
            == "Hi Alex, it was great talking with you today. Looking forward to our next chat\n\nBest, Mary")
    }

    @Test func emailLayoutOnlyInEmailApps() {
        #expect(fmt(email, .formal, .other) == email)
    }

    @Test func emailWithoutGreetingOrSignOffIsNotRestructured() {
        #expect(fmt("Quarterly report attached.", .formal, .email) == "Quarterly report attached.")
        #expect(StyleFormatter.parseEmail("Hey I was wondering about the report.") == nil)
    }

    @Test func emailGreetingNeverSwallowsPronounI() {
        #expect(fmt("Hi, I wanted to follow up on Friday. Kind regards, Sam Lee.", .formal, .email)
            == "Hi,\n\nI wanted to follow up on Friday.\n\nKind regards,\nSam Lee")
    }

    @Test func existingLineBreaksSurvive() {
        #expect(fmt("First line.\n\nSecond line.", .veryCasual) == "first line\n\nsecond line")
    }

    @Test func blankInputStaysBlank() {
        #expect(fmt("   ", .casual) == "")
    }

    @Test func everyCategoryDefaultIsOneOfItsStyles() {
        for c in StyleCategory.allCases { #expect(c.styles.contains(c.defaultStyle), "\(c)") }
    }

    // Cases the Android suite does not have: non-ASCII text and curly apostrophes.
    @Test func unicodeTextSurvivesVeryCasual() {
        #expect(fmt("Café, it’s great. Ünter den Linden!", .veryCasual) == "café it’s great ünter den Linden")
        #expect(fmt("I’m here.", .veryCasual) == "I’m here")
    }
}
