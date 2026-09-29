using Fluent.Core;
using Xunit;

namespace Fluent.Core.Tests;

public class InsertionRulesTests
{
    [Fact]
    public void SpacesAroundWords()
    {
        Assert.Equal(" world", InsertionRules.SpacedInsertion("Hello", 5, 5, "world"));
        Assert.Equal("world", InsertionRules.SpacedInsertion("Hello ", 6, 6, "world"));
        Assert.Equal("hi there", InsertionRules.SpacedInsertion("", 0, 0, "  hi there  "));
        Assert.Equal("All ", InsertionRules.SpacedInsertion("done", 0, 0, "All"));
        Assert.Equal(" X ", InsertionRules.SpacedInsertion("ab", 1, 1, "X"));
    }

    [Fact]
    public void PunctuationAndLineBreaks()
    {
        Assert.Equal(", then", InsertionRules.SpacedInsertion("ok", 2, 2, ", then"));
        Assert.Equal(" here", InsertionRules.SpacedInsertion("wait.", 4, 4, "here"));
        Assert.Equal(" x ", InsertionRules.SpacedInsertion("a;b", 1, 1, "x"));
        Assert.Equal("next", InsertionRules.SpacedInsertion("line\n", 5, 5, "next"));
        Assert.Equal("next", InsertionRules.SpacedInsertion("line\r\n", 6, 6, "next"));
        Assert.Equal("", InsertionRules.SpacedInsertion("x", 1, 1, "   "));
    }

    [Fact]
    public void RespacesAfterPaste()
    {
        var r = InsertionRules.RespaceAfterPaste("Hellohow are you", "how are you");
        Assert.Equal("Hello how are you", r?.Text);
        Assert.Equal(17, r?.Caret);
        Assert.Null(InsertionRules.RespaceAfterPaste("Hello how are you", "how are you"));
        Assert.Equal("fine. Thanks", InsertionRules.RespaceAfterPaste("fine.Thanks", "fine.")?.Text);
        Assert.Null(InsertionRules.RespaceAfterPaste("Yes,", "Yes"));
        Assert.Null(InsertionRules.RespaceAfterPaste("nothing here", "absent"));
    }

    [Fact]
    public void Landed()
    {
        Assert.True(InsertionRules.Landed("a", "a b", "a b"));
        Assert.True(InsertionRules.Landed("a", "a B", "a b"));
        Assert.False(InsertionRules.Landed("a", "a", "a b"));
        Assert.False(InsertionRules.Landed("a", null, "a b"));
    }
}

public class FieldClassifierTests
{
    [Fact]
    public void TextBoxesAreEditable()
    {
        Assert.Equal(FieldKind.Editable, FieldClassifier.Classify(new("Edit", HasValuePattern: true, ValueReadOnly: false)));
        Assert.Equal(FieldKind.Editable, FieldClassifier.Classify(new("Edit", HasTextPattern: true)));   // RichEdit, no ValuePattern
        Assert.Equal(FieldKind.Editable, FieldClassifier.Classify(new("Document", HasValuePattern: true, ValueReadOnly: false)));
        Assert.Equal(FieldKind.Editable, FieldClassifier.Classify(new("Group", HasValuePattern: true, ValueReadOnly: false)));
    }

    [Fact]
    public void PasswordsAreSecure() =>
        Assert.Equal(FieldKind.Secure, FieldClassifier.Classify(new("Edit", IsPassword: true, HasValuePattern: true, ValueReadOnly: false)));

