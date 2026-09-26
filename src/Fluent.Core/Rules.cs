namespace Fluent.Core;

public enum FieldKind
{
    /// <summary>An ordinary text box: the bubble may show and text can go in.</summary>
    Editable,
    /// <summary>A password field. Fluent never shows, records or types here.</summary>
    Secure,
    NotEditable,
}

/// <summary>What Fluent knows about one UI Automation element. Plain values, so the decision about
/// whether the bubble may appear is unit tested without Windows.</summary>
public sealed record FieldTraits(
    string? ControlType,
    bool IsPassword = false,
    bool HasValuePattern = false,
    bool ValueReadOnly = true,
    bool HasTextPattern = false,
    bool IsEnabled = true,
    bool KeyboardFocusable = true);

/// <summary>Mac: <c>FieldClassifier</c>, with UI Automation control types in place of AX roles.</summary>
public static class FieldClassifier
{
    public static readonly HashSet<string> TextTypes = ["Edit", "Document", "ComboBox"];

    public static readonly HashSet<string> NonTextTypes =
    [
        "Button", "CheckBox", "RadioButton", "Slider", "ScrollBar", "Spinner", "Tab", "TabItem",
        "MenuItem", "Menu", "MenuBar", "ListItem", "List", "Tree", "TreeItem", "DataGrid", "DataItem",
        "Hyperlink", "Image", "Text", "ProgressBar", "SplitButton", "Calendar", "Header", "HeaderItem",
        "TitleBar", "ToolBar", "ToolTip", "StatusBar", "Separator", "Thumb", "Window", "Pane",
    ];

    public static FieldKind Classify(FieldTraits t)
    {
        if (t.IsPassword) return FieldKind.Secure;
        if (!t.IsEnabled || !t.KeyboardFocusable) return FieldKind.NotEditable;
        if (t.ControlType == "Edit")
            return t.HasValuePattern && t.ValueReadOnly ? FieldKind.NotEditable : FieldKind.Editable;
        if (t.ControlType == "ComboBox")
            return t.HasValuePattern && !t.ValueReadOnly ? FieldKind.Editable : FieldKind.NotEditable;
        if (t.ControlType == "Document")
            // Browsers report the whole page as a Document; only an editable one is a place to type.
            return t.HasValuePattern && !t.ValueReadOnly ? FieldKind.Editable : FieldKind.NotEditable;
        if (t.ControlType is { } ct && NonTextTypes.Contains(ct)) return FieldKind.NotEditable;
        // Web and Electron editors (contenteditable) come through as groups or customs with a writable value.
        return t.HasValuePattern && !t.ValueReadOnly ? FieldKind.Editable : FieldKind.NotEditable;
    }
}

/// <summary>The spacing and placeholder rules from Android's <c>VoiceAccessibilityService</c>, ported
/// line for line (via the Mac's <c>InsertionRules</c>). Positions are UTF-16 offsets.</summary>
public static class InsertionRules
{
    /// <summary>The text to insert at start..end of <paramref name="existing"/>: trimmed, with a space in
    /// front when the caret sits right after a word and a space after when a word follows.</summary>
    public static string SpacedInsertion(string existing, int start, int end, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return "";
        char? before = start - 1 >= 0 && start - 1 < existing.Length ? existing[start - 1] : null;
        char? after = end >= 0 && end < existing.Length ? existing[end] : null;
        var needsLead = before is { } b && !char.IsWhiteSpace(b) && ",.!?;:".IndexOf(trimmed[0]) < 0;
        var needsTrail = after is { } a && !char.IsWhiteSpace(a) && ",.!?".IndexOf(a) < 0;
        return (needsLead ? " " : "") + trimmed + (needsTrail ? " " : "");
    }

    /// <summary>After pasting <paramref name="piece"/> into a field whose text is now <paramref name="after"/>,
    /// the text with a space added where the paste joined onto a neighbouring word, plus the caret just after
    /// the piece. Null when nothing needs changing.</summary>
    public static (string Text, int Caret)? RespaceAfterPaste(string after, string piece)
    {
        if (piece.Length == 0) return null;
        var at = after.LastIndexOf(piece, StringComparison.Ordinal);
        if (at < 0) return null;
        var end = at + piece.Length;
        char? before = at > 0 ? after[at - 1] : null;
        char? next = end < after.Length ? after[end] : null;
        var lead = before is { } b && !char.IsWhiteSpace(b);
        var trail = next is { } n && !char.IsWhiteSpace(n) && ",.!?;:".IndexOf(n) < 0;
        if (!lead && !trail) return null;
        var fixedText = after[..at] + (lead ? " " : "") + piece + (trail ? " " : "") + after[end..];
        return (fixedText, at + (lead ? 1 : 0) + piece.Length);
    }

    /// <summary>Whether a write really changed the field, judged from the value read back. A value that
    /// changed at all counts as landed (apps may auto-format), so a retry never doubles text.</summary>
    public static bool Landed(string before, string? after, string expected)
    {
        if (after is null) return false;
        if (after == expected) return true;
        return after != before;
    }
}

