using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fluent.Core;

/// <summary>Transcription through the unary Interactions endpoint with <c>gemini-3.5-transcribe</c>, the
/// same request the Android and Mac apps send as their fallback.</summary>
public sealed class GeminiClient(string apiKey, HttpClient? http = null, Uri? endpoint = null, Uri? modelsBase = null)
{
    static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly HttpClient http = http ?? Shared;
    readonly Uri endpoint = endpoint ?? Constants.InteractionsUrl;
    readonly Uri modelsBase = modelsBase ?? Constants.ModelsUrl;

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, TranscriptionMode mode,
        IReadOnlyList<string> languageCodes, IReadOnlyList<string> vocabulary, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiKey)) return TranscriptionResult.Fail(TranscriptionError.NoApiKey);
        using var request = TranscribeRequest(apiKey, wav, mode, languageCodes, vocabulary, endpoint);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Constants.BatchTimeout);
        try
        {
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            var code = (int)response.StatusCode;
            if (code is < 200 or >= 300) return TranscriptionResult.Fail(MapHttpError(code, body));
            var text = ExtractText(body)?.Trim();
            return string.IsNullOrEmpty(text) ? TranscriptionResult.Fail(TranscriptionError.NoSpeech) : TranscriptionResult.Ok(text);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return TranscriptionResult.Fail(MapTransportError(e));
        }
    }

    /// <summary>Validates a key cheaply by fetching the model; used by Settings → Test connection.</summary>
    public async Task<TranscriptionResult> TestConnectionAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiKey)) return TranscriptionResult.Fail(TranscriptionError.NoApiKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(modelsBase, Constants.BatchModel));
        request.Headers.Add("x-goog-api-key", apiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            var code = (int)response.StatusCode;
            return code is >= 200 and < 300 ? TranscriptionResult.Ok(Constants.BatchModel)
                : TranscriptionResult.Fail(MapHttpError(code, body));
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return TranscriptionResult.Fail(MapTransportError(e));
        }
    }

    public static HttpRequestMessage TranscribeRequest(string apiKey, byte[] wav, TranscriptionMode mode,
        IReadOnlyList<string> languageCodes, IReadOnlyList<string> vocabulary, Uri? endpoint = null)
    {
        var config = new JsonObject
        {
            ["language_codes"] = new JsonArray(languageCodes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["mode"] = mode == TranscriptionMode.Smart ? JsonValue.Create("smart") : new JsonObject { ["type"] = "verbatim" },
        };
        if (vocabulary.Count > 0)
            config["custom_vocabulary"] = new JsonArray(vocabulary.Take(1000).Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        var body = new JsonObject
        {
            ["generation_config"] = new JsonObject { ["transcription_config"] = config },
            ["input"] = new JsonArray(new JsonObject
            {
                ["data"] = Convert.ToBase64String(wav), ["mime_type"] = "audio/wav", ["type"] = "audio",
            }),
            ["model"] = Constants.BatchModel,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint ?? Constants.InteractionsUrl)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Headers.Add("Api-Revision", Constants.ApiRevision);
        return request;
    }

    /// <summary>The Interactions response carries the transcript as text content inside <c>steps</c>.
    /// Falls back to <c>output_text</c> and to the classic <c>candidates</c> shape.</summary>
    public static string? ExtractText(byte[] data)
    {
        var root = RootObject(data);
        if (root is null) return null;
        if (root["output_text"] is JsonValue ov && ov.TryGetValue<string>(out var output) && !string.IsNullOrWhiteSpace(output))
            return output;
        if (root["steps"] is JsonArray steps)
        {
            var texts = steps.OfType<JsonObject>()
                .SelectMany(s => s["content"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(c => Str(c["type"]) == "text")
                .Select(c => Str(c["text"]))
                .Where(t => t is not null);
            var joined = string.Join(" ", texts);
            if (!string.IsNullOrWhiteSpace(joined)) return joined;
        }
        if (root["candidates"] is JsonArray cands && cands.FirstOrDefault() is JsonObject first
            && first["content"] is JsonObject content && content["parts"] is JsonArray parts)
        {
            var joined = string.Concat(parts.OfType<JsonObject>().Select(p => Str(p["text"]) ?? ""));
            if (!string.IsNullOrWhiteSpace(joined)) return joined;
        }
        return null;
    }

    public static TranscriptionError MapHttpError(int code, byte[] body)
    {
        var error = RootObject(body)?["error"] as JsonObject;
        var message = Str(error?["message"]) ?? "";
        var status = Str(error?["status"]) ?? "";
        var reasons = (error?["details"] as JsonArray ?? []).OfType<JsonObject>().Select(d => Str(d["reason"]) ?? "");

        var keyInvalid = reasons.Any(r => r.Contains("API_KEY", StringComparison.OrdinalIgnoreCase))
            || message.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase);
        var quota = code == 429 || status == "RESOURCE_EXHAUSTED"
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

        if (keyInvalid) return TranscriptionError.InvalidApiKey;
        if (quota) return TranscriptionError.QuotaExceeded;
        if (code is 401 or 403 || status == "PERMISSION_DENIED") return TranscriptionError.InvalidApiKey;
        return TranscriptionError.Server(code, message);
    }

    public static TranscriptionError MapTransportError(Exception e) => e switch
    {
        TaskCanceledException or OperationCanceledException or TimeoutException => TranscriptionError.ConnectionLost,
        HttpRequestException { InnerException: SocketException se } when se.SocketErrorCode is
            SocketError.HostNotFound or SocketError.NoData or SocketError.NetworkUnreachable
            or SocketError.HostUnreachable or SocketError.ConnectionRefused or SocketError.TryAgain => TranscriptionError.NoNetwork,
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError } => TranscriptionError.NoNetwork,
        HttpRequestException { InnerException: IOException } => TranscriptionError.ConnectionLost,
        _ => TranscriptionError.Unknown(e.Message),
    };

    internal static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Bodies come back either as a bare object or wrapped in a single-element array.</summary>
    static JsonObject? RootObject(byte[] data)
    {
        try
        {
            var parsed = JsonNode.Parse(data);
            return parsed as JsonObject ?? (parsed as JsonArray)?.FirstOrDefault() as JsonObject;
        }
        catch { return null; }
    }
}
