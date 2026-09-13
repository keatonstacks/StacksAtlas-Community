using System.Text.Json;
using System.Text.Json.Nodes;

namespace StacksAtlas.Core.Helpers;

/// <summary>
/// Helpers for federated scan-settings JSON. Node telemetry is read-only metadata
/// reported by Nodes; network/polling remain Hub-authoritative once configured on the Hub.
/// </summary>
public static class ScanSettingsJsonHelper
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Returns true when the Hub has no configured network scopes for this node.
    /// </summary>
    public static bool IsHubPolicyEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!TryGetProperty(root, "network", out var network))
                return true;
            if (!TryGetProperty(network, "subnets", out var subnets))
                return true;

            return subnets.ValueKind != JsonValueKind.Array || subnets.GetArrayLength() == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Reconciles Hub-stored scan settings with a live Node report.
    /// Adopts the full Node payload when Hub policy is empty; otherwise keeps Hub policy and refreshes telemetry.
    /// </summary>
    public static string ReconcileScanSettings(string? existingJson, string? incomingJson)
    {
        if (string.IsNullOrWhiteSpace(incomingJson))
            return existingJson ?? "{}";

        if (string.IsNullOrWhiteSpace(existingJson) || IsHubPolicyEmpty(existingJson))
            return incomingJson;

        return MergeNodeTelemetry(existingJson, incomingJson);
    }

    /// <summary>
    /// Copies read-only telemetry (interfaces, scanEngine) from incoming into base JSON.
    /// </summary>
    public static string MergeNodeTelemetry(string? baseJson, string? incomingJson)
    {
        if (string.IsNullOrWhiteSpace(baseJson))
            return incomingJson ?? "{}";
        if (string.IsNullOrWhiteSpace(incomingJson))
            return baseJson;

        try
        {
            var baseNode = JsonNode.Parse(baseJson)?.AsObject() ?? new JsonObject();
            var incomingNode = JsonNode.Parse(incomingJson)?.AsObject();
            if (incomingNode == null)
                return baseJson;

            foreach (var key in new[] { "interfaces", "Interfaces", "scanEngine", "ScanEngine", "logging", "Logging" })
            {
                if (incomingNode.TryGetPropertyValue(key, out var value) && value != null)
                {
                    var normalizedKey = char.ToLowerInvariant(key[0]) + key[1..];
                    baseNode[normalizedKey] = value.DeepClone();
                }
            }

            return baseNode.ToJsonString(CamelCase);
        }
        catch
        {
            return baseJson;
        }
    }

    /// <summary>
    /// When Hub patches scan settings, preserve per-node telemetry if the patch omits it.
    /// </summary>
    public static string PreserveNodeTelemetry(string? existingJson, string patchJson)
    {
        if (string.IsNullOrWhiteSpace(existingJson))
            return patchJson;

        try
        {
            var patchNode = JsonNode.Parse(patchJson)?.AsObject();
            if (patchNode == null)
                return patchJson;

            var existingNode = JsonNode.Parse(existingJson)?.AsObject();
            if (existingNode == null)
                return patchJson;

            foreach (var key in new[] { "interfaces", "scanEngine", "logging" })
            {
                if (patchNode.ContainsKey(key))
                    continue;

                if (existingNode.TryGetPropertyValue(key, out var value) && value != null)
                    patchNode[key] = value.DeepClone();
            }

            return patchNode.ToJsonString(CamelCase);
        }
        catch
        {
            return patchJson;
        }
    }

    /// <summary>Reads Node-reported debug logging flag from scan-settings telemetry JSON.</summary>
    public static bool TryReadDebugLoggingEnabled(string? json, out bool enabled)
    {
        enabled = false;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!TryGetProperty(root, "logging", out var logging))
                return false;
            if (!TryGetProperty(logging, "isDebugLoggingEnabled", out var flag))
                return false;

            enabled = flag.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(flag.GetString(), out var parsed) && parsed,
                _ => false
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.TryGetProperty(name, out value))
            return true;

        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        return parent.TryGetProperty(pascal, out value);
    }
}
