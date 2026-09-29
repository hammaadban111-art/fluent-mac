namespace Fluent.Core;

/// <summary>A key or key combination the user picked in Settings (Wispr Flow style: press the keys to record
/// them). Windows virtual-key codes, left and right modifiers kept apart. At most one ordinary key; the rest
/// are modifiers. Either a combination of modifiers only (Right Ctrl, Ctrl + Win…) or modifiers plus one key
/// (Ctrl + Alt + Space, F8, Caps Lock…).
///
/// Matching: a modifier-only combination matches its exact sides (Right Ctrl is not Left Ctrl). With an
/// ordinary key, either side of each modifier counts, and no extra modifier may be down, so Ctrl + Space
/// does not fire on Ctrl + Shift + Space.</summary>
public sealed class KeyCombo : IEquatable<KeyCombo>
{
    // Virtual-key codes.
    public const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    public const int VkLWin = 0x5B, VkRWin = 0x5C, VkEscape = 0x1B, VkSpace = 0x20;
    // Generic codes some sources report instead of the sided ones.
    const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

    public enum Group { Ctrl, Alt, Shift, Win }

    /// <summary>Sorted, sided modifier codes.</summary>
    public IReadOnlyList<int> Modifiers { get; }
    /// <summary>The one ordinary key, if any.</summary>
    public int? Key { get; }

    public KeyCombo(IEnumerable<int> modifiers, int? key)
    {
        Key = key is { } k && !IsModifier(k) ? k : null;
        var mods = modifiers.Select(Sided).Where(IsModifier);
        // With an ordinary key either side counts, so store the left one: Right Ctrl + Space is Ctrl + Space.
        if (Key is not null) mods = mods.Select(Left);
        Modifiers = mods.Distinct().OrderBy(Order).ToList();
    }

    static int Left(int vk) => vk switch { VkRShift => VkLShift, VkRControl => VkLControl, VkRMenu => VkLMenu, VkRWin => VkLWin, _ => vk };

    public static readonly KeyCombo Off = new([], null);
    public static readonly KeyCombo RightCtrl = new([VkRControl], null);
    public static KeyCombo DefaultHold => RightCtrl;
    public static KeyCombo DefaultToggle => new([VkLControl, VkLMenu], VkSpace);

    public bool IsOff => Modifiers.Count == 0 && Key is null;
    public bool ModifierOnly => Key is null && Modifiers.Count > 0;
    public IEnumerable<Group> Groups => Modifiers.Select(GroupOf).Distinct();
    /// <summary>Alt or Win on their own open the menu bar or the Start menu when released, so presses that
    /// Fluent consumes need masking.</summary>
    public bool NeedsMask => Groups.Any(g => g is Group.Alt or Group.Win);

    // matching

    /// <summary>Whether the whole combination is down, given the set of virtual keys currently held.</summary>
    public bool IsDown(IReadOnlySet<int> held)
    {
        if (IsOff) return false;
        if (Key is null) return Modifiers.All(held.Contains);
        if (!held.Contains(Key.Value)) return false;
        var want = Groups.ToHashSet();
        var have = held.Where(IsModifier).Select(GroupOf).ToHashSet();
        return want.SetEquals(have);
    }

    /// <summary>Whether <paramref name="vk"/> is one of the keys that make up the combination.</summary>
    public bool IsPart(int vk)
    {
        if (IsOff) return false;
        vk = Sided(vk);
        if (Key is null) return Modifiers.Contains(vk);
        return vk == Key || (IsModifier(vk) && Groups.Contains(GroupOf(vk)));
    }

    // keys

    public static bool IsModifier(int vk) => vk is VkLShift or VkRShift or VkLControl or VkRControl or VkLMenu or VkRMenu
        or VkLWin or VkRWin or VkShift or VkControl or VkMenu;

    static int Sided(int vk) => vk switch { VkShift => VkLShift, VkControl => VkLControl, VkMenu => VkLMenu, _ => vk };

    public static Group GroupOf(int vk) => Sided(vk) switch
    {
        VkLControl or VkRControl => Group.Ctrl,
        VkLMenu or VkRMenu => Group.Alt,
        VkLShift or VkRShift => Group.Shift,
        _ => Group.Win,
    };