    [Fact]
    public void EverythingElseIsNot()
    {
        Assert.Equal(FieldKind.NotEditable, FieldClassifier.Classify(new("Edit", HasValuePattern: true, ValueReadOnly: true)));
        Assert.Equal(FieldKind.NotEditable, FieldClassifier.Classify(new("Document", HasValuePattern: true, ValueReadOnly: true)));   // a web page
        Assert.Equal(FieldKind.NotEditable, FieldClassifier.Classify(new("Edit", HasValuePattern: true, ValueReadOnly: false, IsEnabled: false)));
        Assert.Equal(FieldKind.NotEditable, FieldClassifier.Classify(new("Slider", HasValuePattern: true, ValueReadOnly: false)));
        Assert.Equal(FieldKind.NotEditable, FieldClassifier.Classify(new("Button")));
        Assert.Equal(FieldKind.NotEditable, FieldClassifier.Classify(new("Group")));
        Assert.Equal(FieldKind.NotEditable, FieldClassifier.Classify(new(null)));
    }
}

public class AppCategoryTests
{
    [Fact]
    public void DesktopApps()
    {
        Assert.Equal(StyleCategory.Personal, AppCategories.Category("WhatsApp.exe"));
        Assert.Equal(StyleCategory.Personal, AppCategories.Category("Telegram"));
        Assert.Equal(StyleCategory.Work, AppCategories.Category("slack"));
        Assert.Equal(StyleCategory.Work, AppCategories.Category("ms-teams.exe"));
        Assert.Equal(StyleCategory.Email, AppCategories.Category("OUTLOOK.EXE"));
        Assert.Equal(StyleCategory.Email, AppCategories.Category("olk"));
        Assert.Equal(StyleCategory.Other, AppCategories.Category("notepad"));
        Assert.Equal(StyleCategory.Other, AppCategories.Category(null));
    }

    [Fact]
    public void WebAppsInBrowsers()
    {
        Assert.Equal(StyleCategory.Email, AppCategories.Category("chrome", "Inbox (3) - someone@gmail.com - Gmail - Google Chrome"));
        Assert.Equal(StyleCategory.Personal, AppCategories.Category("msedge", "WhatsApp - Personal - Microsoft Edge"));
        Assert.Equal(StyleCategory.Work, AppCategories.Category("firefox", "general (Channel) - Acme - Slack — Mozilla Firefox"));
        Assert.Equal(StyleCategory.Other, AppCategories.Category("chrome", "Wikipedia - Google Chrome"));
        // A title only counts in a browser.
        Assert.Equal(StyleCategory.Other, AppCategories.Category("notepad", "notes about Gmail - Notepad"));
    }

    [Fact]
    public void ListsDoNotOverlap()
    {
        Assert.Empty(AppCategories.Personal.Intersect(AppCategories.Work));
        Assert.Empty(AppCategories.Personal.Intersect(AppCategories.Email));
        Assert.Empty(AppCategories.Work.Intersect(AppCategories.Email));
    }
}

public class ShortcutTests
{
    [Fact]
    public void HoldGestureTiming()
    {
        var g = new HoldGesture();
        Assert.Equal(HoldGesture.Action.ArmTimer, g.Down(0));
        Assert.True(g.TimerFired());
        Assert.Equal(HoldGesture.Action.StopAndInsert, g.Up());

        Assert.Equal(HoldGesture.Action.ArmTimer, g.Down(0));
        Assert.Equal(HoldGesture.Action.CancelTimer, g.OtherKey());   // Ctrl+C is typing
        Assert.False(g.TimerFired());
        Assert.Equal(HoldGesture.Action.CancelTimer, g.Up());

        Assert.Equal(HoldGesture.Action.ArmTimer, g.Down(0));
        Assert.Equal(HoldGesture.Action.CancelTimer, g.Up());         // a tap
        Assert.False(g.TimerFired());

        Assert.Equal(HoldGesture.Action.ArmTimer, g.Down(0));
        Assert.True(g.TimerFired());
        Assert.Equal(HoldGesture.Action.None, g.OtherKey());          // typing while dictating is fine
        Assert.Equal(HoldGesture.Action.StopAndInsert, g.Up());
    }

