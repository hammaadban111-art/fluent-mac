using Fluent.Core;
using Xunit;

namespace Fluent.Core.Tests;

public class UpdatesTests
{
    const string Manifest = """
        {"android":{"version":"1.9.6","code":18,"url":"https://x/fluent.apk","sha256":"AA"},
         "windows":{"version":"1.3","url":"https://fluent-voice-v2.vercel.app/download/Fluent-Setup-1.3.exe","sha256":"ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789","size":123,"notes":"Faster"}}
        """;

    [Fact]
    public void ReadsTheWindowsEntry()
    {
        var r = Updates.Parse(Manifest)!;
        Assert.Equal("1.3", r.Version);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", r.Sha256);
        Assert.Equal(123, r.Size);
        Assert.Equal("Faster", r.Notes);
        Assert.Null(Updates.Parse(Manifest, "mac"));
        Assert.Null(Updates.Parse("not json"));
        Assert.Null(Updates.Parse("[]"));
        Assert.Null(Updates.Parse("\"unavailable\""));
        Assert.Null(Updates.Parse("""{"windows":{"version":"1.3","url":"https://x/x.exe","sha256":"abcdef"}}"""));
        Assert.Null(Updates.Parse("""{"windows":{"version":"1.3","url":"http://insecure/x.exe","sha256":"aa"}}"""));
        Assert.Null(Updates.Parse("""{"windows":{"version":"1.3","url":"https://x/x.exe"}}"""));
    }

    [Theory]
    [InlineData("1.3", "1.2", true)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("1.2.1", "1.2", true)]
    [InlineData("1.10", "1.9", true)]
    [InlineData("1.2", "1.3", false)]
    [InlineData("v2.0", "1.9.9", true)]
    [InlineData("99999999999.1", "2.2", true)]
    [InlineData("2.2", "1.3", true)]
    public void ComparesVersionsByNumber(string latest, string current, bool newer) =>
        Assert.Equal(newer, Updates.IsNewer(latest, current));
}
