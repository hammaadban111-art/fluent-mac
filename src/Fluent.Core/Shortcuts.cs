namespace Fluent.Core;

/// <summary>The key held for push-to-talk (Mac: Right Option / Fn). Right Ctrl is the default: it is
/// never AltGr, and a lone Right Ctrl press does nothing in Windows or in apps.</summary>
public enum HoldKey { RightCtrl, RightAlt, CtrlWin, Off }

public static class HoldKeyInfo
{
    public static string Label(this HoldKey k) => k switch
    {
        HoldKey.RightCtrl => "Right Ctrl",
        HoldKey.RightAlt => "Right Alt",
        HoldKey.CtrlWin => "Ctrl + Win",
        _ => "Off",
    };

    // Virtual-key codes.
    public const int VkLControl = 0xA2, VkRControl = 0xA3, VkRMenu = 0xA5, VkLWin = 0x5B, VkRWin = 0x5C;
    public const int VkEscape = 0x1B;

    /// <summary>Whether the hold key is fully down, given the set of virtual keys currently held.</summary>
    public static bool IsDown(this HoldKey k, IReadOnlySet<int> held) => k switch
    {
        HoldKey.RightCtrl => held.Contains(VkRControl),
        HoldKey.RightAlt => held.Contains(VkRMenu),
        HoldKey.CtrlWin => (held.Contains(VkLControl) || held.Contains(VkRControl)) && (held.Contains(VkLWin) || held.Contains(VkRWin)),
        _ => false,
    };

    /// <summary>Whether <paramref name="vk"/> is one of the keys that make up the hold key.</summary>
    public static bool IsPart(this HoldKey k, int vk) => k switch
    {
        HoldKey.RightCtrl => vk == VkRControl,
        HoldKey.RightAlt => vk == VkRMenu,
        HoldKey.CtrlWin => vk is VkLControl or VkRControl or VkLWin or VkRWin,
        _ => false,
    };
}

/// <summary>A press-to-start, press-again-to-stop shortcut, registered with <c>RegisterHotKey</c>.</summary>
public sealed record ToggleShortcut(uint Modifiers, uint Key, string Label)
{
    // RegisterHotKey modifier bits.
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8, NoRepeat = 0x4000;
    public const uint Space = 0x20, D = 0x44;

    public static readonly IReadOnlyList<ToggleShortcut> Presets =
    [
        new(Control | Alt, Space, "Ctrl + Alt + Space"),
        new(Control | Shift, Space, "Ctrl + Shift + Space"),
        new(Alt, Space, "Alt + Space"),
        new(Control | Alt, D, "Ctrl + Alt + D"),
    ];

    public static readonly ToggleShortcut Off = new(0, 0, "Off");
    public static ToggleShortcut Default => Presets[0];
    public bool IsOff => this == Off;

    public static ToggleShortcut ByLabel(string? label) =>
        label == Off.Label ? Off : Presets.FirstOrDefault(p => p.Label == label) ?? Default;
}

/// <summary>Push-to-talk timing, the Mac's <c>HoldGesture</c> unchanged. A modifier tapped and released
/// quickly, or used together with another key (Ctrl+C…), is ordinary typing and must never start a dictation.</summary>
public sealed class HoldGesture
{
    public static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(280);

    public enum Action { None, ArmTimer, CancelTimer, StopAndInsert }

    public double? PressedAt { get; private set; }
    public bool Spoiled { get; private set; }
    public bool Dictating { get; private set; }

    public Action Down(double at)
    {
        PressedAt = at; Spoiled = false; Dictating = false;
        return Action.ArmTimer;
    }

    public Action OtherKey()
    {
        if (PressedAt is null || Dictating) return Action.None;
        Spoiled = true;
        return Action.CancelTimer;
    }

    public Action Up()
    {
        var result = Dictating ? Action.StopAndInsert : Action.CancelTimer;
        PressedAt = null; Spoiled = false; Dictating = false;
        return result;
    }

    /// <summary>Called when the hold timer fires. True means start recording now.</summary>
    public bool TimerFired()
    {
        if (PressedAt is null || Spoiled || Dictating) return false;
        Dictating = true;
        return true;
    }
}
