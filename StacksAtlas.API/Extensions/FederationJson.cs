using System.Text.Json;
using System.Text.Json.Serialization;
using StacksAtlas.Core.Json;

namespace StacksAtlas.API.Extensions;

public static class FederationJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new LiteDbObjectIdJsonConverter());
    }
}
