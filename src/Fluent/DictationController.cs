using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Fluent.Core;

namespace Fluent;

public enum DictationPhase { Idle, Recording, Paused, Transcribing, Done, Error }

public enum DictationSource { Bubble, BubbleHold, HoldKey, Toggle, Tray, InApp }

/// <summary>One dictation at a time, from start to inserted text: the Mac's <c>DictationController</c>
/// (Android's <c>DictationEngine</c> + <c>BubbleService</c> session handling). The capsule draws this state.
/// Everything here runs on the UI thread.</summary>
public sealed class DictationController : Observable
{
    readonly AppModel model;
    readonly Recorder recorder = new();
    readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer ticker;
    readonly Stopwatch run = new();
    TimeSpan accumulated;
    FocusedField? target;
    StyleCategory category = StyleCategory.Other;
    GeminiLiveClient? live;
    CancellationTokenSource? endAfter;
    int generation;

    /// <summary>Test hooks only: point the Live and batch requests at a local mock server.</summary>
    public static Uri? LiveUrlOverride { get; set; }
    public static Uri? BatchUrlOverride { get; set; }

    public DictationController(AppModel model)
    {
        this.model = model;
        ticker = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = TimeSpan.FromMilliseconds(100) };
        ticker.Tick += (_, _) => Tick();
        recorder.OnLevel = level => ui.BeginInvoke(() => Push(level));
    }

    DictationPhase phase = DictationPhase.Idle;
    public DictationPhase Phase
    {
        get => phase;
        private set { if (Set(ref phase, value)) { Raise(nameof(IsLive)); Raise(nameof(IsBusy)); Raise(nameof(Label)); model.NotifyDictation(); PhaseChanged?.Invoke(); } }
    }
    public event Action? PhaseChanged;

    public string Message { get; private set; } = "";
    public DictationSource Source { get; private set; } = DictationSource.Toggle;
    public bool IsLive => Phase is DictationPhase.Recording or DictationPhase.Paused;
    public bool IsBusy => Phase != DictationPhase.Idle;
    public TimeSpan Elapsed { get; private set; }
    public string ElapsedText => $"{(int)Elapsed.TotalMinutes}:{Elapsed.Seconds:00}";
    /// <summary>Newest microphone levels, oldest first, for the waveform.</summary>
    public double[] Levels { get; private set; } = Enumerable.Repeat(0.08, 18).ToArray();
    public string? LastText { get; private set; }
    public TranscriptionError? LastError { get; private set; }
    public string? TargetAppName { get; private set; }
    public string? LastRoute { get; private set; }
    public double LastLatencyMs { get; private set; }
    public string? LastInsert { get; private set; }

    public string Label => Phase switch
    {
        DictationPhase.Recording => "Listening",
        DictationPhase.Paused => "Paused",
        DictationPhase.Transcribing => "Writing it up…",
        DictationPhase.Done => Message.Length == 0 ? "Inserted" : Message,
        DictationPhase.Error => Message.Length == 0 ? "Something went wrong" : Message,
        _ => "",
    };

    /// <summary>Starts listening. <paramref name="field"/> is the text box the words are for (null for in-app).</summary>
    public void Start(DictationSource source, FocusedField? field)
    {
        if (IsLive || Phase == DictationPhase.Transcribing) return;
        endAfter?.Cancel();
        if (!model.TermsAccepted) return;
        if (field?.Kind == FieldKind.Secure) return;   // never listen for a password field

        Source = source;
        target = field;
        TargetAppName = field?.AppName;
        category = field?.Category ?? StyleCategory.Other;
        LastError = null;
        Raise(nameof(LastError));

        var key = model.ApiKey;
        if (string.IsNullOrEmpty(key) && LiveUrlOverride is null) { Fail(TranscriptionError.NoApiKey); return; }
        key ??= "test-key";

        // Open the live socket first so it is coming up while the microphone starts. Audio captured
        // before it is ready is queued inside the client and sent in order.
        var client = new GeminiLiveClient(key, model.Store.Mode, model.Store.LanguageCodes, model.Store.Vocabulary, LiveUrlOverride);
        live?.Close();
        live = client;
        recorder.OnChunk = pcm => client.Send(pcm);
        client.Connect();
        try
        {
            recorder.Start();
        }
        catch (Recorder.StartException e)
        {
            Log.Write("mic: " + e.State + " " + e.Message);
            model.RefreshPermissions();
            Fail(e.State == Recorder.MicState.BlockedByWindows ? TranscriptionError.MicPermission : TranscriptionError.MicUnavailable);
            return;
        }
        accumulated = TimeSpan.Zero;
        run.Restart();
        Elapsed = TimeSpan.Zero;
        Levels = Enumerable.Repeat(0.08, Levels.Length).ToArray();
        Raise(nameof(Levels)); Raise(nameof(Elapsed)); Raise(nameof(ElapsedText));
        Phase = DictationPhase.Recording;
        if (model.SoundsEnabled) Sounds.Start();
        ticker.Start();
        Log.Write($"start via {source} into {field?.Process ?? "(fluent)"} ({category})");
    }

    public void TogglePause()
    {
        switch (Phase)
        {
            case DictationPhase.Recording:
                recorder.Pause();
                accumulated += run.Elapsed;
                run.Reset();
                Phase = DictationPhase.Paused;
                break;
            case DictationPhase.Paused:
                recorder.Resume();
                run.Restart();
                Phase = DictationPhase.Recording;
                break;
        }
    }

    /// <summary>Ends the recording and writes the text into the field.</summary>
    public void Stop()
    {
        if (!IsLive) return;
        ticker.Stop();
        UpdateElapsed();
        var samples = recorder.Stop();
        if (model.SoundsEnabled) Sounds.Stop();
        var duration = Wav.Duration(samples.Length);
        if (duration < Constants.MinRecordingSeconds) { Fail(TranscriptionError.TooShort); return; }
        Phase = DictationPhase.Transcribing;
        var client = live;
        live = null;
        var key = model.ApiKey ?? "test-key";
        var store = model.Store;
        var mode = store.Mode;
        var languages = store.LanguageCodes;
        var vocabulary = store.Vocabulary;
        var gen = ++generation;
        var stoppedAt = Stopwatch.StartNew();
        _ = Task.Run(async () =>
        {
            // Live first: most of the transcript already arrived while the user was talking.
            if (client is not null && await client.FinishAsync() is { } text)
            {
                Note("live", stoppedAt);
                await ui.InvokeAsync(() => FinishAsync(TranscriptionResult.Ok(text), duration, gen)).Task.Unwrap();
                return;
            }
            if (client?.Failure is { Kind: TranscriptionErrorKind.InvalidApiKey or TranscriptionErrorKind.QuotaExceeded } f)
                Log.Write("live failed: " + f.Kind);
            // The socket never came up or failed: send the whole recording in one request.
            var result = await new GeminiClient(key, endpoint: BatchUrlOverride)
                .TranscribeAsync(Wav.Encode(samples), mode, languages, vocabulary);
            Note("batch", stoppedAt);
            await ui.InvokeAsync(() => FinishAsync(result, duration, gen)).Task.Unwrap();
        });
    }

    void Note(string route, Stopwatch since)
    {
        LastRoute = route;
        LastLatencyMs = since.Elapsed.TotalMilliseconds;
        Log.Write($"transcript via {route} {LastLatencyMs:0} ms after stop");
    }

    public void Cancel()
    {
        ticker.Stop();
        generation++;
        recorder.Stop();
        live?.Close();
        live = null;
        Phase = DictationPhase.Idle;
    }

    /// <summary>Bubble click, toggle shortcut and tray: start when idle, stop when listening.</summary>
    public void Toggle(DictationSource source, FocusedField? field)
    {
        if (IsLive) Stop(); else Start(source, field);
    }

    public void ClearLast()
    {
        LastText = null; LastError = null;
        Raise(nameof(LastText)); Raise(nameof(LastError));
    }

    async Task FinishAsync(TranscriptionResult result, double duration, int gen)
    {
        if (Phase != DictationPhase.Transcribing || gen != generation) return;   // cancelled meanwhile
        if (result.Error is { } error) { Fail(error); return; }
        var text = model.Store.ApplyStyle(result.Text!, category);
        LastText = text;
        Raise(nameof(LastText));
        model.Record(Transcript.New(text, duration));
        if (Source == DictationSource.InApp)
        {
            Done("Done", 0.75);
            return;
        }
        var outcome = await TextInserter.InsertAsync(text, target);
        LastInsert = outcome.Kind + ": " + outcome.Detail;
        Log.Write("insert " + outcome.Kind + (outcome.Kind == InsertKind.Failed ? " (" + outcome.Detail + ")" : ""));
        if (outcome.Kind != InsertKind.Failed)
        {
            Done("Inserted", 0.75);
        }
        else
        {
            // Never lose a transcript: it stays on the clipboard to paste by hand.
            await TextInserter.SetClipboardAsync(text, transient: false);
            Done("Copied — press Ctrl+V to paste", 2.2);
        }
    }

    void Done(string message, double seconds)
    {
        Message = message;
        Phase = DictationPhase.Done;
        Raise(nameof(Label));
        EndAfter(seconds);
    }

    void Fail(TranscriptionError error)
    {
        ticker.Stop();
        recorder.Stop();
        live?.Close();
        live = null;
        LastError = error;
        Raise(nameof(LastError));
        Message = error.UserMessage;
        Log.Write("error " + error.Kind);
        if (Phase == DictationPhase.Error) Raise(nameof(Label));
        Phase = DictationPhase.Error;
        EndAfter(2.6);
    }

    void EndAfter(double seconds)
    {
        endAfter?.Cancel();
        var cts = endAfter = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            ui.BeginInvoke(() => { if (!IsLive && Phase != DictationPhase.Transcribing) Phase = DictationPhase.Idle; });
        }, TaskScheduler.Default);
    }

    void Tick()
    {
        UpdateElapsed();
        if (Elapsed > Constants.MaxRecording) Stop();
    }

    void UpdateElapsed()
    {
        Elapsed = accumulated + run.Elapsed;
        Raise(nameof(Elapsed)); Raise(nameof(ElapsedText));
    }

    void Push(float level)
    {
        if (Phase != DictationPhase.Recording) return;
        var l = Levels.Skip(1).Append(Math.Clamp(level, 0.06, 1)).ToArray();
        Levels = l;
        Raise(nameof(Levels));
    }

    // demo

    /// <summary>--demo capsule-*: shows a phase with a synthetic level so the capsule can be captured on a
    /// machine with no microphone. Never used in normal runs.</summary>
    public void ShowDemo(DictationPhase p, string message = "")
    {
        Message = message;
        Elapsed = TimeSpan.FromSeconds(7);
        Levels = [0.2, 0.45, 0.8, 0.55, 0.95, 0.6, 0.35, 0.7, 0.5, 0.85, 0.4, 0.65, 0.3, 0.9, 0.55, 0.45, 0.7, 0.35];
        Phase = p;
        Raise(nameof(Label)); Raise(nameof(Levels)); Raise(nameof(ElapsedText));
        if (p == DictationPhase.Recording)
        {
            var clock = 0.0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            t.Tick += (_, _) =>
            {
                clock += 0.08;
                Levels = Levels.Skip(1).Append(0.35 + 0.5 * Math.Abs(Math.Sin(clock * 2.3) * Math.Cos(clock * 0.7))).ToArray();
                Elapsed = TimeSpan.FromSeconds(7 + clock);
                Raise(nameof(Levels)); Raise(nameof(ElapsedText));
            };
            t.Start();
        }
    }
}
