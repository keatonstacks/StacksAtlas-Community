using System.Text.Json;
using System.Text.Json.Serialization;

namespace StacksAtlas.Core.Models;

public class NetworkScopeConverter : JsonConverter<NetworkScope>
{
    public override NetworkScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var cidr = reader.GetString() ?? string.Empty;
            return new NetworkScope { Cidr = cidr };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected string or object for NetworkScope");
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var scope = new NetworkScope();
        
        if (root.TryGetProperty("cidr", out var cidrProp) || root.TryGetProperty("Cidr", out cidrProp))
            scope.Cidr = cidrProp.GetString() ?? string.Empty;

        if (root.TryGetProperty("interfaceId", out var ifaceProp) || root.TryGetProperty("InterfaceId", out ifaceProp))
            scope.InterfaceId = ifaceProp.GetString();

        if (root.TryGetProperty("enableArp", out var arpProp) || root.TryGetProperty("EnableArp", out arpProp))
            scope.EnableArp = arpProp.GetBoolean();

        if (root.TryGetProperty("enablePing", out var pingProp) || root.TryGetProperty("EnablePing", out pingProp))
            scope.EnablePing = pingProp.GetBoolean();

        if (root.TryGetProperty("enableMdns", out var mdnsProp) || root.TryGetProperty("EnableMdns", out mdnsProp))
            scope.EnableMdns = mdnsProp.GetBoolean();

        if (root.TryGetProperty("enableUpnp", out var upnpProp) || root.TryGetProperty("EnableUpnp", out upnpProp))
            scope.EnableUpnp = upnpProp.GetBoolean();

        if (root.TryGetProperty("id", out var idProp) || root.TryGetProperty("Id", out idProp))
            scope.Id = idProp.GetString() ?? string.Empty;

        if (root.TryGetProperty("vlanTag", out var vlanProp) || root.TryGetProperty("VlanTag", out vlanProp))
            scope.VlanTag = vlanProp.GetString();

        return scope;
    }

    public override void Write(Utf8JsonWriter writer, NetworkScope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(value.Id))
            writer.WriteString("id", value.Id);
        writer.WriteString("cidr", value.Cidr);
        writer.WriteString("interfaceId", value.InterfaceId);
        writer.WriteBoolean("enableArp", value.EnableArp);
        writer.WriteBoolean("enablePing", value.EnablePing);
        writer.WriteBoolean("enableMdns", value.EnableMdns);
        writer.WriteBoolean("enableUpnp", value.EnableUpnp);
        if (!string.IsNullOrWhiteSpace(value.VlanTag))
            writer.WriteString("vlanTag", value.VlanTag);
        writer.WriteEndObject();
    }
}
