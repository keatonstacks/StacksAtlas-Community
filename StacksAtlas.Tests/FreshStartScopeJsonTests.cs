using System.Text.Json;
using StacksAtlas.Core.Services.Governance;

namespace StacksAtlas.Tests;

public class FreshStartScopeJsonTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Theory]
    [InlineData("{\"scope\":\"ApplianceOnly\"}", FreshStartScope.ApplianceOnly)]
    [InlineData("{\"scope\":\"FactoryReset\"}", FreshStartScope.FactoryReset)]
    [InlineData("{\"Scope\":\"ApplianceOnly\"}", FreshStartScope.ApplianceOnly)]
    public void Deserializes_StringScope_FromUiPayload(string json, FreshStartScope expected)
    {
        var request = JsonSerializer.Deserialize<FreshStartRequestDto>(json, Options);
        Assert.NotNull(request);
        Assert.Equal(expected, request!.Scope);
    }

    private sealed class FreshStartRequestDto
    {
        public FreshStartScope? Scope { get; set; }
    }
}