/// <summary>Android decides the Style category from the package of the app in front. Windows does the
/// same from the process name of the foreground window, with Android's lists mapped to their Windows apps
/// (and web apps judged by the browser tab title).</summary>
public static class AppCategories
{
    public static readonly HashSet<string> Personal =
    [
        "whatsapp", "whatsapp.root", "telegram", "signal", "discord", "discordptb", "discordcanary",
        "messenger", "line", "viber", "wechat", "weixin", "kakaotalk", "instagram", "snapchat", "skype",
        "phonelink", "yourphone",
    ];

    public static readonly HashSet<string> Work =
    [
        "slack", "teams", "ms-teams", "msteams", "zoom", "webex", "ciscocollabhost", "mattermost",
        "rocket.chat", "lark", "feishu", "zohocliq", "linkedin", "workplace", "googlechat",
    ];

    public static readonly HashSet<string> Email =
    [
        "outlook", "olk", "hxoutlook", "thunderbird", "mailbird", "em client", "mailclient", "spark",
        "proton mail", "protonmail", "tutanota", "superhuman", "postbox", "mimestream", "hxmail",
    ];

    /// <summary>Web apps in a browser, matched on the window title (e.g. "Inbox (3) - you@gmail.com - Gmail").</summary>
    static readonly (string Needle, StyleCategory Category)[] WebTitles =
    [
        ("- Gmail", StyleCategory.Email), ("Outlook", StyleCategory.Email), ("Proton Mail", StyleCategory.Email),
        ("Yahoo Mail", StyleCategory.Email),
        ("WhatsApp", StyleCategory.Personal), ("Telegram", StyleCategory.Personal), ("Messenger", StyleCategory.Personal),
        ("Discord", StyleCategory.Personal), ("Instagram", StyleCategory.Personal),
        ("Slack", StyleCategory.Work), ("Microsoft Teams", StyleCategory.Work), ("Google Chat", StyleCategory.Work),
        ("LinkedIn", StyleCategory.Work),
    ];

    public static readonly HashSet<string> Browsers =
        ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "chromium", "zen", "librewolf", "waterfox"];

    public static string Normalize(string process)
    {
        var p = process.Trim().ToLowerInvariant();
        return p.EndsWith(".exe") ? p[..^4] : p;
    }

    public static StyleCategory Category(string? process, string? windowTitle = null)
    {
        if (string.IsNullOrWhiteSpace(process)) return StyleCategory.Other;
        var p = Normalize(process);
        if (Personal.Contains(p)) return StyleCategory.Personal;
        if (Work.Contains(p)) return StyleCategory.Work;
        if (Email.Contains(p)) return StyleCategory.Email;
        if (Browsers.Contains(p) && windowTitle is { Length: > 0 } title)
            foreach (var (needle, cat) in WebTitles)
                if (title.Contains(needle, StringComparison.OrdinalIgnoreCase)) return cat;
        return StyleCategory.Other;
    }

    /// <summary>Windows wording for the Style screen ("This style applies in …").</summary>
    public static string AppliesIn(StyleCategory c) => c switch
    {
        StyleCategory.Personal => "WhatsApp, Telegram, Signal, Discord, Messenger and friends (apps or web)",
        StyleCategory.Work => "Slack, Teams, Google Chat, Zoom and other work chat",
        StyleCategory.Email => "Outlook, Gmail, Thunderbird, Proton Mail and other email",
        _ => "Every other app",
    };
}

/// <summary>Where the bubble sits next to a text box, in screen pixels (origin top-left, y down).</summary>
public static class BubblePlacement
{
    public const double Gap = 8;

    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public double MidX => X + Width / 2;
        public double MidY => Y + Height / 2;
        public bool IsEmpty => Width < 1 || Height < 1;
        public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
    }

    /// <summary>Just past the right end of a one-line field, centred on it; inside the bottom-right corner
    /// of a tall one. <paramref name="offset"/> is where the user dragged it to, relative to that spot.
    /// The result is kept on the field's screen.</summary>
    public static (double X, double Y) Origin(Rect field, double size, Rect screen, (double X, double Y) offset = default)
    {
        double x, y;
        if (field.IsEmpty)
        {
            x = screen.Right - size - 24; y = screen.MidY - size / 2;
        }
        else if (field.Height > size * 2.2)
        {
            x = field.Right - size - 14; y = field.Bottom - size - 14;
        }
        else
        {
            x = field.Right + Gap; y = field.MidY - size / 2;
            // No room on the right: tuck it inside the field's right end instead.
            if (x + size > screen.Right - 4) x = field.Right - size - 6;
        }
        return Clamp(x + offset.X, y + offset.Y, size, screen);
    }

    public static (double X, double Y) Clamp(double x, double y, double size, Rect screen) =>
        (Math.Min(Math.Max(x, screen.X + 4), screen.Right - size - 4),
         Math.Min(Math.Max(y, screen.Y + 4), screen.Bottom - size - 4));

    /// <summary>Top-centre of the work area: where the recording capsule goes.</summary>
    public static (double X, double Y) CapsuleOrigin(Rect workArea, double width, double height) =>
        (workArea.MidX - width / 2, workArea.Y + 10);
}
