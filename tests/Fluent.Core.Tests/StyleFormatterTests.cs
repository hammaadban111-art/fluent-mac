using Fluent.Core;
using Xunit;

namespace Fluent.Core.Tests;

/// <summary>The Android StyleFormatterTest cases (via the Mac suite), unchanged, so every platform
/// formats identically.</summary>
public class StyleFormatterTests
{
    static string Fmt(string text, WritingStyle style, StyleCategory category = StyleCategory.Other) =>
        StyleFormatter.Format(text, style, category);

    const string Lunch = "Hey, are you free for lunch tomorrow? Let's do 12 if that works for you.";

    [Fact] public void FormalLeavesSmartTranscriptAlone() =>
        Assert.Equal(Lunch, Fmt(Lunch, WritingStyle.Formal, StyleCategory.Personal));

    [Fact] public void CasualDropsIntroCommaAndFinalPeriod() =>
        Assert.Equal("Hey are you free for lunch tomorrow? Let's do 12 if that works for you",
            Fmt(Lunch, WritingStyle.Casual, StyleCategory.Personal));

    [Fact] public void VeryCasualLowercasesAndStripsButKeepsQuestions() =>
        Assert.Equal("hey are you free for lunch tomorrow? let's do 12 if that works for you",
            Fmt(Lunch, WritingStyle.VeryCasual, StyleCategory.Personal));

    [Fact]
    public void VeryCasualKeepsPronounAcronymsAndInTokenPunctuation()
    {
        Assert.Equal("I think NASA said 3.5 at 12:30 on fluent.app and 1,000 people came",
            Fmt("I think NASA said 3.5 at 12:30 on fluent.app, and 1,000 people came.", WritingStyle.VeryCasual));
        Assert.Equal("ok see you there wait what?", Fmt("Ok, see you there! Wait, what?!", WritingStyle.VeryCasual));
    }

    [Fact]
    public void ExcitedOnlyChangesClosingFullStop()
    {
        Assert.Equal("So far, I am enjoying it. I am excited for tomorrow!",
            Fmt("So far, I am enjoying it. I am excited for tomorrow.", WritingStyle.Excited));
        Assert.Equal("Are you coming?", Fmt("Are you coming?", WritingStyle.Excited));
        Assert.Equal("No punctuation here", Fmt("No punctuation here", WritingStyle.Excited));
    }

    [Fact]
    public void AbbreviationsAndEllipsesKeepTheirDots()
    {
        Assert.Equal("See you at 5 p.m.", Fmt("See you at 5 p.m.", WritingStyle.Casual));
        Assert.Equal("See you at 5 p.m.", Fmt("See you at 5 p.m.", WritingStyle.Excited));
        Assert.Equal("Well...", Fmt("Well...", WritingStyle.Casual));
    }

    [Fact] public void CasualKeepsNumbersAndNamedGreetings() =>
        Assert.Equal("Hi Alex, it costs 1,000 dollars", Fmt("Hi Alex, it costs 1,000 dollars.", WritingStyle.Casual, StyleCategory.Work));

    const string Email = "Hi Alex, it was great talking with you today. Looking forward to our next chat. Best, Mary.";

    [Fact] public void FormalEmailIsLaidOut() =>
        Assert.Equal("Hi Alex,\n\nIt was great talking with you today. Looking forward to our next chat.\n\nBest,\nMary",
            Fmt(Email, WritingStyle.Formal, StyleCategory.Email));

    [Fact] public void ExcitedEmailExclaimsBodyNotSignOff() =>
        Assert.Equal("Hi Alex,\n\nIt was great talking with you today. Looking forward to our next chat!\n\nBest,\nMary",
            Fmt(Email, WritingStyle.Excited, StyleCategory.Email));

    [Fact] public void CasualEmailKeepsGreetingInline() =>
        Assert.Equal("Hi Alex, it was great talking with you today. Looking forward to our next chat\n\nBest, Mary",
            Fmt(Email, WritingStyle.Casual, StyleCategory.Email));

    [Fact] public void EmailLayoutOnlyInEmailApps() => Assert.Equal(Email, Fmt(Email, WritingStyle.Formal, StyleCategory.Other));

    [Fact]
    public void EmailWithoutGreetingOrSignOffIsNotRestructured()
    {
        Assert.Equal("Quarterly report attached.", Fmt("Quarterly report attached.", WritingStyle.Formal, StyleCategory.Email));
        Assert.Null(StyleFormatter.ParseEmail("Hey I was wondering about the report."));
    }

    [Fact] public void EmailGreetingNeverSwallowsPronounI() =>
        Assert.Equal("Hi,\n\nI wanted to follow up on Friday.\n\nKind regards,\nSam Lee",
            Fmt("Hi, I wanted to follow up on Friday. Kind regards, Sam Lee.", WritingStyle.Formal, StyleCategory.Email));

    [Fact] public void ExistingLineBreaksSurvive() =>
        Assert.Equal("first line\n\nsecond line", Fmt("First line.\n\nSecond line.", WritingStyle.VeryCasual));

    [Fact] public void BlankInputStaysBlank() => Assert.Equal("", Fmt("   ", WritingStyle.Casual));

    [Fact]
    public void EveryCategoryDefaultIsOneOfItsStyles()
    {
        foreach (var c in Enum.GetValues<StyleCategory>()) Assert.Contains(c.DefaultStyle(), c.Styles());
    }

    [Fact]
    public void UnicodeTextSurvivesVeryCasual()
    {
        Assert.Equal("café it’s great ünter den Linden", Fmt("Café, it’s great. Ünter den Linden!", WritingStyle.VeryCasual));
        Assert.Equal("I’m here", Fmt("I’m here.", WritingStyle.VeryCasual));
    }

    // Windows-only cases: transcripts arrive with Windows line endings from the clipboard path.
    [Fact] public void SamplesFormatForEveryStyle()
    {
        foreach (var c in Enum.GetValues<StyleCategory>())
            foreach (var s in c.Styles())
                Assert.False(string.IsNullOrWhiteSpace(StyleFormatter.Format(c.Sample(), s, c)));
    }
}
