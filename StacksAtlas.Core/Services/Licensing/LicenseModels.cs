namespace StacksAtlas.Core.Services.Licensing;

public enum LicenseTier
{
    Home = 0,
    Pro = 1,
    Enterprise = 2,
    Business = 3
}

public record LicenseStatus(
    bool IsActive,
    LicenseTier Tier,
    int DeviceLimit,
    string? LicenseKey,
    string HardwareId,
    string? Message = null,
    int CurrentCount = 0,
    bool IsLimited = false,
    string? InstanceId = null,
    int NodeLimit = 0,
    int HubLimit = 0,
    bool SSOEnabled = false,
    bool WebhooksEnabled = false,
    bool APIEnabled = false,
    bool AllowsHub = false,
    bool AllowsFederationJoin = false,
    bool AllowsNodeEnrollment = false,
    int MaxStandaloneActivations = 0,
    BillingModel BillingModel = BillingModel.Lifetime,
    bool AllowsExternalHubDatabase = false
);
