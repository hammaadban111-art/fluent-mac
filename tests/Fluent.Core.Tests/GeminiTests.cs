using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Fluent.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Fluent.Core.Tests;

/// <summary>An in-process stand-in for the Gemini Live WebSocket, behaving like the Mac's
/// mock_gemini_live.py: setupComplete after 0.4 s, a final chunk per second of audio, and the rest plus
/// turnComplete after audioStreamEnd. It records the audio bytes and message order.</summary>
sealed class MockLive : IAsyncDisposable
{
    public enum Mode { Normal, Reject, NoSetup, Stall }
    static readonly string[] Words = "sounds good are you free for lunch tomorrow let's do twelve if that works".Split(' ');

    readonly WebApplication app;
    public Uri Url { get; }
    public List<string> Order { get; } = [];
    public JsonObject? Setup { get; private set; }
    public string? Query { get; private set; }
    public string? Mime { get; private set; }
    public MemoryStream Audio { get; } = new();
    public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MockLive(Mode mode)
    {
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        app = b.Build();
        app.UseWebSockets();
        app.Run(async ctx =>
        {
            Query = ctx.Request.QueryString.Value;
            if (mode == Mode.Reject) { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsync("{\"error\":\"API key not valid\"}"); return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            try { await Serve(ws, mode); } catch { }
            Done.TrySetResult();
        });
        app.StartAsync().GetAwaiter().GetResult();
        var http = new Uri(app.Urls.First());
        Url = new Uri($"ws://127.0.0.1:{http.Port}/ws");
    }

    async Task Serve(WebSocket ws, Mode mode)
    {
        Setup = JsonNode.Parse(await Receive(ws) ?? "{}") as JsonObject;
        if (mode != Mode.NoSetup) { await Task.Delay(400); await SendJson(ws, new JsonObject { ["setupComplete"] = new JsonObject() }); }
        int sent = 0; long nextMark = 32000;
        while (await Receive(ws) is { } raw)
        {
            var rt = (JsonNode.Parse(raw) as JsonObject)?["realtimeInput"] as JsonObject;
            if (rt is null) continue;
            if (rt.ContainsKey("activityStart")) Order.Add("activityStart");
            if (rt["audio"] is JsonObject audio)
            {
                if (Order.Count == 0 || Order[^1] != "audio") Order.Add("audio");
                Mime = audio["mimeType"]!.GetValue<string>();
                var bytes = Convert.FromBase64String(audio["data"]!.GetValue<string>());
                Audio.Write(bytes);
                if (Audio.Length >= nextMark && sent < Words.Length - 2)
                {
                    nextMark += 32000;
                    await SendChunk(ws, string.Join(" ", Words[sent..(sent + 2)]));
                    sent += 2;
                }
            }
            if (rt.ContainsKey("activityEnd")) Order.Add("activityEnd");
            if (rt["audioStreamEnd"] is not null)
            {
                Order.Add("audioStreamEnd");
                if (mode == Mode.Stall) continue;
                await Task.Delay(300);
                await SendChunk(ws, string.Join(" ", Words[sent..]));
                await SendJson(ws, new JsonObject { ["serverContent"] = new JsonObject { ["turnComplete"] = true } });
            }
        }
    }

    static Task SendChunk(WebSocket ws, string text) =>
        SendJson(ws, new JsonObject { ["serverContent"] = new JsonObject { ["inputTranscription"] = new JsonObject { ["text"] = text } } });

    static Task SendJson(WebSocket ws, JsonObject o) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(o.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

    static async Task<string?> Receive(WebSocket ws)
    {
        var buf = new byte[1 << 16];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, CancellationToken.None);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buf, 0, r.Count);
            if (r.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}

public class GeminiLiveClientTests
{
    /// <summary>Four seconds of a 440 Hz tone, the shape of real microphone chunks (100 ms each).</summary>
    static short[] Tone(int seconds = 4) =>
        Enumerable.Range(0, Constants.SampleRate * seconds)
            .Select(i => (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / Constants.SampleRate))).ToArray();

    [Fact]
    public async Task StreamsWhileTalkingAndReturnsTheFinalTranscript()
    {
        await using var mock = new MockLive(MockLive.Mode.Normal);
        using var client = new GeminiLiveClient("test-key", TranscriptionMode.Smart, ["en-US"], ["Fluent"], mock.Url);
        client.Connect();
        var pcm = Tone();
        // Sent in real-time sized chunks from the start, so some audio is queued before setupComplete.
        for (var i = 0; i < pcm.Length; i += 1600)
        {
            client.Send(pcm.AsSpan(i, Math.Min(1600, pcm.Length - i)));
            if (i % 16000 == 0) await Task.Delay(120);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var text = await client.FinishAsync();
        Assert.Equal("sounds good are you free for lunch tomorrow let's do twelve if that works", text);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms after stop");
        await mock.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Every byte arrived, in order.
        var sent = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, sent, 0, sent.Length);
        Assert.Equal(SHA256.HashData(sent), SHA256.HashData(mock.Audio.ToArray()));
        Assert.Equal(["activityStart", "audio", "activityEnd", "audioStreamEnd"], mock.Order);
        Assert.Equal("audio/pcm;rate=16000", mock.Mime);
        Assert.Contains("key=test-key", mock.Query);
        var setup = mock.Setup!["setup"]!;
        Assert.Equal("models/gemini-3.5-transcribe-live", setup["model"]!.GetValue<string>());
        Assert.True(setup["realtimeInputConfig"]!["automaticActivityDetection"]!["disabled"]!.GetValue<bool>());
        Assert.Equal("SMART", setup["inputAudioTranscription"]!["mode"]!.GetValue<string>());
        Assert.Equal("Fluent", setup["inputAudioTranscription"]!["customVocabulary"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task RejectedKeyFallsBackAndSaysWhy()
    {
        await using var mock = new MockLive(MockLive.Mode.Reject);
        using var client = new GeminiLiveClient("bad", TranscriptionMode.Smart, [], [], mock.Url);
        client.Connect();
        client.Send(Tone(1));
        await Task.Delay(800);
        Assert.Null(await client.FinishAsync());
        Assert.Equal(TranscriptionErrorKind.InvalidApiKey, client.Failure?.Kind);
    }

    [Fact]
    public async Task NoSetupMeansBatchFallback()
    {
        await using var mock = new MockLive(MockLive.Mode.NoSetup);
        using var client = new GeminiLiveClient("k", TranscriptionMode.Smart, [], [], mock.Url, TimeSpan.FromSeconds(1));
        client.Connect();
        client.Send(Tone(1));
        await Task.Delay(1500);
        Assert.Null(await client.FinishAsync());
        Assert.Equal(TranscriptionErrorKind.ConnectionLost, client.Failure?.Kind);
    }

    [Fact]
    public async Task StalledTurnNeverReturnsAPartialTranscript()
    {
        await using var mock = new MockLive(MockLive.Mode.Stall);
        using var client = new GeminiLiveClient("k", TranscriptionMode.Verbatim, [], [], mock.Url);
        client.Connect();
        client.Send(Tone(3));
        await Task.Delay(1000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await client.FinishAsync(TimeSpan.FromSeconds(1)));
        Assert.InRange(sw.ElapsedMilliseconds, 800, 3000);
    }

    [Fact]
    public async Task NothingListeningFailsFast()
    {
        using var client = new GeminiLiveClient("k", TranscriptionMode.Smart, [], [], new Uri("ws://127.0.0.1:9/nothing"), TimeSpan.FromSeconds(2));
        client.Connect();
        await Task.Delay(500);
        Assert.Null(await client.FinishAsync());
    }

    [Fact]
    public void ServerMessagesAreParsed()
    {
        using var client = new GeminiLiveClient("k", TranscriptionMode.Smart, [], [], new Uri("ws://127.0.0.1:9/"));
        string? interim = null;
        client.OnInterim = t => interim = t;
        client.Handle("""{"serverContent":{"interimInputTranscription":{"text":"hel"}}}"""u8.ToArray());
        Assert.Equal("hel", interim);
        client.Handle("""{"error":{"code":429,"message":"Resource exhausted"}}"""u8.ToArray());
        Assert.Equal(TranscriptionErrorKind.QuotaExceeded, client.Failure?.Kind);
    }
}

public class GeminiClientTests
{
    [Fact]
    public async Task RequestMatchesTheOtherApps()
    {
        using var req = GeminiClient.TranscribeRequest("k", [1, 2, 3], TranscriptionMode.Verbatim, ["en-GB"], ["Fluent"]);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("k", req.Headers.GetValues("x-goog-api-key").Single());
        Assert.Equal(Constants.ApiRevision, req.Headers.GetValues("Api-Revision").Single());
        var body = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
        Assert.Equal("gemini-3.5-transcribe", body["model"]!.GetValue<string>());
        Assert.Equal("AQID", body["input"]![0]!["data"]!.GetValue<string>());
        Assert.Equal("audio/wav", body["input"]![0]!["mime_type"]!.GetValue<string>());
        var cfg = body["generation_config"]!["transcription_config"]!;
        Assert.Equal("verbatim", cfg["mode"]!["type"]!.GetValue<string>());
        Assert.Equal("en-GB", cfg["language_codes"]![0]!.GetValue<string>());
        Assert.Equal("Fluent", cfg["custom_vocabulary"]![0]!.GetValue<string>());

        using var smart = GeminiClient.TranscribeRequest("k", [1], TranscriptionMode.Smart, [], []);
        var s = JsonNode.Parse(await smart.Content!.ReadAsStringAsync())!["generation_config"]!["transcription_config"]!;
        Assert.Equal("smart", s["mode"]!.GetValue<string>());
        Assert.Null(s["custom_vocabulary"]);
    }

    [Fact]
    public void ResponseShapes()
    {
        Assert.Equal("hello there", GeminiClient.ExtractText(
            """{"steps":[{"content":[{"type":"text","text":"hello"},{"type":"thought","text":"x"}]},{"content":[{"type":"text","text":"there"}]}]}"""u8.ToArray()));
        Assert.Equal("direct", GeminiClient.ExtractText("""{"output_text":"direct"}"""u8.ToArray()));
        Assert.Equal("ab", GeminiClient.ExtractText("""[{"candidates":[{"content":{"parts":[{"text":"a"},{"text":"b"}]}}]}]"""u8.ToArray()));
        Assert.Null(GeminiClient.ExtractText("""{"steps":[]}"""u8.ToArray()));
        Assert.Null(GeminiClient.ExtractText("not json"u8.ToArray()));
    }

    [Fact]
    public void ErrorMapping()
    {
        Assert.Equal(TranscriptionErrorKind.InvalidApiKey, GeminiClient.MapHttpError(400,
            """{"error":{"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}"""u8.ToArray()).Kind);
        Assert.Equal(TranscriptionErrorKind.InvalidApiKey, GeminiClient.MapHttpError(400,
            """{"error":{"message":"x","details":[{"reason":"API_KEY_INVALID"}]}}"""u8.ToArray()).Kind);
        Assert.Equal(TranscriptionErrorKind.QuotaExceeded, GeminiClient.MapHttpError(429, "{}"u8.ToArray()).Kind);
        Assert.Equal(TranscriptionErrorKind.QuotaExceeded, GeminiClient.MapHttpError(400,
            """{"error":{"status":"RESOURCE_EXHAUSTED"}}"""u8.ToArray()).Kind);
        Assert.Equal(TranscriptionErrorKind.InvalidApiKey, GeminiClient.MapHttpError(403, "{}"u8.ToArray()).Kind);
        var server = GeminiClient.MapHttpError(500, """{"error":{"message":"boom"}}"""u8.ToArray());
        Assert.Equal((TranscriptionErrorKind.Server, 500, "boom"), (server.Kind, server.Code, server.Detail));
    }

    [Fact]
    public async Task RoundTripAgainstALocalServer()
    {
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = b.Build();
        string? seenKey = null;
        app.Run(async ctx =>
        {
            seenKey = ctx.Request.Headers["x-goog-api-key"];
            if (seenKey == "bad") { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsync("""{"error":{"message":"API key not valid"}}"""); return; }
            await ctx.Response.WriteAsync("""{"steps":[{"content":[{"type":"text","text":"  Hello from the mock.  "}]}]}""");
        });
        await app.StartAsync();
        var url = new Uri(app.Urls.First() + "/v1beta/interactions");

        var ok = await new GeminiClient("good", endpoint: url).TranscribeAsync(Wav.Encode(new short[1600]), TranscriptionMode.Smart, [], []);
        Assert.Equal("Hello from the mock.", ok.Text);
        Assert.Equal("good", seenKey);
        var bad = await new GeminiClient("bad", endpoint: url).TranscribeAsync([], TranscriptionMode.Smart, [], []);
        Assert.Equal(TranscriptionErrorKind.InvalidApiKey, bad.Error?.Kind);
        var none = await new GeminiClient("").TranscribeAsync([], TranscriptionMode.Smart, [], []);
        Assert.Equal(TranscriptionErrorKind.NoApiKey, none.Error?.Kind);
        var offline = await new GeminiClient("k", endpoint: new Uri("http://127.0.0.1:9/x")).TranscribeAsync([], TranscriptionMode.Smart, [], []);
        Assert.Equal(TranscriptionErrorKind.NoNetwork, offline.Error?.Kind);
    }
}
