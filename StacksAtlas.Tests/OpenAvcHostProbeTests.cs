using StacksAtlas.Core.Services.Integrations;

namespace StacksAtlas.Tests;

public class OpenAvcHostProbeTests
{
    [Fact]
    public void TryParseHealthJson_accepts_openavc_health_shape()
    {
        const string body = """{"status":"ok","version":"0.6.0","uptime":120000,"hostname":"room-pc"}""";

        var ok = OpenAvcHostProbe.TryParseHealthJson(body, out var version);

        Assert.True(ok);
        Assert.Equal("0.6.0", version);
    }

    [Fact]
    public void TryParseHealthJson_rejects_generic_json_without_openavc_fields()
    {
        const string body = """{"version":"1.0.0","service":"other-app"}""";

        Assert.False(OpenAvcHostProbe.TryParseHealthJson(body, out _));
    }

    [Fact]
    public void LooksLikeOpenAvcTitle_matches_scan_http_title()
    {
        Assert.True(OpenAvcHostProbe.LooksLikeOpenAvcTitle("OpenAVC 0.6.0"));
        Assert.False(OpenAvcHostProbe.LooksLikeOpenAvcTitle("Synology DiskStation"));
    }
}