    static int Order(int vk) => (int)GroupOf(vk) * 2 + (vk is VkRControl or VkRMenu or VkRShift or VkRWin ? 1 : 0);

    // text

    /// <summary>Stored form: hex codes joined with '+', or "off".</summary>
    public string Code => IsOff ? "off" : string.Join("+", Modifiers.Append(Key ?? -1).Where(k => k >= 0).Select(k => k.ToString("X2")));

    public static KeyCombo? Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        if (code == "off") return Off;
        var mods = new List<int>();
        int? key = null;
        foreach (var part in code.Split('+'))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out var vk) || vk is <= 0 or > 0xFE) return null;
            if (IsModifier(vk)) mods.Add(vk);
            else if (key is null) key = vk;
            else return null;
        }
        var c = new KeyCombo(mods, key);
        return c.IsOff ? null : c;
    }

    public string Label
    {
        get
        {
            if (IsOff) return "Off";
            // With an ordinary key, sides do not matter (either Ctrl works), so the label leaves them out.
            IEnumerable<string> mods = Key is null
                ? Modifiers.Select(ModifierName)
                : Groups.OrderBy(g => g).Select(g => g.ToString());
            return string.Join(" + ", Key is { } k ? mods.Append(KeyName(k)) : mods);
        }
    }

    static string ModifierName(int vk) => vk switch
    {
        VkLControl => "Left Ctrl", VkRControl => "Right Ctrl", VkLMenu => "Left Alt", VkRMenu => "Right Alt",
        VkLShift => "Left Shift", VkRShift => "Right Shift", VkLWin => "Left Win", _ => "Right Win",
    };

    public static string KeyName(int vk)
    {
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
        return vk switch
        {
            0x20 => "Space", 0x0D => "Enter", 0x09 => "Tab", 0x08 => "Backspace", 0x1B => "Esc",
            0x2D => "Insert", 0x2E => "Delete", 0x24 => "Home", 0x23 => "End", 0x21 => "Page Up", 0x22 => "Page Down",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x14 => "Caps Lock", 0x90 => "Num Lock", 0x91 => "Scroll Lock", 0x13 => "Pause", 0x2C => "Print Screen",
            0x5D => "Menu", 0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num -", 0x6E => "Num .", 0x6F => "Num /",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/", 0xC0 => "`",
            0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'", 0xE2 => "\\ (102nd key)",
            0xAD => "Mute", 0xAE => "Volume Down", 0xAF => "Volume Up", 0xB0 => "Next Track", 0xB1 => "Previous Track",
            0xB2 => "Stop Media", 0xB3 => "Play/Pause", 0xA6 => "Browser Back", 0xA7 => "Browser Forward",
            0xA8 => "Browser Refresh", 0xAA => "Browser Search", 0xAB => "Browser Favorites", 0xAC => "Browser Home",
            0xB4 => "Mail", 0xB5 => "Media", 0xB6 => "App 1", 0xB7 => "App 2",
            0x19 => "Kanji", 0x15 => "Kana", 0x1C => "Convert", 0x1D => "Non-convert",
            _ => $"Key {vk:X2}",
        };
    }

    /// <summary>Why this combination is a poor choice, or null. It still works; Settings shows the note.</summary>
    public string? Warning => IsOff ? null
        : Key is { } k && Modifiers.Count == 0 && (k is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or 0x20 or 0x0D or 0x09 or 0x08 or >= 0xBA and <= 0xDE)
            ? $"{KeyName(k)} on its own will stop typing in other apps while Fluent uses it."
            : null;

    public bool Equals(KeyCombo? other) => other is not null && Code == other.Code;
    public override bool Equals(object? obj) => Equals(obj as KeyCombo);
    public override int GetHashCode() => Code.GetHashCode();
    public override string ToString() => Label;

    // Settings written by Fluent 1.0, before any key could be picked.

    public static KeyCombo FromLegacyHold(string? name) => name switch
    {
        "RightCtrl" => RightCtrl,
        "RightAlt" => new([VkRMenu], null),
        "CtrlWin" => new([VkLControl, VkLWin], null),
        "Off" => Off,
        _ => DefaultHold,
    };

    public static KeyCombo FromLegacyToggle(string? label) => label switch
    {
        "Ctrl + Shift + Space" => new([VkLControl, VkLShift], VkSpace),
        "Alt + Space" => new([VkLMenu], VkSpace),
        "Ctrl + Alt + D" => new([VkLControl, VkLMenu], 0x44),
        "Off" => Off,
        _ => DefaultToggle,
    };
}

