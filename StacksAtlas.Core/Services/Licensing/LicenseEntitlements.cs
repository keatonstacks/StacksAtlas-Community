namespace StacksAtlas.Core.Services.Licensing;

/// <summary>
/// v2 licensing entitlements per tier (see licensing.md).
/// </summary>
public static class LicenseEntitlements
{
    /// <summary>Free tier: no practical cap on standalone HWID activations (enforce at Lemon Squeezy if desired).</summary>
    public const int UnlimitedStandaloneActivations = int.MaxValue;

    /// <summary>Hub fleet size is not capped on the Hub license  -  each enrolled appliance has its own key.</summary>
    public const int UnlimitedFleetEnrollment = int.MaxValue;

    public static TierEntitlements ForTier(LicenseTier tier) => tier switch
    {
        LicenseTier.Pro => new TierEntitlements(
            NodeLimit: UnlimitedFleetEnrollment,
            HubLimit: 1,
            MaxStandaloneActivations: 0,
            AllowsHub: true,
            AllowsFederationJoin: true,
            AllowsNodeEnrollment: true,
            AllowsExternalHubDatabase: false,
            BillingModel: BillingModel.Lifetime,
            SsoEnabled: true,
            WebhooksEnabled: true,
            ApiEnabled: true),

        LicenseTier.Business => new TierEntitlements(
            NodeLimit: UnlimitedFleetEnrollment,
            HubLimit: 1,
            MaxStandaloneActivations: 0,
            AllowsHub: true,
            AllowsFederationJoin: true,
            AllowsNodeEnrollment: true,
            AllowsExternalHubDatabase: false,
            BillingModel: BillingModel.Lifetime,
            SsoEnabled: true,
            WebhooksEnabled: true,
            ApiEnabled: true),

        LicenseTier.Enterprise => new TierEntitlements(
            NodeLimit: int.MaxValue,
            HubLimit: int.MaxValue,
            MaxStandaloneActivations: 0,
            AllowsHub: true,
            AllowsFederationJoin: true,
            AllowsNodeEnrollment: true,
            AllowsExternalHubDatabase: true,
            BillingModel: BillingModel.Subscription,
            SsoEnabled: true,
            WebhooksEnabled: true,
            ApiEnabled: true),

        _ => new TierEntitlements(
            NodeLimit: 0,
            HubLimit: 0,
            MaxStandaloneActivations: UnlimitedStandaloneActivations,
            AllowsHub: false,
            AllowsFederationJoin: false,
            AllowsNodeEnrollment: false,
            AllowsExternalHubDatabase: false,
            BillingModel: BillingModel.Lifetime,
            SsoEnabled: false,
            WebhooksEnabled: true,
            ApiEnabled: true)
    };
}

public readonly record struct TierEntitlements(
    int NodeLimit,
    int HubLimit,
    int MaxStandaloneActivations,
    bool AllowsHub,
    bool AllowsFederationJoin,
    bool AllowsNodeEnrollment,
    bool AllowsExternalHubDatabase,
    BillingModel BillingModel,
    bool SsoEnabled,
    bool WebhooksEnabled,
    bool ApiEnabled);

public enum BillingModel
{
    Lifetime = 0,
    Subscription = 1
}