    [Fact]
    public void ModifierOnlyCombosMatchTheirExactSide()
    {
        var rc = KeyCombo.RightCtrl;
        Assert.True(rc.IsDown(new HashSet<int> { KeyCombo.VkRControl }));
        Assert.False(rc.IsDown(new HashSet<int> { KeyCombo.VkLControl }));
        var ctrlWin = new KeyCombo([KeyCombo.VkLControl, KeyCombo.VkLWin], null);
        Assert.True(ctrlWin.IsDown(new HashSet<int> { KeyCombo.VkLControl, KeyCombo.VkLWin }));
        Assert.False(ctrlWin.IsDown(new HashSet<int> { KeyCombo.VkLWin }));
        Assert.True(ctrlWin.IsPart(KeyCombo.VkLWin));
        Assert.False(ctrlWin.IsPart(KeyCombo.VkRControl));
        Assert.False(KeyCombo.Off.IsDown(new HashSet<int> { KeyCombo.VkRControl }));
        Assert.True(ctrlWin.NeedsMask);
        Assert.False(rc.NeedsMask);
    }

    [Fact]
    public void CombosWithAKeyTakeEitherSideButNoExtraModifier()
    {
        var c = KeyCombo.DefaultToggle;   // Ctrl + Alt + Space
        Assert.Equal("Ctrl + Alt + Space", c.Label);
        Assert.True(c.IsDown(new HashSet<int> { KeyCombo.VkRControl, KeyCombo.VkLMenu, KeyCombo.VkSpace }));
        Assert.False(c.IsDown(new HashSet<int> { KeyCombo.VkLControl, KeyCombo.VkLMenu, KeyCombo.VkLShift, KeyCombo.VkSpace }));
        Assert.False(c.IsDown(new HashSet<int> { KeyCombo.VkLControl, KeyCombo.VkSpace }));
        var f8 = new KeyCombo([], 0x77);
        Assert.Equal("F8", f8.Label);
        Assert.True(f8.IsDown(new HashSet<int> { 0x77 }));
        Assert.False(f8.IsDown(new HashSet<int> { 0x77, KeyCombo.VkLShift }));
        Assert.Null(f8.Warning);
        Assert.NotNull(new KeyCombo([], 0x41).Warning);   // a lone letter
        Assert.Null(new KeyCombo([KeyCombo.VkLControl], 0x41).Warning);
    }

    [Fact]
    public void SidesDoNotMatterWithAnOrdinaryKey()
    {
        Assert.Equal(new KeyCombo([KeyCombo.VkLControl], KeyCombo.VkSpace), new KeyCombo([KeyCombo.VkRControl], KeyCombo.VkSpace));
        Assert.NotEqual(KeyCombo.RightCtrl, new KeyCombo([KeyCombo.VkLControl], null));
    }

    [Fact]
    public void CombosRoundTripThroughTheirCode()
    {
        foreach (var c in new[] { KeyCombo.RightCtrl, KeyCombo.DefaultToggle, new KeyCombo([KeyCombo.VkRMenu, KeyCombo.VkLShift], 0x14), KeyCombo.Off })
            Assert.Equal(c, KeyCombo.Parse(c.Code));
        Assert.Equal("A3", KeyCombo.RightCtrl.Code);
        Assert.Null(KeyCombo.Parse("nonsense"));
        Assert.Null(KeyCombo.Parse("41+42"));   // two ordinary keys
        Assert.Null(KeyCombo.Parse(null));
        Assert.Equal("Left Ctrl + Left Win", new KeyCombo([KeyCombo.VkLWin, KeyCombo.VkLControl], null).Label);
    }

    [Fact]
    public void LegacySettingsStillLoad()
    {
        Assert.Equal(KeyCombo.RightCtrl, KeyCombo.FromLegacyHold("RightCtrl"));
        Assert.Equal(new KeyCombo([KeyCombo.VkRMenu], null), KeyCombo.FromLegacyHold("RightAlt"));
        Assert.Equal(KeyCombo.Off, KeyCombo.FromLegacyHold("Off"));
        Assert.Equal(KeyCombo.DefaultToggle, KeyCombo.FromLegacyToggle("Ctrl + Alt + Space"));
        Assert.Equal("Alt + Space", KeyCombo.FromLegacyToggle("Alt + Space").Label);
        Assert.Equal(KeyCombo.Off, KeyCombo.FromLegacyToggle("Off"));
        Assert.Equal(KeyCombo.DefaultToggle, KeyCombo.FromLegacyToggle(null));
    }