/// <summary>Turns the keys pressed in Settings into a <see cref="KeyCombo"/>. Modifiers are collected while
/// they are held; the first ordinary key completes the combination, or, with modifiers only, letting go
/// of all of them does. Esc on its own cancels.</summary>
public sealed class KeyComboRecorder
{
    readonly HashSet<int> down = [];
    readonly HashSet<int> peak = [];

    public enum Result { Continue, Done, Cancelled }
    public KeyCombo? Combo { get; private set; }

    public Result Key(int vk, bool isDown)
    {
        if (isDown)
        {
            if (KeyCombo.IsModifier(vk)) { down.Add(vk); peak.Add(vk); return Result.Continue; }
            if (vk == KeyCombo.VkEscape && down.Count == 0) return Result.Cancelled;
            Combo = new KeyCombo(down, vk);
            return Result.Done;
        }
        down.Remove(vk);
        if (down.Count == 0 && peak.Count > 0 && KeyCombo.IsModifier(vk))
        {
            Combo = new KeyCombo(peak, null);
            return Result.Done;
        }
        return Result.Continue;
    }
}

/// <summary>What the keyboard hook does while Settings records a shortcut, and just after. While recording,
/// every key goes to the <see cref="KeyComboRecorder"/> first (key-ups too: a modifier on its own, like
/// Right Ctrl, completes when it is let go) and is then swallowed, so Win does not open Start and the key
/// does nothing in the app. Keys still held when recording ends keep being swallowed (their repeats and
/// release), so no app sees half a keystroke and nothing fires by accident.</summary>
public sealed class KeyRecordingSession
{
    readonly HashSet<int> held = [];
    KeyComboRecorder? recorder;

    public bool Recording => recorder is not null;

    public void Start() => recorder = new KeyComboRecorder();

    public void Stop() => recorder = null;

    /// <summary>One key event. Swallow says whether to hide it from Windows; Result and Combo say whether
    /// recording just finished.</summary>
    public (bool Swallow, KeyComboRecorder.Result Result, KeyCombo? Combo) Key(int vk, bool down)
    {
        if (recorder is { } r)
        {
            var repeat = down && held.Contains(vk);
            if (down) held.Add(vk); else held.Remove(vk);
            if (repeat) return (true, KeyComboRecorder.Result.Continue, null);
            var result = r.Key(vk, down);
            if (result != KeyComboRecorder.Result.Continue) recorder = null;
            return (true, result, result == KeyComboRecorder.Result.Done ? r.Combo : null);
        }
        // Recording is over: the rest of a keystroke that started while recording.
        if (held.Contains(vk))
        {
            if (!down) held.Remove(vk);
            return (true, KeyComboRecorder.Result.Continue, null);
        }
        return (false, KeyComboRecorder.Result.Continue, null);
    }
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

/// <summary>Everything the keyboard hook decides, kept free of Windows calls so it can be tested:
/// hold-to-talk on <see cref="Hold"/>, start/stop on <see cref="Toggle"/>, Esc to cancel.
///
/// The start/stop combination fires when it goes down if it has an ordinary key, and on a quick tap
/// (pressed and released with nothing else) if it is modifiers only. When both are the same modifier keys,
/// a tap starts or stops and a hold talks. The ordinary key of either combination is swallowed while it
/// is in use, so F8 or Caps Lock do nothing else; modifiers always pass through.</summary>
public sealed class HotkeyEngine
{
    public KeyCombo Hold { get; set; } = KeyCombo.DefaultHold;
    public KeyCombo Toggle { get; set; } = KeyCombo.DefaultToggle;
    /// <summary>False before the terms are accepted: keys are only tracked.</summary>
    public bool Active { get; set; } = true;
    public HoldGesture Gesture { get; } = new();

    public static readonly TimeSpan TapWindow = TimeSpan.FromMilliseconds(500);

    public enum Signal { ArmHoldTimer, CancelHoldTimer, StopHold, Toggle, Cancel, Mask }

    readonly HashSet<int> held = [];
    readonly HashSet<int> swallowed = [];
    double? tapStart;
    bool tapClean;

