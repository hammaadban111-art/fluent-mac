using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Fluent.Core;

/// <summary>Streams microphone audio to <c>gemini-3.5-transcribe-live</c> over the Live API WebSocket while
/// the user is still talking, so the transcript is (almost) ready the moment they stop. A port of the
/// Android and Mac <c>GeminiLiveClient</c>: same setup message, manual turn boundaries, same
/// finalise-then-await flow.
///
/// Audio sent before the socket is ready is queued and flushed on <c>setupComplete</c>. If the socket never
/// gets ready, or fails, <see cref="FinishAsync"/> returns null and the caller falls back to the batch
/// request with the full recording, so a turn is never lost.</summary>
public sealed class GeminiLiveClient : IDisposable
{
    public Uri Url { get; }
    readonly string apiKey;
    readonly TranscriptionMode mode;
    readonly IReadOnlyList<string> languageCodes;
    readonly IReadOnlyList<string> vocabulary;
    readonly TimeSpan connectTimeout;

    readonly object gate = new();
    readonly ClientWebSocket socket = new();
    readonly Channel<string> outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    readonly CancellationTokenSource life = new();
    bool ready, closed, activityStarted, turnDone, completedNormally;
    readonly List<byte[]> queued = [];
    readonly StringBuilder finals = new();
    TaskCompletionSource<bool>? waiter;

    public TranscriptionError? Failure { get; private set; }
    /// <summary>Latest interim (not yet final) text, for display.</summary>
    public Action<string>? OnInterim { get; set; }

    public GeminiLiveClient(string apiKey, TranscriptionMode mode, IReadOnlyList<string> languageCodes,
        IReadOnlyList<string> vocabulary, Uri? url = null, TimeSpan? connectTimeout = null)
    {
        this.apiKey = apiKey;
        this.mode = mode;
        this.languageCodes = languageCodes;
        this.vocabulary = vocabulary;
        Url = url ?? Constants.LiveUrl;
        this.connectTimeout = connectTimeout ?? Constants.LiveConnectTimeout;
        socket.Options.CollectHttpResponseDetails = true;
    }

    public bool IsReady { get { lock (gate) return ready && !closed; } }