    [Fact]
    public void RecorderCapturesCombos()
    {
        var r = new KeyComboRecorder();
        Assert.Equal(KeyComboRecorder.Result.Continue, r.Key(KeyCombo.VkLControl, true));
        Assert.Equal(KeyComboRecorder.Result.Continue, r.Key(KeyCombo.VkLMenu, true));
        Assert.Equal(KeyComboRecorder.Result.Done, r.Key(0x44, true));
        Assert.Equal("Ctrl + Alt + D", r.Combo!.Label);

        r = new KeyComboRecorder();   // modifiers only: done when all are let go
        r.Key(KeyCombo.VkRControl, true);
        r.Key(KeyCombo.VkRWin, true);
        Assert.Equal(KeyComboRecorder.Result.Continue, r.Key(KeyCombo.VkRWin, false));
        Assert.Equal(KeyComboRecorder.Result.Done, r.Key(KeyCombo.VkRControl, false));
        Assert.Equal("Right Ctrl + Right Win", r.Combo!.Label);

        r = new KeyComboRecorder();
        Assert.Equal(KeyComboRecorder.Result.Cancelled, r.Key(KeyCombo.VkEscape, true));
        r = new KeyComboRecorder();
        Assert.Equal(KeyComboRecorder.Result.Done, r.Key(0x14, true));   // Caps Lock alone
        Assert.Equal("Caps Lock", r.Combo!.Label);
    }
}

public class KeyRecordingSessionTests
{
    static KeyCombo? Record(params (int vk, bool down)[] keys)
    {
        var s = new KeyRecordingSession();
        s.Start();
        KeyCombo? got = null;
        foreach (var (vk, down) in keys)
        {
            var (swallow, result, combo) = s.Key(vk, down);
            Assert.True(swallow);
            if (result == KeyComboRecorder.Result.Done) got = combo;
        }
        return got;
    }

    [Fact]
    public void AModifierOnItsOwnRecordsWhenLetGo()
    {
        Assert.Equal("Right Ctrl", Record((KeyCombo.VkRControl, true), (KeyCombo.VkRControl, true), (KeyCombo.VkRControl, false))?.Label);
        Assert.Equal("Left Ctrl", Record((KeyCombo.VkLControl, true), (KeyCombo.VkLControl, false))?.Label);
        Assert.Equal("Left Shift", Record((KeyCombo.VkLShift, true), (KeyCombo.VkLShift, false))?.Label);
        Assert.Equal("Right Alt", Record((KeyCombo.VkRMenu, true), (KeyCombo.VkRMenu, false))?.Label);
        Assert.Equal("Left Ctrl + Left Win", Record((KeyCombo.VkLControl, true), (KeyCombo.VkLWin, true),
            (KeyCombo.VkLWin, false), (KeyCombo.VkLControl, false))?.Label);
    }

    [Fact]
    public void ModifiersPlusAKeyRecordOnTheKeyAndTheRestIsSwallowed()
    {
        var s = new KeyRecordingSession();
        s.Start();
        s.Key(KeyCombo.VkLControl, true);
        var (_, result, combo) = s.Key(0x4C, true);   // L
        Assert.Equal(KeyComboRecorder.Result.Done, result);
        Assert.Equal("Ctrl + L", combo!.Label);
        Assert.False(s.Recording);
        Assert.True(s.Key(0x4C, true).Swallow);             // auto-repeat: never reaches the app or the shortcuts
        Assert.True(s.Key(0x4C, false).Swallow);
        Assert.True(s.Key(KeyCombo.VkLControl, false).Swallow);
        Assert.False(s.Key(0x4C, true).Swallow);             // afterwards keys are the user's again
    }