    public IReadOnlySet<int> Held => held;

    /// <summary>One key event. Returns whether to swallow it, and what to do (in order).</summary>
    public (bool Swallow, List<Signal> Signals) Key(int vk, bool down, double now, bool dictationLive)
    {
        var signals = new List<Signal>();
        var repeat = down && held.Contains(vk);
        if (down) held.Add(vk); else held.Remove(vk);

        if (repeat) return (swallowed.Contains(vk), signals);
        if (!down && swallowed.Remove(vk)) { ReleaseHold(vk, signals); ReleaseTap(vk, now, signals); return (true, signals); }
        if (!Active) return (false, signals);

        if (down && vk == KeyCombo.VkEscape && dictationLive && !Hold.IsPart(vk) && !Toggle.IsPart(vk))
        {
            // Esc that cancels a dictation is Fluent's; it must not also close a dialog in the app.
            signals.Add(Signal.Cancel);
            swallowed.Add(vk);
            return (true, signals);
        }

        var swallow = false;

        // Start / stop.
        if (down && Toggle.Key == vk && Toggle.IsDown(held))
        {
            signals.Add(Signal.Toggle);
            if (Toggle.NeedsMask) signals.Add(Signal.Mask);
            swallow = true;
        }
        if (Toggle.ModifierOnly)
        {
            if (down && Toggle.IsPart(vk) && Toggle.IsDown(held) && held.Count == Toggle.Modifiers.Count)
            {
                tapStart = now; tapClean = true;
                if (Toggle.NeedsMask) signals.Add(Signal.Mask);
            }
            else if (down) tapClean = false;
        }

        // Hold to talk.
        if (!Hold.IsOff)
        {
            if (Hold.IsPart(vk))
            {
                var isDown = Hold.IsDown(held);
                // Modifiers only: nothing else may be held (Shift + Right Ctrl + arrow is text selection).
                var clean = !Hold.ModifierOnly || held.Count == Hold.Modifiers.Count;
                if (isDown && clean && down && Gesture.PressedAt is null)
                {
                    Add(signals, Gesture.Down(now));
                    if (vk == Hold.Key) swallow = true;
                }
                else if (!isDown && Gesture.PressedAt is not null) ReleaseHold(vk, signals);
                else if (down && vk == Hold.Key && Gesture.PressedAt is not null) swallow = true;
            }
            else if (down && Gesture.PressedAt is not null) Add(signals, Gesture.OtherKey());   // Ctrl+C is a shortcut, not a hold
        }

        if (!down) ReleaseTap(vk, now, signals);
        if (swallow && down) swallowed.Add(vk);
        return (swallow, signals);
    }

    void ReleaseHold(int vk, List<Signal> signals)
    {
        if (Gesture.PressedAt is null || !Hold.IsPart(vk)) return;
        var wasDictating = Gesture.Dictating;
        Add(signals, Gesture.Up());
        if (wasDictating) tapStart = null;   // a hold that talked is not also a tap
    }

    void ReleaseTap(int vk, double now, List<Signal> signals)
    {
        if (tapStart is not { } start || !Toggle.IsPart(vk)) return;
        tapStart = null;
        var window = Toggle.Equals(Hold) ? HoldGesture.HoldDelay : TapWindow;
        if (tapClean && now - start <= window.TotalSeconds && !Gesture.Dictating) signals.Add(Signal.Toggle);
    }

    /// <summary>The hold timer fired: true means start recording now.</summary>
    public bool HoldTimerFired() => Active && Gesture.TimerFired();

    /// <summary>Forget held keys, e.g. after the hook was paused while recording a new combination.</summary>
    public void Reset()
    {
        held.Clear(); swallowed.Clear(); tapStart = null;
        if (Gesture.PressedAt is not null) Gesture.Up();
    }

    static void Add(List<Signal> signals, HoldGesture.Action a)
    {
        switch (a)
        {
            case HoldGesture.Action.ArmTimer: signals.Add(Signal.ArmHoldTimer); break;
            case HoldGesture.Action.CancelTimer: signals.Add(Signal.CancelHoldTimer); break;
            case HoldGesture.Action.StopAndInsert: signals.Add(Signal.StopHold); break;
        }
    }
}