    /// <summary>Opens the socket and sends the setup message. Returns immediately; audio can be sent at once.</summary>
    public void Connect()
    {
        var sep = string.IsNullOrEmpty(Url.Query) ? "?" : "&";
        var uri = new Uri(Url + sep + "key=" + Uri.EscapeDataString(apiKey));
        Enqueue(SetupMessage().ToJsonString());
        _ = Task.Run(async () =>
        {
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
                connectCts.CancelAfter(connectTimeout);
                await socket.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
            }
            catch
            {
                Fail(HttpFailure() ?? TranscriptionError.ConnectionLost);
                return;
            }
            _ = Task.Run(SendLoop);
            _ = Task.Run(ReceiveLoop);
        });
        // Give up on the socket if it is not ready in time; the batch fallback takes over.
        _ = Task.Delay(connectTimeout).ContinueWith(_ =>
        {
            bool isReady; lock (gate) isReady = ready;
            if (!isReady) Fail(TranscriptionError.ConnectionLost);
        }, TaskScheduler.Default);
    }

    /// <summary>Streams 16 kHz mono PCM16 samples. Safe to call from the audio thread.</summary>
    public void Send(ReadOnlySpan<short> pcm)
    {
        if (pcm.IsEmpty) return;
        var data = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm.ToArray(), 0, data, 0, data.Length);   // little-endian on every Windows PC
        bool flushNow;
        lock (gate)
        {
            if (closed) return;
            if (!ready) { queued.Add(data); return; }
            flushNow = true;
        }
        if (flushNow) Enqueue(AudioMessage(data).ToJsonString());
    }

    /// <summary>Ends the utterance and waits for the final transcript. Null means "use the batch fallback".</summary>
    public async Task<string?> FinishAsync(TimeSpan? timeout = null)
    {
        bool isReady, started, alreadyDone;
        lock (gate) { isReady = ready && !closed; started = activityStarted; alreadyDone = turnDone; }
        if (alreadyDone) { Close(); return NormalResult(); }
        if (!isReady) { Close(); return null; }
        if (started) Enqueue(new JsonObject { ["realtimeInput"] = new JsonObject { ["activityEnd"] = new JsonObject() } }.ToJsonString());
        Enqueue(new JsonObject { ["realtimeInput"] = new JsonObject { ["audioStreamEnd"] = true } }.ToJsonString());

        Task wait;
        lock (gate)
        {
            if (turnDone || closed) wait = Task.CompletedTask;
            else { waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); wait = waiter.Task; }
        }
        // Stalled: give up, the batch fallback takes over.
        await Task.WhenAny(wait, Task.Delay(timeout ?? Constants.LiveFinalizeTimeout)).ConfigureAwait(false);
        CompleteTurn();
        var text = NormalResult();
        Close();
        return text;
    }

    string? NormalResult()
    {
        lock (gate) return completedNormally && !string.IsNullOrWhiteSpace(finals.ToString()) ? finals.ToString() : null;
    }

    public void Close()
    {
        lock (gate)
        {
            if (closed) { return; }
            closed = true;
            queued.Clear();
        }
        outbox.Writer.TryComplete();
        _ = CloseSocketAsync(WebSocketCloseStatus.NormalClosure);
        CompleteTurn();
    }

    public void Dispose() => Close();

    // messages

    internal JsonObject SetupMessage()
    {
        var transcription = new JsonObject
        {
            ["mode"] = mode.Raw(),
            ["languageCodes"] = new JsonArray(languageCodes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
        };
        if (vocabulary.Count > 0)
            transcription["customVocabulary"] = new JsonArray(vocabulary.Take(1000).Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        return new JsonObject
        {
            ["setup"] = new JsonObject
            {
                ["model"] = $"models/{Constants.LiveModel}",
                ["generationConfig"] = new JsonObject { ["responseModalities"] = new JsonArray("TEXT") },
                ["realtimeInputConfig"] = new JsonObject { ["automaticActivityDetection"] = new JsonObject { ["disabled"] = true } },
                ["inputAudioTranscription"] = transcription,
            },
        };
    }

    internal static JsonObject AudioMessage(byte[] pcm) => new()
    {
        ["realtimeInput"] = new JsonObject
        {
            ["audio"] = new JsonObject
            {
                ["data"] = Convert.ToBase64String(pcm),
                ["mimeType"] = $"audio/pcm;rate={Constants.SampleRate}",
            },
        },
    };

    // socket

    void Enqueue(string text)
    {
        lock (gate) if (closed) return;
        outbox.Writer.TryWrite(text);
    }

    async Task SendLoop()
    {
        try
        {
            await foreach (var text in outbox.Reader.ReadAllAsync(life.Token).ConfigureAwait(false))
            {
                if (socket.State != WebSocketState.Open) break;
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, life.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch { Fail(TranscriptionError.ConnectionLost); }
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var r = await socket.ReceiveAsync(buffer, life.Token).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;
                Handle(message.ToArray());
                message.SetLength(0);
            }
            // The server closed the socket. If the turn had not completed, the batch fallback takes over.
            Fail(CloseFailure() ?? TranscriptionError.ConnectionLost);
        }
        catch (OperationCanceledException) { }
        catch { Fail(TranscriptionError.ConnectionLost); }
    }

    TranscriptionError? HttpFailure()
    {
        var code = (int)socket.HttpStatusCode;
        if (code < 300) return null;
        return code switch
        {
            400 or 401 or 403 => TranscriptionError.InvalidApiKey,
            429 => TranscriptionError.QuotaExceeded,
            _ => TranscriptionError.Server(code, ""),
        };
    }

    TranscriptionError? CloseFailure()
    {
        var reason = socket.CloseStatusDescription ?? "";
        if (reason.Contains("API key", StringComparison.OrdinalIgnoreCase)) return TranscriptionError.InvalidApiKey;
        if (reason.Contains("quota", StringComparison.OrdinalIgnoreCase)) return TranscriptionError.QuotaExceeded;
        return null;
    }

    /// <summary>Handles one server message. Internal so tests can feed recorded messages.</summary>
    internal void Handle(byte[] data)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(data) as JsonObject; } catch { return; }
        if (root is null) return;

        if (root.ContainsKey("setupComplete")) BecameReady();

        if (root["error"] is JsonObject error)
        {
            var code = error["code"] is JsonValue cv && cv.TryGetValue<int>(out var ci) ? ci
                : int.TryParse(GeminiClient.Str(error["code"]), out var cs) ? cs : 0;
            var message = GeminiClient.Str(error["message"]) ?? "";
            Fail(code == 429 || message.Contains("quota", StringComparison.OrdinalIgnoreCase) ? TranscriptionError.QuotaExceeded
                : code is 400 or 401 or 403 ? TranscriptionError.InvalidApiKey : TranscriptionError.Server(code, message));
            return;
        }

        if (root["serverContent"] is not JsonObject server) return;
        if (GeminiClient.Str((server["interimInputTranscription"] as JsonObject)?["text"]) is { Length: > 0 } interim)
            OnInterim?.Invoke(interim);
        if (GeminiClient.Str((server["inputTranscription"] as JsonObject)?["text"]) is { Length: > 0 } chunk)
        {
            lock (gate)
            {
                if (finals.Length > 0 && finals[^1] != ' ') finals.Append(' ');
                finals.Append(chunk.Trim(' ', '\t'));
            }
        }
        if (root.ContainsKey("generationComplete") || server.ContainsKey("generationComplete") || server.ContainsKey("turnComplete"))
        {
            lock (gate) if (!closed) completedNormally = true;
            CompleteTurn();
        }
    }

    /// <summary>On <c>setupComplete</c>: open the turn, then drain the queue. New audio keeps queueing until
    /// the queue is empty, so chunks always go out in the order they were recorded.</summary>
    void BecameReady()
    {
        lock (gate)
        {
            if (activityStarted || closed) return;
            activityStarted = true;
        }
        Enqueue(new JsonObject { ["realtimeInput"] = new JsonObject { ["activityStart"] = new JsonObject() } }.ToJsonString());
        while (true)
        {
            List<byte[]> batch;
            lock (gate)
            {
                if (closed) return;
                batch = [.. queued];
                queued.Clear();
                if (batch.Count == 0) ready = true;
            }
            if (batch.Count == 0) break;
            foreach (var chunk in batch) Enqueue(AudioMessage(chunk).ToJsonString());
        }
    }

    void Fail(TranscriptionError error)
    {
        lock (gate)
        {
            Failure ??= error;
            if (closed) { }
            else closed = true;
        }
        outbox.Writer.TryComplete();
        _ = CloseSocketAsync(WebSocketCloseStatus.EndpointUnavailable);
        CompleteTurn();
    }

    async Task CloseSocketAsync(WebSocketCloseStatus status)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseOutputAsync(status, null, cts.Token).ConfigureAwait(false);
            }
        }
        catch { }
        finally
        {
            life.Cancel();
            socket.Abort();
        }
    }

    void CompleteTurn()
    {
        TaskCompletionSource<bool>? w;
        lock (gate)
        {
            turnDone = true;
            w = waiter;
            waiter = null;
        }
        w?.TrySetResult(true);
    }
}
