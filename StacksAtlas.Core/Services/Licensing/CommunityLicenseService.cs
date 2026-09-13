using System;
using System.Threading.Tasks;

namespace StacksAtlas.Core.Services.Licensing;

/// <summary>
/// Default open-source community license service.
/// Provides unrestricted standalone discovery, scanning, device classification, and local integrations.
/// </summary>
public sealed class CommunityLicenseService : ILicenseService
{
    public event Action<LicenseStatus>? OnLicenseStatusChanged;

    private static readonly LicenseStatus CommunityStatus = new(
        IsActive: true,
        Tier: LicenseTier.Home,
        DeviceLimit: int.MaxValue,
        LicenseKey: "COMMUNITY-OPEN-SOURCE",
        HardwareId: "community-instance",
        Message: "StacksAtlas Community Edition (MIT Open Source)",
        CurrentCount: 0,
        IsLimited: false,
        WebhooksEnabled: true,
        APIEnabled: true,
        AllowsHub: false,
        AllowsFederationJoin: false,
        AllowsNodeEnrollment: false
    );

    public Task<LicenseStatus> GetCurrentStatusAsync()
    {
        return Task.FromResult(CommunityStatus);
    }

    public Task<LicenseStatus> ActivateAsync(string licenseKey)
    {
        return Task.FromResult(CommunityStatus);
    }

    public void ApplyInheritedLicense(LicenseTier tier, string payloadJson, string signatureBase64)
    {
    }

    public void ClearInheritedLicense()
    {
    }
}
