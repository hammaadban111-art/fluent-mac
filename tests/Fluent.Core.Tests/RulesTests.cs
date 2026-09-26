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
    public void HoldKeys()
    {
        Assert.True(HoldKey.RightCtrl.IsDown(new HashSet<int> { HoldKeyInfo.VkRControl }));
        Assert.False(HoldKey.RightCtrl.IsDown(new HashSet<int> { HoldKeyInfo.VkLControl }));
        Assert.True(HoldKey.CtrlWin.IsDown(new HashSet<int> { HoldKeyInfo.VkLControl, HoldKeyInfo.VkLWin }));
        Assert.False(HoldKey.CtrlWin.IsDown(new HashSet<int> { HoldKeyInfo.VkLWin }));
        Assert.False(HoldKey.Off.IsDown(new HashSet<int> { HoldKeyInfo.VkRControl }));
        Assert.True(HoldKey.CtrlWin.IsPart(HoldKeyInfo.VkRWin));
        Assert.False(HoldKey.RightAlt.IsPart(HoldKeyInfo.VkRControl));
    }

    [Fact]
    public void ToggleShortcutsRoundTrip()
    {
        foreach (var p in ToggleShortcut.Presets) Assert.Equal(p, ToggleShortcut.ByLabel(p.Label));
        Assert.Equal(ToggleShortcut.Off, ToggleShortcut.ByLabel("Off"));
        Assert.Equal(ToggleShortcut.Default, ToggleShortcut.ByLabel("nonsense"));
        Assert.Equal(ToggleShortcut.Default, ToggleShortcut.ByLabel(null));
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
