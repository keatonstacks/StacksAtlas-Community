using System.Net;

namespace StacksAtlas.Core.Services.Security;

/// <summary>
/// Rules for HTTP-first browser routing (1.7.5 TLS UX). Federation and remote API traffic must never be redirected.
/// </summary>
public static class ApplianceBrowserRoutingPolicy
{
    /// <summary>API prefixes that must never be scheme-redirected (federation, probes, integrations).</summary>
    public static readonly string[] ApiPassthroughPrefixes =
    [
        "/api/federation",
        "/api/health",
        "/api/webhooks",
    ];

    public static bool ShouldBypassSchemeRouting(string? path, IPAddress? remoteAddress)
    {
        if (string.IsNullOrEmpty(path))
            return true;

        // All REST and SignalR negotiate traffic stays on the requested scheme/port.
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api", StringComparison.OrdinalIgnoreCase))
            return true;

        // Browser onboarding redirects are loopback-only  -  LAN nodes and Tailscale clients are unaffected.
        return !IsLoopback(remoteAddress);
    }

    public static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
            return true;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            return IPAddress.IsLoopback(address.MapToIPv4());

        return false;
    }

    /// <summary>Endpoints that must stay available while the database is still initializing.</summary>
    public static bool IsBootstrapApiPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var lower = path.ToLowerInvariant();
        if (lower.StartsWith("/api/federation"))
            return true;

        foreach (var prefix in ApiPassthroughPrefixes)
        {
            if (lower.StartsWith(prefix))
                return true;
        }

        return lower.StartsWith("/api/health")
            || lower.StartsWith("/api/system/info")
            || lower.StartsWith("/api/system/certificate")
            || lower.StartsWith("/api/system/dashboard")
            || lower.StartsWith("/api/onboarding")
            || lower.StartsWith("/api/auth")
            || lower.StartsWith("/api/license/hardware-id");
    }
}
