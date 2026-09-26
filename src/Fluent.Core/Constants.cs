namespace Fluent.Core;

/// <summary>Same values as the Mac and iPhone apps' <c>Constants</c>.</summary>
public static class Constants
{
    // Audio
    public const int SampleRate = 16_000;
    public static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(9);
    public const double MinRecordingSeconds = 0.35;

    // Gemini
    public const string BatchModel = "gemini-3.5-transcribe";
    public const string LiveModel = "gemini-3.5-transcribe-live";
    public static readonly Uri InteractionsUrl = new("https://generativelanguage.googleapis.com/v1beta/interactions");
    public static readonly Uri ModelsUrl = new("https://generativelanguage.googleapis.com/v1beta/models/");
    public const string ApiRevision = "2026-05-20";
    public static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(60);
    public static readonly Uri LiveUrl = new("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent");
    /// <summary>6 s for the socket to come up and 6 s to finalise after stop (normally well under a
    /// second); past either, the whole recording goes through the batch request instead.</summary>
    public static readonly TimeSpan LiveConnectTimeout = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan LiveFinalizeTimeout = TimeSpan.FromSeconds(6);

    // Legal. Bump TermsVersion whenever the Terms change materially: everyone is asked to accept again.
    public const string TermsVersion = "2026-09-24";
    public const string TermsUrl = "https://fluent-voice-v2.vercel.app/terms";
    public const string PrivacyUrl = "https://fluent-voice-v2.vercel.app/privacy";
    public const string ApiKeyUrl = "https://aistudio.google.com/apikey";
    public const string WebsiteUrl = "https://fluent-voice-v2.vercel.app";
}

public enum TranscriptionMode { Smart, Verbatim }

public static class TranscriptionModeInfo
{
    public static string Label(this TranscriptionMode m) => m == TranscriptionMode.Smart ? "Smart" : "Verbatim";
    public static string Detail(this TranscriptionMode m) => m == TranscriptionMode.Smart
        ? "Removes filler words, fixes self-corrections, adds punctuation"
        : "Writes exactly what you said";
    public static string Raw(this TranscriptionMode m) => m == TranscriptionMode.Smart ? "SMART" : "VERBATIM";
    public static TranscriptionMode Parse(string? raw) => raw == "VERBATIM" ? TranscriptionMode.Verbatim : TranscriptionMode.Smart;
}

public enum TranscriptionErrorKind
{
    NoApiKey, InvalidApiKey, QuotaExceeded, NoNetwork, ConnectionLost,
    MicUnavailable, MicPermission, NoSpeech, TooShort, Server, Unknown,
}

public sealed record TranscriptionError(TranscriptionErrorKind Kind, int Code = 0, string Detail = "")
{
    public static readonly TranscriptionError NoApiKey = new(TranscriptionErrorKind.NoApiKey);
    public static readonly TranscriptionError InvalidApiKey = new(TranscriptionErrorKind.InvalidApiKey);
    public static readonly TranscriptionError QuotaExceeded = new(TranscriptionErrorKind.QuotaExceeded);
    public static readonly TranscriptionError NoNetwork = new(TranscriptionErrorKind.NoNetwork);
    public static readonly TranscriptionError ConnectionLost = new(TranscriptionErrorKind.ConnectionLost);
    public static readonly TranscriptionError MicUnavailable = new(TranscriptionErrorKind.MicUnavailable);
    public static readonly TranscriptionError MicPermission = new(TranscriptionErrorKind.MicPermission);
    public static readonly TranscriptionError NoSpeech = new(TranscriptionErrorKind.NoSpeech);
    public static readonly TranscriptionError TooShort = new(TranscriptionErrorKind.TooShort);
    public static TranscriptionError Server(int code, string detail) => new(TranscriptionErrorKind.Server, code, detail);
    public static TranscriptionError Unknown(string detail) => new(TranscriptionErrorKind.Unknown, 0, detail);

    public string UserMessage => Kind switch
    {
        TranscriptionErrorKind.NoApiKey => "Add your Gemini API key in Settings first.",
        TranscriptionErrorKind.InvalidApiKey => "Gemini rejected the API key. Check it in Settings.",
        TranscriptionErrorKind.QuotaExceeded => "Gemini quota or rate limit reached. Try again shortly.",
        TranscriptionErrorKind.NoNetwork => "No internet connection.",
        TranscriptionErrorKind.ConnectionLost => "Lost the connection to Gemini.",
        TranscriptionErrorKind.MicUnavailable => "Microphone is unavailable. Another app may be using it.",
        TranscriptionErrorKind.MicPermission => "Windows is blocking the microphone for Fluent.",
        TranscriptionErrorKind.NoSpeech => "Didn't catch any speech.",
        TranscriptionErrorKind.TooShort => "That was too short to transcribe.",
        TranscriptionErrorKind.Server => $"Gemini returned an error ({Code}).",
        _ => "Transcription failed.",
    };
}

/// <summary>Success or failure of a transcription, like Swift's <c>Result&lt;String, TranscriptionError&gt;</c>.</summary>
public readonly record struct TranscriptionResult(string? Text, TranscriptionError? Error)
{
    public static TranscriptionResult Ok(string text) => new(text, null);
    public static TranscriptionResult Fail(TranscriptionError e) => new(null, e);
    public bool IsSuccess => Error is null;
}