    [Fact]
    public void EscCancelsAndALoneLetterRecords()
    {
        var s = new KeyRecordingSession();
        s.Start();
        Assert.Equal(KeyComboRecorder.Result.Cancelled, s.Key(KeyCombo.VkEscape, true).Result);
        Assert.True(s.Key(KeyCombo.VkEscape, false).Swallow);
        Assert.Equal("A", Record((0x41, true))?.Label);
    }

    [Fact]
    public void NothingIsSwallowedWhenNotRecording()
    {
        var s = new KeyRecordingSession();
        Assert.False(s.Key(KeyCombo.VkRControl, true).Swallow);
        Assert.False(s.Key(KeyCombo.VkRControl, false).Swallow);
    }
}

public class HotkeyEngineTests
{
    static HotkeyEngine Engine(KeyCombo hold, KeyCombo toggle) => new() { Hold = hold, Toggle = toggle };

    [Fact]
    public void HoldingRightCtrlTalksAndPassesTheKeyThrough()
    {
        var e = Engine(KeyCombo.RightCtrl, KeyCombo.DefaultToggle);
        var (swallow, s) = e.Key(KeyCombo.VkRControl, true, 0, false);
        Assert.False(swallow);
        Assert.Equal([HotkeyEngine.Signal.ArmHoldTimer], s);
        Assert.True(e.HoldTimerFired());
        (swallow, s) = e.Key(KeyCombo.VkRControl, true, 0.3, true);   // auto-repeat
        Assert.Empty(s);
        (swallow, s) = e.Key(KeyCombo.VkRControl, false, 2, true);
        Assert.False(swallow);
        Assert.Equal([HotkeyEngine.Signal.StopHold], s);
    }

    [Fact]
    public void CtrlCIsNotAHold()
    {
        var e = Engine(KeyCombo.RightCtrl, KeyCombo.Off);
        e.Key(KeyCombo.VkRControl, true, 0, false);
        var (_, s) = e.Key(0x43, true, 0.1, false);
        Assert.Equal([HotkeyEngine.Signal.CancelHoldTimer], s);
        Assert.False(e.HoldTimerFired());
    }

    [Fact]
    public void AnOrdinaryHoldKeyIsSwallowedDownRepeatAndUp()
    {
        var f8 = new KeyCombo([], 0x77);
        var e = Engine(f8, KeyCombo.Off);
        Assert.True(e.Key(0x77, true, 0, false).Swallow);
        Assert.True(e.HoldTimerFired());
        Assert.True(e.Key(0x77, true, 0.4, true).Swallow);
        var (swallow, s) = e.Key(0x77, false, 1, true);
        Assert.True(swallow);
        Assert.Equal([HotkeyEngine.Signal.StopHold], s);
        Assert.False(e.Key(0x41, true, 2, false).Swallow);   // other keys untouched
    }

    [Fact]
    public void ToggleWithAKeyFiresOnceAndSwallowsTheKey()
    {
        var e = Engine(KeyCombo.RightCtrl, KeyCombo.DefaultToggle);
        e.Key(KeyCombo.VkLControl, true, 0, false);
        e.Key(KeyCombo.VkLMenu, true, 0.01, false);
        var (swallow, s) = e.Key(KeyCombo.VkSpace, true, 0.02, false);
        Assert.True(swallow);
        Assert.Equal([HotkeyEngine.Signal.Toggle, HotkeyEngine.Signal.Mask], s);
        Assert.True(e.Key(KeyCombo.VkSpace, true, 0.5, true).Swallow);   // repeat: no second toggle
        Assert.Empty(e.Key(KeyCombo.VkSpace, true, 0.6, true).Signals);
        Assert.True(e.Key(KeyCombo.VkSpace, false, 0.7, true).Swallow);
        Assert.False(e.Key(KeyCombo.VkLMenu, false, 0.8, true).Swallow);   // modifiers always pass
        e.Key(KeyCombo.VkLControl, false, 0.8, true);
        Assert.False(e.Key(KeyCombo.VkSpace, true, 1, false).Swallow);   // Space alone types a space
    }

