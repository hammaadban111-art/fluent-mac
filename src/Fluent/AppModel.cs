using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Fluent.Core;

namespace Fluent;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public enum Tab { Dictate, History, Style, Settings }

/// <summary>App-wide state for the UI. Settings are mirrored into observable properties and written
/// straight back to settings.json, like the Mac's <c>AppModel</c>.</summary>
public sealed class AppModel : Observable
{
    public Settings Store { get; }
    public HistoryStore HistoryStore { get; }
    public DictationController Dictation { get; }
    /// <summary>Set when launched with --demo: nothing touches the real settings or key.</summary>
    public string? Demo { get; }

    public event Action? ThemeChanged;
    public event Action? ShortcutsChanged;
    public event Action? TabChanged;

    public AppModel(Settings store, string dataDir, string? demo = null)
    {
        Store = store;
        Demo = demo;
        HistoryStore = new HistoryStore(dataDir);
        Store.ClearRestartSnooze();
        var key = demo is null ? KeyStore.Load() : null;
        hasApiKey = !string.IsNullOrEmpty(key);
        maskedKey = Mask(key);
        launchAtLogin = demo is null && LaunchAtLogin.Enabled;
        foreach (var t in HistoryStore.All()) History.Add(t);
        Dictation = new DictationController(this);
        RefreshPermissions();
    }

    Tab tab = Tab.Dictate;
    public Tab Tab { get => tab; set { if (Set(ref tab, value)) TabChanged?.Invoke(); } }

    public bool TermsAccepted => Store.TermsAccepted;
    public void AcceptTerms() { Store.AcceptTerms(); Raise(nameof(TermsAccepted)); }

    public bool SetupComplete { get => Store.SetupComplete; set { Store.SetupComplete = value; Raise(); } }

    public string ThemeId
    {
        get => Palette.ById(Store.ThemeId).Id;
        set { Store.ThemeId = value; Raise(); Raise(nameof(Palette)); Raise(nameof(OverlayPalette)); ThemeChanged?.Invoke(); }
    }
    public Palette Palette => Palette.ById(Store.ThemeId);
    public string OverlayThemeId
    {
        get => Store.OverlayThemeId;
        set { Store.OverlayThemeId = value; Raise(); Raise(nameof(OverlayPalette)); }
    }
    public Palette OverlayPalette => OverlayThemeId == Settings.MatchApp ? Palette : Palette.ById(OverlayThemeId);

    public TranscriptionMode Mode { get => Store.Mode; set { Store.Mode = value; Raise(); Raise(nameof(ModeDetail)); Raise(nameof(StyleNote)); } }
    public string ModeDetail => Mode.Detail();
    public string LanguageCode { get => Store.LanguageCode; set { Store.LanguageCode = value; Raise(); } }

    public ObservableCollection<string> Vocabulary { get; } = [];
    public void AddWord(string w)
    {
        w = w.Trim();
        var list = Store.Vocabulary.ToList();
        if (w.Length == 0 || list.Contains(w)) return;
        list.Add(w);
        Store.Vocabulary = list;
        Vocabulary.Add(w);
    }
    public void RemoveWord(string w)
    {
        Store.Vocabulary = Store.Vocabulary.Where(x => x != w).ToList();
        Vocabulary.Remove(w);
    }

    public bool HistoryEnabled { get => Store.HistoryEnabled; set { Store.HistoryEnabled = value; Raise(); RaiseStats(); } }
    public bool StyleEnabled { get => Store.StyleEnabled; set { Store.StyleEnabled = value; Raise(); } }
    public string StyleNote => Mode == TranscriptionMode.Verbatim ? "Off in Verbatim mode, which keeps every word exactly" : "Applies in Smart mode";
    public WritingStyle StyleFor(StyleCategory c) => Store.StyleFor(c);
    public void SetStyle(WritingStyle s, StyleCategory c) { Store.SetStyle(s, c); Raise("Styles"); }

