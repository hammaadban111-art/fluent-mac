namespace Fluent.Core;

/// <summary>Picks the transcript after stop: live first (it is normally final well under a second after
/// stop); if live has not answered within <see cref="Constants.LiveGrace"/>, the batch request with the
/// whole recording starts as well and the first usable answer wins.</summary>
public static class TranscriptRace
{
    public static async Task<(TranscriptionResult Result, string Route)> RunAsync(
        Task<string?>? live, Func<Task<TranscriptionResult>> batch, TimeSpan grace)
    {
        if (live is null) return (await Batch(batch), "batch");
        if (await Task.WhenAny(live, Task.Delay(grace)) == live)
            return Text(live) is { } text ? (TranscriptionResult.Ok(text), "live") : (await Batch(batch), "batch");

        var batchTask = Batch(batch);
        if (await Task.WhenAny(live, batchTask) == live && Text(live) is { } early)
            return (TranscriptionResult.Ok(early), "live");
        var b = await batchTask;
        if (b.IsSuccess) return (b, "batch (live slow)");
        // Batch failed first: the live answer may still come.
        try { await live; } catch { }
        return Text(live) is { } late ? (TranscriptionResult.Ok(late), "live") : (b, "batch");
    }

    /// <summary>A live task that failed counts as no answer.</summary>
    static string? Text(Task<string?> live) => live.IsCompletedSuccessfully ? live.Result : null;

    static async Task<TranscriptionResult> Batch(Func<Task<TranscriptionResult>> batch)
    {
        try { return await batch(); }
        catch { return TranscriptionResult.Fail(TranscriptionError.ConnectionLost); }
    }
}
