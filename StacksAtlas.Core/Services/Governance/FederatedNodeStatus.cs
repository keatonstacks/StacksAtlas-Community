using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Governance;

/// <summary>
/// Canonical federated node status values and helpers for Hub fleet presentation.
/// </summary>
public static class FederatedNodeStatus
{
    public const string Online = "online";
    public const string Offline = "offline";
    public const string Resetting = "resetting";
    public const string SetupRequired = "setup_required";

    public static bool RequiresOperatorSetup(OnboardingSettings onboarding, bool hasUsers) =>
        !OnboardingSettingsReset.GetEffectiveFoundationComplete(onboarding, hasUsers);

    public static string ResolveFromRegistration(bool requiresOperatorSetup) =>
        requiresOperatorSetup ? SetupRequired : Online;

    /// <summary>
    /// Applies node-reported operational readiness to a stored fleet status row.
    /// </summary>
    public static string? ResolveAfterOperationalPulse(string? storedStatus, bool requiresOperatorSetup)
    {
        if (string.Equals(storedStatus, Resetting, StringComparison.OrdinalIgnoreCase))
            return null;

        if (requiresOperatorSetup)
        {
            if (!string.Equals(storedStatus, SetupRequired, StringComparison.OrdinalIgnoreCase))
                return SetupRequired;
            return null;
        }

        if (string.Equals(storedStatus, SetupRequired, StringComparison.OrdinalIgnoreCase))
            return Online;

        return null;
    }

    /// <summary>
    /// Heartbeats refresh liveness only  -  do not promote setup/reset states to online.
    /// </summary>
    public static bool ShouldPreserveOnHeartbeat(string? status) =>
        string.Equals(status, SetupRequired, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Resetting, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hub presentation status: SignalR registry wins; stale ConnectionId rows age out after grace.
    /// </summary>
    public static string ResolveEffectiveStatus(Models.FederatedNode node, bool signalRConnected, DateTime utcNow)
    {
        var stored = string.IsNullOrWhiteSpace(node.Status) ? Offline : node.Status;
        if (ShouldPreserveOnHeartbeat(stored))
            return stored;

        if (signalRConnected)
            return Online;

        if (string.IsNullOrEmpty(node.ConnectionId))
            return Offline;

        if (utcNow - node.LastSeenUtc > TimeSpan.FromMinutes(3))
            return Offline;

        return stored;
    }
}
