using System.Globalization;
using System.Text.Json;

namespace Fluent.Core;

/// <summary>One platform's entry in the website's updates.json, published with every release.</summary>
public sealed record ReleaseInfo(string Version, string Url, string Sha256, long Size, string Notes);

/// <summary>Reads updates.json and compares versions. Pure, so it is unit-tested.</summary>
public static class Updates
{
    /// <summary>The entry for <paramref name="platform"/> ("windows", "mac", "android"), or null.</summary>
    public static ReleaseInfo? Parse(string json, string platform = "windows")
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(platform, out var p) || p.ValueKind != JsonValueKind.Object) return null;
            string? S(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var version = S("version");
            var url = S("url");
            var sha = S("sha256");
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sha)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return null;
            var size = p.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) ? n : 0;
            return new ReleaseInfo(version.Trim(), url, sha.Trim().ToLowerInvariant(), size, S("notes") ?? "");
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Whether <paramref name="latest"/> is a later version than <paramref name="current"/>: dotted
    /// numbers compared part by part, missing parts as 0 ("1.2" equals "1.2.0").</summary>
    public static bool IsNewer(string latest, string current)
    {
        var a = Parts(latest);
        var b = Parts(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    static int[] Parts(string v) => v.Trim().TrimStart('v', 'V').Split('.', '-', '+')
        .TakeWhile(p => p.Length > 0 && p.All(char.IsDigit))
        .Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
}
