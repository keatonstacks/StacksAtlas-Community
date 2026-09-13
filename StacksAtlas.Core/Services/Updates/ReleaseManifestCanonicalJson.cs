using System.Text;
using System.Text.Json;
using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

/// <summary>
/// Deterministic UTF-8 JSON for Ed25519 signing  -  keys sorted lexicographically at every object level.
/// Signers and verifiers MUST use these bytes; pretty-printed JSON is not verified.
/// </summary>
public static class ReleaseManifestCanonicalJson
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false
    };

    public static byte[] ToCanonicalUtf8(ReleaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var json = JsonSerializer.Serialize(manifest, DeserializeOptions);
        return ToCanonicalUtf8(Encoding.UTF8.GetBytes(json));
    }

    public static byte[] ToCanonicalUtf8(ReadOnlySpan<byte> manifestJsonUtf8)
    {
        if (manifestJsonUtf8.IsEmpty)
            throw new ArgumentException("Manifest JSON is empty.", nameof(manifestJsonUtf8));

        using var doc = JsonDocument.Parse(manifestJsonUtf8.ToArray());
        return Encoding.UTF8.GetBytes(CanonicalizeElement(doc.RootElement));
    }

    public static ReleaseManifest DeserializeVerified(ReadOnlySpan<byte> manifestJsonUtf8)
    {
        return JsonSerializer.Deserialize<ReleaseManifest>(manifestJsonUtf8.ToArray(), DeserializeOptions)
               ?? throw new InvalidOperationException("Manifest JSON deserialized to null.");
    }

    private static string CanonicalizeElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeObject(element),
            JsonValueKind.Array => CanonicalizeArray(element),
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}")
        };

    private static string CanonicalizeObject(JsonElement element)
    {
        var props = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        if (props.Count == 0)
            return "{}";

        var sb = new StringBuilder();
        sb.Append('{');
        for (var i = 0; i < props.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonSerializer.Serialize(props[i].Name));
            sb.Append(':');
            sb.Append(CanonicalizeElement(props[i].Value));
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string CanonicalizeArray(JsonElement element)
    {
        var items = element.EnumerateArray().Select(CanonicalizeElement).ToList();
        if (items.Count == 0)
            return "[]";

        return "[" + string.Join(",", items) + "]";
    }
}