    public bool BubbleEnabled { get => Store.BubbleEnabled; set { Store.BubbleEnabled = value; Raise(); } }
    public double BubbleSize { get => Store.BubbleSize; set { Store.BubbleSize = value; Raise(); } }
    public double BubbleOpacity { get => Store.BubbleOpacity; set { Store.BubbleOpacity = value; Raise(); } }

    public ObservableCollection<string> ExcludedApps { get; } = [];
    public void Exclude(string process)
    {
        var p = AppCategories.Normalize(process);
        if (p.Length == 0 || Store.ExcludedApps.Contains(p)) return;
        Store.ExcludedApps = [.. Store.ExcludedApps, p];
        ExcludedApps.Add(p);
    }
    public void Include(string process)
    {
        Store.ExcludedApps = Store.ExcludedApps.Where(x => x != process).ToList();
        ExcludedApps.Remove(process);
    }

    public SnoozeChoice SnoozeChoice { get => Store.SnoozeChoice; set { Store.SnoozeChoice = value; Raise(); } }
    public bool Snoozed => Store.SnoozeActive();
    public string SnoozeLabel => Store.SnoozeUntil == -1 ? "Snoozed until Fluent restarts"
        : $"Snoozed until {DateTimeOffset.FromUnixTimeMilliseconds((long)(Store.SnoozeUntil * 1000)).ToLocalTime():t}";
    public void Snooze(SnoozeChoice? c = null) { Store.Snooze(c ?? SnoozeChoice); RaiseSnooze(); }
    public void Resume() { Store.ClearSnooze(); RaiseSnooze(); }
    void RaiseSnooze() { Raise(nameof(Snoozed)); Raise(nameof(SnoozeLabel)); Raise(nameof(Status)); Raise(nameof(Headline)); }

    public HoldKey HoldKey { get => Store.HoldKey; set { Store.HoldKey = value; Raise(); RaiseShortcuts(); } }
    public ToggleShortcut ToggleShortcut { get => Store.ToggleShortcut; set { Store.ToggleShortcut = value; Raise(); RaiseShortcuts(); } }
    public string ShortcutSummary => $"Hold {HoldKey.Label()} to talk · {ToggleShortcut.Label} to start and stop";
    void RaiseShortcuts() { Raise(nameof(ShortcutSummary)); ShortcutsChanged?.Invoke(); }

    public bool SoundsEnabled { get => Store.SoundsEnabled; set { Store.SoundsEnabled = value; Raise(); } }

    bool launchAtLogin;
    public bool LaunchAtLoginEnabled
    {
        get => launchAtLogin;
        set
        {
            LaunchAtLoginError = Demo is null ? LaunchAtLogin.Set(value) : null;
            launchAtLogin = Demo is null ? LaunchAtLogin.Enabled : value;
            Raise(); Raise(nameof(LaunchAtLoginError));
        }
    }
    public string? LaunchAtLoginError { get; private set; }

    // permissions and key

    Recorder.MicState mic = Recorder.MicState.Allowed;
    public Recorder.MicState Mic { get => mic; private set { if (Set(ref mic, value)) { Raise(nameof(MicAllowed)); Raise(nameof(Ready)); Raise(nameof(Status)); } } }
    public bool MicAllowed => Mic == Recorder.MicState.Allowed;
    bool frozen;

    /// <summary>Demo screenshots of a set-up PC: the CI machine has no microphone or key of its own.</summary>
    public void PretendReady()
    {
        frozen = true;
        mic = Recorder.MicState.Allowed;
        hasApiKey = true;
        maskedKey = "AIza••••••••x9Qk";
        RaiseAll();
    }

    public void RefreshPermissions()
    {
        if (frozen) return;
        Mic = Recorder.Check();
        if (Store.SnoozeUntil > 0 && !Snoozed) { Store.ClearSnooze(); RaiseSnooze(); }
    }

    bool hasApiKey;
    string maskedKey;
    public bool HasApiKey => hasApiKey;
    public string MaskedKey => maskedKey;
    public bool Ready => MicAllowed && HasApiKey;

