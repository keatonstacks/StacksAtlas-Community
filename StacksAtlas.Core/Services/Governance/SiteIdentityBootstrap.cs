using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Governance;

/// <summary>
/// Keeps onboarding SiteName aligned with federation NodeId / NodeDisplayName
/// (Settings → Identity uses NodeId as the visible site name).
/// </summary>
public static partial class SiteIdentityBootstrap
{
    public const string DefaultUnnamedNodeId = "node-unnamed";

    /// <summary>
    /// Applies a friendly site name to federation identity when the node is still unnamed
    /// (or NodeDisplayName is empty). Does not overwrite Hub-assigned NodeId after enrollment.
    /// </summary>
    public static bool TryApplySiteName(
        FederationSettingsStore federationStore,
        string siteName,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(siteName))
            return false;

        var trimmed = siteName.Trim();
        var federation = federationStore.Current;
        var enrolled = !string.IsNullOrWhiteSpace(federation.HubUrl)
            || federation.UseTailscaleForHubConnection;

        var changed = false;

        if (string.IsNullOrWhiteSpace(federation.NodeDisplayName)
            || string.Equals(federation.NodeDisplayName, DefaultUnnamedNodeId, StringComparison.OrdinalIgnoreCase))
        {
            federation.NodeDisplayName = trimmed;
            changed = true;
        }

        // Identity UI edits NodeId for standalone/Hub; keep it in sync when still default.
        if (!enrolled
            && (string.IsNullOrWhiteSpace(federation.NodeId)
                || string.Equals(federation.NodeId, DefaultUnnamedNodeId, StringComparison.OrdinalIgnoreCase)))
        {
            var slug = SlugifyNodeId(trimmed);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                federation.NodeId = slug;
                changed = true;
            }
        }

        if (!changed)
            return false;

        federationStore.Save(federation);
        logger?.LogInformation(
            "Applied onboarding site identity. NodeId={NodeId} DisplayName={DisplayName}",
            federation.NodeId,
            federation.NodeDisplayName);
        return true;
    }

    public static string SlugifyNodeId(string siteName)
    {
        var slug = NodeIdSlugRegex().Replace(siteName.Trim().ToLowerInvariant(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
            return DefaultUnnamedNodeId;
        if (slug.Length > 64)
            slug = slug[..64].Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? DefaultUnnamedNodeId : slug;
    }

    [GeneratedRegex(@"[^a-z0-9\-_]+")]
    private static partial Regex NodeIdSlugRegex();
}
