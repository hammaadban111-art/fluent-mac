using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fluent.Core;

public sealed record Transcript(Guid Id, string Text, DateTimeOffset CreatedAt, double DurationSeconds)
{
    public static Transcript New(string text, double durationSeconds) => new(Guid.NewGuid(), text, DateTimeOffset.Now, durationSeconds);

    [JsonIgnore]
    public int WordCount => Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>Local transcript history: text only, one JSON file, newest first. Written only when history
/// is switched on.</summary>
public sealed class HistoryStore
{
    public const int Limit = 500;
    readonly string path;
    readonly object gate = new();
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public HistoryStore(string directory)
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "history.json");
    }

    public List<Transcript> All() { lock (gate) return Load(); }

    public void Add(Transcript t)
    {
        lock (gate)
        {
            var items = Load();
            items.Insert(0, t);
            Save(items.Take(Limit).ToList());
        }
    }

    public void Delete(Guid id) { lock (gate) Save(Load().Where(t => t.Id != id).ToList()); }

    public void Clear() { lock (gate) Save([]); }

    List<Transcript> Load()
    {
        try { return JsonSerializer.Deserialize<List<Transcript>>(File.ReadAllText(path), Json) ?? []; }
        catch { return []; }
    }

    void Save(List<Transcript> items)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(items, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Wraps raw 16-bit little-endian mono PCM in a RIFF/WAVE header.</summary>
public static class Wav
{
    public static byte[] Encode(ReadOnlySpan<short> samples, int sampleRate = Constants.SampleRate)
    {
        var dataBytes = samples.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
        w.Write((short)1);                 // PCM
        w.Write((short)1);                 // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);           // byte rate
        w.Write((short)2);                 // block align
        w.Write((short)16);                // bits per sample
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Reads the samples of a 16-bit mono WAV (the test fixtures and the fake microphone).</summary>
    public static short[] DecodePcm16(byte[] wav)
    {
        var i = 12;
        while (i + 8 <= wav.Length)
        {
            var id = Encoding.ASCII.GetString(wav, i, 4);
            var size = BitConverter.ToInt32(wav, i + 4);
            if (id == "data")
            {
                var n = Math.Min(size, wav.Length - i - 8) / 2;
                var o = new short[n];
                Buffer.BlockCopy(wav, i + 8, o, 0, n * 2);
                return o;
            }
            i += 8 + size + (size & 1);
        }
        return [];
    }

    public static double Duration(int sampleCount, int sampleRate = Constants.SampleRate) => (double)sampleCount / sampleRate;
}
