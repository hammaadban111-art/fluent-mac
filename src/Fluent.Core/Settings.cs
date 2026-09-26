using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fluent.Core;

/// <summary>Where settings live. The app uses <see cref="JsonFileStore"/>; tests use <see cref="MemoryStore"/>.</summary>
public interface ISettingsStore
{
    JsonNode? Get(string key);
    void Set(string key, JsonNode? value);
}

public sealed class MemoryStore : ISettingsStore
{
    readonly Dictionary<string, JsonNode?> values = new();
    public JsonNode? Get(string key) => values.TryGetValue(key, out var v) ? v?.DeepClone() : null;
    public void Set(string key, JsonNode? value) => values[key] = value?.DeepClone();
}

/// <summary>One JSON file (settings.json in %APPDATA%\Fluent), rewritten atomically on every change.
/// The API key is never here: it is kept apart, encrypted for the Windows user (see the app's KeyStore).</summary>
public sealed class JsonFileStore : ISettingsStore
{
    readonly string path;
    readonly object gate = new();
    JsonObject root;

    public JsonFileStore(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        root = Load(path);
    }

    static JsonObject Load(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    public JsonNode? Get(string key)
    {
        lock (gate) return root[key]?.DeepClone();
    }

    public void Set(string key, JsonNode? value)
    {
        lock (gate)
        {
            if (value is null) root.Remove(key); else root[key] = value.DeepClone();
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
    }
}

public static class Languages
{
    public const string Auto = "auto";

    public static readonly IReadOnlyList<(string Code, string Name)> Options =
    [
        (Auto, "Detect automatically"),
        ("en-US", "English (United States)"),
        ("en-GB", "English (United Kingdom)"),
        ("es-ES", "Spanish (Spain)"),
        ("es-419", "Spanish (Latin America)"),
        ("fr-FR", "French"),
        ("de-DE", "German"),
        ("it-IT", "Italian"),
        ("pt-BR", "Portuguese (Brazil)"),
        ("nl-NL", "Dutch"),
        ("hi-IN", "Hindi"),
        ("ar-EG", "Arabic"),
        ("ja-JP", "Japanese"),
        ("ko-KR", "Korean"),
        ("zh-CN", "Chinese (Simplified)"),
        ("ru-RU", "Russian"),
        ("tr-TR", "Turkish"),
        ("pl-PL", "Polish"),
        ("sv-SE", "Swedish"),
        ("id-ID", "Indonesian"),
        ("vi-VN", "Vietnamese"),
        ("ur-PK", "Urdu"),
    ];
}

/// <summary>Snooze choices, as Android's <c>SnoozeDuration</c>. <see cref="UntilRestart"/> lasts until Fluent quits.</summary>
public enum SnoozeChoice { Fifteen = 15, Thirty = 30, Hour = 60, UntilRestart = -1 }

public static class SnoozeInfo
{
    public static string Label(this SnoozeChoice c) => c switch
    {
        SnoozeChoice.Fifteen => "15 minutes",
        SnoozeChoice.Thirty => "30 minutes",
        SnoozeChoice.Hour => "1 hour",
        _ => "Until Fluent restarts",
    };
}

/// <summary>Every setting, with the same keys and defaults as the Mac app's <c>Settings</c> +
/// <c>MacSettings</c> (which follow Android's <c>SettingsRepository</c>).</summary>
public sealed class Settings(ISettingsStore store)
{
    public ISettingsStore Store { get; } = store;
    public const string MatchApp = "match";
    const double UntilRestartMarker = -1;

    string? Str(string k) => Store.Get(k) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    bool? Bool(string k) => Store.Get(k) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
    double? Num(string k) => Store.Get(k) is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    public bool SetupComplete { get => Bool("setup_complete") ?? false; set => Store.Set("setup_complete", value); }

    public TranscriptionMode Mode
    {
        get => TranscriptionModeInfo.Parse(Str("mode"));
        set => Store.Set("mode", value.Raw());
    }

    public string LanguageCode { get => Str("language") ?? Languages.Auto; set => Store.Set("language", value); }

    /// <summary>What Gemini is told: nothing for automatic detection, otherwise the one chosen language.</summary>
    public IReadOnlyList<string> LanguageCodes => LanguageCode == Languages.Auto ? [] : [LanguageCode];

    public IReadOnlyList<string> Vocabulary
    {
        get => Store.Get("vocabulary") is JsonArray a ? a.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() : [];
        set => Store.Set("vocabulary", new JsonArray(value.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()));
    }

    /// <summary>Off by default, as on Android: transcripts are only kept when the user asks for it.</summary>
    public bool HistoryEnabled { get => Bool("history_enabled") ?? false; set => Store.Set("history_enabled", value); }

    public string ThemeId { get => Str("theme") ?? "aurora"; set => Store.Set("theme", value); }

    public bool StyleEnabled { get => Bool("style_enabled") ?? true; set => Store.Set("style_enabled", value); }

    public WritingStyle StyleFor(StyleCategory c)
    {
        var s = StyleInfo.ParseStyle(Str($"style_{c.Raw().ToLowerInvariant()}"));
        return s is { } style && c.Styles().Contains(style) ? style : c.DefaultStyle();
    }

    public void SetStyle(WritingStyle s, StyleCategory c) => Store.Set($"style_{c.Raw().ToLowerInvariant()}", s.Raw());

    public bool TermsAccepted => Str("terms_accepted_version") == Constants.TermsVersion;

    public void AcceptTerms(DateTimeOffset? now = null)
    {
        Store.Set("terms_accepted_version", Constants.TermsVersion);
        Store.Set("terms_accepted_at", (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds());
    }

    /// <summary>Verbatim mode promises the words exactly as spoken, so styles apply in Smart mode only.</summary>
    public string ApplyStyle(string transcript, StyleCategory category)
    {
        if (!StyleEnabled || Mode != TranscriptionMode.Smart) return transcript;
        var styled = StyleFormatter.Format(transcript, StyleFor(category), category);
        return string.IsNullOrWhiteSpace(styled) ? transcript : styled;
    }

    // Windows side (Mac: MacSettings)

    /// <summary>On by default: the bubble is the point of the app.</summary>
    public bool BubbleEnabled { get => Bool("bubble_enabled") ?? true; set => Store.Set("bubble_enabled", value); }

    /// <summary>Bubble diameter in device-independent pixels, 34…60 like the Mac.</summary>
    public double BubbleSize
    {
        get => Math.Clamp(Num("bubble_size") ?? 42, 34, 60);
        set => Store.Set("bubble_size", Math.Clamp(value, 34, 60));
    }

    public double BubbleOpacity
    {
        get => Math.Clamp(Num("bubble_opacity") ?? 0.95, 0.35, 1);
        set => Store.Set("bubble_opacity", Math.Clamp(value, 0.35, 1));
    }

    /// <summary>Where the user dragged the bubble, relative to its automatic spot next to the field.</summary>
    public (double X, double Y) BubbleOffset
    {
        get => (Num("bubble_offset_x") ?? 0, Num("bubble_offset_y") ?? 0);
        set { Store.Set("bubble_offset_x", value.X); Store.Set("bubble_offset_y", value.Y); }
    }

    /// <summary>Process names (lower case, no ".exe") where the bubble never shows.</summary>
    public IReadOnlyList<string> ExcludedApps
    {
        get => Store.Get("excluded_apps") is JsonArray a ? a.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() : [];
        set => Store.Set("excluded_apps", new JsonArray(value.Select(AppCategories.Normalize).Distinct().Order()
            .Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()));
    }

    public bool IsExcluded(string? process) => process is not null && ExcludedApps.Contains(AppCategories.Normalize(process));

    public SnoozeChoice SnoozeChoice
    {
        get => Num("snooze_minutes") is { } m && Enum.IsDefined(typeof(SnoozeChoice), (int)m) ? (SnoozeChoice)(int)m : SnoozeChoice.Thirty;
        set => Store.Set("snooze_minutes", (int)value);
    }

    /// <summary>Seconds since 1970; 0 when not snoozed; -1 for "until restart".</summary>
    public double SnoozeUntil { get => Num("snooze_until") ?? 0; set => Store.Set("snooze_until", value); }

    public void Snooze(SnoozeChoice choice, DateTimeOffset? now = null) =>
        SnoozeUntil = choice == SnoozeChoice.UntilRestart ? UntilRestartMarker
            : (now ?? DateTimeOffset.UtcNow).AddMinutes((int)choice).ToUnixTimeMilliseconds() / 1000.0;

    public void ClearSnooze() => SnoozeUntil = 0;

    public bool SnoozeActive(DateTimeOffset? now = null) =>
        SnoozeUntil == UntilRestartMarker || SnoozeUntil > (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>"Until restart" ends when Fluent starts again.</summary>
    public void ClearRestartSnooze()
    {
        if (SnoozeUntil == UntilRestartMarker) SnoozeUntil = 0;
    }

    public HoldKey HoldKey
    {
        get => Enum.TryParse<HoldKey>(Str("hold_key"), out var k) ? k : HoldKey.RightCtrl;
        set => Store.Set("hold_key", value.ToString());
    }

    public ToggleShortcut ToggleShortcut
    {
        get => ToggleShortcut.ByLabel(Str("toggle_shortcut"));
        set => Store.Set("toggle_shortcut", value.Label);
    }

    /// <summary>Palette for the bubble and capsule; <see cref="MatchApp"/> follows the app theme.</summary>
    public string OverlayThemeId { get => Str("overlay_theme") ?? MatchApp; set => Store.Set("overlay_theme", value); }

    /// <summary>Short start/stop sounds (Android: haptics).</summary>
    public bool SoundsEnabled { get => Bool("sounds") ?? true; set => Store.Set("sounds", value); }

    /// <summary>Whether the bubble may be shown for a field in <paramref name="process"/> right now
    /// (Android's <c>syncBubbleVisibility</c>).</summary>
    public bool BubbleAllowed(bool termsAccepted, FieldKind? field, string? process, DateTimeOffset? now = null) =>
        BubbleEnabled && termsAccepted && !SnoozeActive(now) && field == FieldKind.Editable
        && !IsExcluded(process) && !string.Equals(process, "fluent", StringComparison.OrdinalIgnoreCase);
}
