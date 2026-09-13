using System.Text.Json.Serialization;

namespace StacksAtlas.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkRole
{
    Default,
    HubCommunication,
    AlertsAndSiem,
    DiscoveryFallback
}
