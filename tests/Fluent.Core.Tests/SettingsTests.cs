using Fluent.Core;
using Xunit;

namespace Fluent.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void DefaultsMatchTheMacAndAndroid()
    {
        var s = new Settings(new MemoryStore());
        Assert.False(s.SetupComplete);
        Assert.Equal(TranscriptionMode.Smart, s.Mode);
        Assert.Equal(Languages.Auto, s.LanguageCode);
        Assert.Empty(s.LanguageCodes);
        Assert.False(s.HistoryEnabled);
        Assert.Equal("aurora", s.ThemeId);
        Assert.True(s.StyleEnabled);
        Assert.True(s.BubbleEnabled);
        Assert.Equal(42, s.BubbleSize);
        Assert.Equal(WritingStyle.Casual, s.StyleFor(StyleCategory.Personal));
        Assert.Equal(WritingStyle.Formal, s.StyleFor(StyleCategory.Email));
        Assert.Equal(HoldKey.RightCtrl, s.HoldKey);
        Assert.Equal(ToggleShortcut.Default, s.ToggleShortcut);
        Assert.Equal(Settings.MatchApp, s.OverlayThemeId);
        Assert.False(s.TermsAccepted);
    }

    [Fact]
    public void ValuesPersistThroughTheJsonFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fluent-test-" + Guid.NewGuid());
        var file = Path.Combine(dir, "settings.json");
        var s = new Settings(new JsonFileStore(file))
        {
            Mode = TranscriptionMode.Verbatim, LanguageCode = "hi-IN", Vocabulary = ["Hammaad", "Fluent"],
            ThemeId = "ember", BubbleSize = 99, HoldKey = HoldKey.CtrlWin,
            ToggleShortcut = ToggleShortcut.Presets[2], ExcludedApps = ["KeePass.exe", "keepass"],
        };
        s.SetStyle(WritingStyle.VeryCasual, StyleCategory.Work);
        s.AcceptTerms();

        var t = new Settings(new JsonFileStore(file));
        Assert.Equal(TranscriptionMode.Verbatim, t.Mode);
        Assert.Equal(["hi-IN"], t.LanguageCodes);
        Assert.Equal(["Hammaad", "Fluent"], t.Vocabulary);
        Assert.Equal("ember", t.ThemeId);
        Assert.Equal(60, t.BubbleSize);   // clamped
        Assert.Equal(HoldKey.CtrlWin, t.HoldKey);
        Assert.Equal(ToggleShortcut.Presets[2], t.ToggleShortcut);
        Assert.Equal(["keepass"], t.ExcludedApps);
        Assert.True(t.IsExcluded("KEEPASS.EXE"));
        Assert.Equal(WritingStyle.VeryCasual, t.StyleFor(StyleCategory.Work));
        Assert.True(t.TermsAccepted);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        var file = Path.Combine(Path.GetTempPath(), "fluent-bad-" + Guid.NewGuid() + ".json");
        File.WriteAllText(file, "{ not json");
        Assert.Equal("aurora", new Settings(new JsonFileStore(file)).ThemeId);
        File.Delete(file);
    }

    [Fact]
    public void AStyleFromTheWrongCategoryIsIgnored()
    {
        var s = new Settings(new MemoryStore());
        s.SetStyle(WritingStyle.Excited, StyleCategory.Personal);   // Personal has no Excited
        Assert.Equal(WritingStyle.Casual, s.StyleFor(StyleCategory.Personal));
    }

    [Fact]
    public void StyleAppliesInSmartModeOnly()
    {
        var s = new Settings(new MemoryStore());
        const string raw = "Hey, the build is green. Ship it!";
        Assert.Equal("hey the build is green ship it", ApplyVeryCasual(s, raw));
        s.Mode = TranscriptionMode.Verbatim;
        Assert.Equal(raw, s.ApplyStyle(raw, StyleCategory.Work));
        s.Mode = TranscriptionMode.Smart;
        s.StyleEnabled = false;
        Assert.Equal(raw, s.ApplyStyle(raw, StyleCategory.Work));
    }

    static string ApplyVeryCasual(Settings s, string raw)
    {
        s.SetStyle(WritingStyle.VeryCasual, StyleCategory.Work);
        return s.ApplyStyle(raw, StyleCategory.Work);
    }

    [Fact]
    public void SnoozeAndBubbleRules()
    {
        var s = new Settings(new MemoryStore());
        var now = DateTimeOffset.UtcNow;
        Assert.True(s.BubbleAllowed(true, FieldKind.Editable, "notepad", now));
        Assert.False(s.BubbleAllowed(false, FieldKind.Editable, "notepad", now));
        Assert.False(s.BubbleAllowed(true, FieldKind.Secure, "notepad", now));
        Assert.False(s.BubbleAllowed(true, FieldKind.Editable, "Fluent", now));
        s.ExcludedApps = ["notepad"];
        Assert.False(s.BubbleAllowed(true, FieldKind.Editable, "Notepad.exe", now));
        s.ExcludedApps = [];

        s.Snooze(SnoozeChoice.Fifteen, now);
        Assert.True(s.SnoozeActive(now.AddMinutes(14)));
        Assert.False(s.SnoozeActive(now.AddMinutes(16)));
        Assert.False(s.BubbleAllowed(true, FieldKind.Editable, "notepad", now));

        s.Snooze(SnoozeChoice.UntilRestart, now);
        Assert.True(s.SnoozeActive(now.AddDays(3)));
        s.ClearRestartSnooze();
        Assert.False(s.SnoozeActive(now));
    }

    [Fact]
    public void HistoryKeepsNewestFirstAndSurvivesReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fluent-hist-" + Guid.NewGuid());
        var h = new HistoryStore(dir);
        var a = Transcript.New("first one", 1);
        var b = Transcript.New("second one here", 2);
        h.Add(a); h.Add(b);
        var all = new HistoryStore(dir).All();
        Assert.Equal([b.Id, a.Id], all.Select(t => t.Id));
        Assert.Equal(3, all[0].WordCount);
        h.Delete(b.Id);
        Assert.Single(h.All());
        h.Clear();
        Assert.Empty(h.All());
        Directory.Delete(dir, true);
    }

    [Fact]
    public void WavRoundTrip()
    {
        short[] pcm = [0, 1, -1, short.MaxValue, short.MinValue, 1234];
        var wav = Wav.Encode(pcm);
        Assert.Equal(44 + 12, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(pcm, Wav.DecodePcm16(wav));
    }
}