    [Fact]
    public void ModifierOnlyToggleFiresOnAQuickCleanTap()
    {
        var e = Engine(KeyCombo.Off, new KeyCombo([KeyCombo.VkRMenu], null));
        e.Key(KeyCombo.VkRMenu, true, 0, false);
        Assert.Contains(HotkeyEngine.Signal.Toggle, e.Key(KeyCombo.VkRMenu, false, 0.2, false).Signals);
        // Used with another key (AltGr + e): not a tap.
        e.Key(KeyCombo.VkRMenu, true, 1, false);
        e.Key(0x45, true, 1.1, false);
        e.Key(0x45, false, 1.15, false);
        Assert.DoesNotContain(HotkeyEngine.Signal.Toggle, e.Key(KeyCombo.VkRMenu, false, 1.2, false).Signals);
        // Held too long: not a tap.
        e.Key(KeyCombo.VkRMenu, true, 2, false);
        Assert.DoesNotContain(HotkeyEngine.Signal.Toggle, e.Key(KeyCombo.VkRMenu, false, 3, false).Signals);
    }

    [Fact]
    public void SameModifiersForBothTapTogglesHoldTalks()
    {
        var e = Engine(KeyCombo.RightCtrl, KeyCombo.RightCtrl);
        e.Key(KeyCombo.VkRControl, true, 0, false);
        var s = e.Key(KeyCombo.VkRControl, false, 0.1, false).Signals;
        Assert.Contains(HotkeyEngine.Signal.Toggle, s);
        Assert.Contains(HotkeyEngine.Signal.CancelHoldTimer, s);

        e.Key(KeyCombo.VkRControl, true, 1, false);
        Assert.True(e.HoldTimerFired());
        s = e.Key(KeyCombo.VkRControl, false, 3, true).Signals;
        Assert.Equal([HotkeyEngine.Signal.StopHold], s);
    }

    [Fact]
    public void EscCancelsOnlyWhileDictatingAndIsSwallowedThen()
    {
        var e = Engine(KeyCombo.RightCtrl, KeyCombo.DefaultToggle);
        var (swallow, s) = e.Key(KeyCombo.VkEscape, true, 0, true);
        Assert.True(swallow);
        Assert.Equal([HotkeyEngine.Signal.Cancel], s);
        Assert.True(e.Key(KeyCombo.VkEscape, false, 0.1, true).Swallow);
        (swallow, s) = e.Key(KeyCombo.VkEscape, true, 1, false);
        Assert.False(swallow);
        Assert.Empty(s);
    }

    [Fact]
    public void ShiftThenRightCtrlIsTextSelectionNotAHold()
    {
        var e = Engine(KeyCombo.RightCtrl, KeyCombo.Off);
        e.Key(KeyCombo.VkLShift, true, 0, false);
        Assert.Empty(e.Key(KeyCombo.VkRControl, true, 0.1, false).Signals);
        Assert.False(e.HoldTimerFired());
    }

    [Fact]
    public void InactiveEngineOnlyTracksKeys()
    {
        var e = Engine(new KeyCombo([], 0x77), KeyCombo.DefaultToggle);
        e.Active = false;
        var (swallow, s) = e.Key(0x77, true, 0, false);
        Assert.False(swallow);
        Assert.Empty(s);
    }
}

public class TranscriptRaceTests
{
    static Func<Task<TranscriptionResult>> Batch(string? text, int ms, List<string> calls) => async () =>
    {
        calls.Add("batch");
        await Task.Delay(ms);
        return text is null ? TranscriptionResult.Fail(TranscriptionError.ConnectionLost) : TranscriptionResult.Ok(text);
    };

    static async Task<string?> Live(string? text, int ms) { await Task.Delay(ms); return text; }