    public void SaveApiKey(string key)
    {
        var trimmed = key.Trim();
        if (Demo is null) { if (trimmed.Length == 0) KeyStore.Delete(); else KeyStore.Save(trimmed); }
        hasApiKey = trimmed.Length > 0;
        maskedKey = Mask(trimmed);
        Raise(nameof(HasApiKey)); Raise(nameof(MaskedKey)); Raise(nameof(Ready)); Raise(nameof(Status));
    }

    public string? ApiKey => Demo is null ? KeyStore.Load() : null;

    public Task<TranscriptionResult> TestApiKeyAsync() => new GeminiClient(ApiKey ?? "").TestConnectionAsync();

    static string Mask(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 8) return "••••";
        return key[..4] + new string('•', 8) + key[^4..];
    }

    public (string Text, string Kind) Status =>
        !TermsAccepted ? ("Open Fluent to get started", "warn")
        : Mic == Recorder.MicState.BlockedByWindows ? ("Microphone blocked", "danger")
        : Mic == Recorder.MicState.NoDevice ? ("No microphone", "danger")
        : !HasApiKey ? ("API key needed", "warn")
        : Snoozed ? ("Snoozed", "faint")
        : ("Ready", "ok");

    public string Headline => Dictation.Phase switch
    {
        DictationPhase.Recording => "Listening.",
        DictationPhase.Paused => "Paused.",
        DictationPhase.Transcribing => "Writing it down.",
        _ => Snoozed ? "Snoozed." : "Ready when you are.",
    };

    // history

    public ObservableCollection<Transcript> History { get; } = [];

    public void Record(Transcript t)
    {
        if (!HistoryEnabled) return;
        HistoryStore.Add(t);
        History.Insert(0, t);
        RaiseStats();
    }

    public void Delete(Transcript t)
    {
        HistoryStore.Delete(t.Id);
        History.Remove(t);
        RaiseStats();
    }

    public void ClearHistory()
    {
        HistoryStore.Clear();
        History.Clear();
        RaiseStats();
    }

    public int WordsToday => History.Where(t => t.CreatedAt.LocalDateTime.Date == DateTime.Today).Sum(t => t.WordCount);
    public int DictationsToday => History.Count(t => t.CreatedAt.LocalDateTime.Date == DateTime.Today);
    void RaiseStats() { Raise(nameof(WordsToday)); Raise(nameof(DictationsToday)); }

    /// <summary>Only used by --demo, so the History screenshot has something to show.</summary>
    public void SeedDemoHistory()
    {
        var now = DateTimeOffset.Now;
        (string, double, double)[] samples =
        [
            ("Hey team, the new build is up. Can everyone test the onboarding flow before Friday?", 0, 6.2),
            ("Hi Sam,\n\nThanks for the notes. I'll send the updated deck tomorrow morning.\n\nBest,\nAlex", 3600, 8.4),
            ("Pick up oat milk, coffee beans and something for dinner", 7200, 3.1),
            ("Remind me to book the dentist for next week", 90_000, 2.7),
        ];
        foreach (var (text, ago, secs) in samples.Reverse())
            HistoryStore.Add(new Transcript(Guid.NewGuid(), text, now.AddSeconds(-ago), secs));
        History.Clear();
        foreach (var t in HistoryStore.All()) History.Add(t);
        RaiseStats();
    }

    public void LoadCollections()
    {
        Vocabulary.Clear();
        foreach (var w in Store.Vocabulary) Vocabulary.Add(w);
        ExcludedApps.Clear();
        foreach (var p in Store.ExcludedApps) ExcludedApps.Add(p);
    }

    public void NotifyDictation() { Raise(nameof(Headline)); }

    public static string DataDir(string? demo) => demo is null
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fluent")
        : Path.Combine(Path.GetTempPath(), "Fluent-demo-" + Guid.NewGuid().ToString("N"));
}