    [Fact]
    public async Task FastLiveWinsWithoutBatch()
    {
        var calls = new List<string>();
        var (r, route) = await TranscriptRace.RunAsync(Live("hello", 20), Batch("batch", 10, calls), TimeSpan.FromMilliseconds(300));
        Assert.Equal(("hello", "live"), (r.Text, route));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task FailedLiveFallsBackToBatch()
    {
        var calls = new List<string>();
        var (r, route) = await TranscriptRace.RunAsync(Live(null, 10), Batch("batch", 10, calls), TimeSpan.FromMilliseconds(300));
        Assert.Equal(("batch", "batch"), (r.Text, route));
    }

    [Fact]
    public async Task SlowLiveStartsBatchAndTheFirstAnswerWins()
    {
        var calls = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (r, route) = await TranscriptRace.RunAsync(Live("late", 3000), Batch("batch", 100, calls), TimeSpan.FromMilliseconds(200));
        Assert.Equal("batch", r.Text);
        Assert.StartsWith("batch", route);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"{sw.ElapsedMilliseconds} ms");

        (r, route) = await TranscriptRace.RunAsync(Live("live", 400), Batch("batch", 3000, calls), TimeSpan.FromMilliseconds(200));
        Assert.Equal(("live", "live"), (r.Text, route));
    }

    [Fact]
    public async Task AFaultedLiveTaskFallsBackToBatch()
    {
        var calls = new List<string>();
        static async Task<string?> Boom(int ms) { await Task.Delay(ms); throw new InvalidOperationException("socket"); }
        var (r, route) = await TranscriptRace.RunAsync(Boom(10), Batch("batch", 10, calls), TimeSpan.FromMilliseconds(300));
        Assert.Equal(("batch", "batch"), (r.Text, route));
        (r, route) = await TranscriptRace.RunAsync(Boom(500), Batch("slow batch", 100, calls), TimeSpan.FromMilliseconds(50));
        Assert.Equal("slow batch", r.Text);
        (r, _) = await TranscriptRace.RunAsync(Boom(500), Batch(null, 50, calls), TimeSpan.FromMilliseconds(50));
        Assert.False(r.IsSuccess);
    }

    [Fact]
    public async Task BatchErrorWaitsForALateLiveAnswer()
    {
        var calls = new List<string>();
        var (r, route) = await TranscriptRace.RunAsync(Live("late", 600), Batch(null, 50, calls), TimeSpan.FromMilliseconds(200));
        Assert.Equal(("late", "live"), (r.Text, route));
        (r, _) = await TranscriptRace.RunAsync(Live(null, 600), Batch(null, 50, calls), TimeSpan.FromMilliseconds(200));
        Assert.False(r.IsSuccess);
    }
}

public class PlacementTests
{
    static readonly BubblePlacement.Rect Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void OneLineFieldGetsBubbleOnTheRight()
    {
        var (x, y) = BubblePlacement.Origin(new(100, 100, 400, 30), 58, Screen);
        Assert.Equal(508, x);
        Assert.Equal(86, y);
    }

    [Fact]
    public void TallFieldGetsBubbleInsideCorner()
    {
        var (x, y) = BubblePlacement.Origin(new(100, 100, 800, 600), 58, Screen);
        Assert.Equal(828, x);
        Assert.Equal(628, y);
    }

    [Fact]
    public void BubbleStaysOnScreen()
    {
        var (x, _) = BubblePlacement.Origin(new(1500, 100, 410, 30), 58, Screen);
        Assert.True(x + 58 <= 1916);
        var (dx, dy) = BubblePlacement.Origin(new(100, 100, 400, 30), 58, Screen, (-5000, 9000));
        Assert.Equal(4, dx);
        Assert.Equal(1040 - 58 - 4, dy);
    }

    [Fact]
    public void SecondMonitorToTheLeft()
    {
        var left = new BubblePlacement.Rect(-1920, 0, 1920, 1080);
        var (x, _) = BubblePlacement.Origin(new(-1000, 200, 300, 30), 58, left);
        Assert.Equal(-1000 + 300 + 8, x);
    }
}
