using StacksAtlas.Core.Services.Licensing;

namespace StacksAtlas.Tests;

internal static class LicenseTestFixtures
{
    public static LicenseStatus HubFleetLicense(
        LicenseTier tier = LicenseTier.Enterprise,
        int nodeLimit = int.MaxValue) =>
        new(
            IsActive: true,
            Tier: tier,
            DeviceLimit: int.MaxValue,
            LicenseKey: "test-key",
            HardwareId: "test-hwid",
            NodeLimit: nodeLimit,
            HubLimit: tier == LicenseTier.Enterprise ? int.MaxValue : 1,
            AllowsHub: true,
            AllowsFederationJoin: true,
            AllowsNodeEnrollment: true);

    public static LicenseStatus ProFleetLicense() =>
        new(
            IsActive: true,
            Tier: LicenseTier.Pro,
            DeviceLimit: int.MaxValue,
            LicenseKey: "pro-key",
            HardwareId: "test-hwid",
            NodeLimit: 10,
            HubLimit: 1,
            AllowsHub: true,
            AllowsFederationJoin: true,
            AllowsNodeEnrollment: true);

    public static LicenseStatus FreeStandaloneLicense() =>
        new(
            IsActive: true,
            Tier: LicenseTier.Home,
            DeviceLimit: int.MaxValue,
            LicenseKey: "free-key",
            HardwareId: "test-hwid",
            MaxStandaloneActivations: LicenseEntitlements.UnlimitedStandaloneActivations,
            AllowsHub: false,
            AllowsFederationJoin: false,
            AllowsNodeEnrollment: false);

    internal sealed class StubLicenseService(LicenseStatus status) : ILicenseService
    {
        public event Action<LicenseStatus>? OnLicenseStatusChanged { add { } remove { } }

        public Task<LicenseStatus> GetCurrentStatusAsync() => Task.FromResult(status);

        public Task<LicenseStatus> ActivateAsync(string licenseKey) => Task.FromResult(status);

        public void ApplyInheritedLicense(LicenseTier tier, string payloadJson, string signatureBase64) { }

        public void ClearInheritedLicense() { }
    }

    internal sealed class StubNodeFleetTelemetryPurgeService : StacksAtlas.Core.Services.Federation.INodeFleetTelemetryPurgeService
    {
        public Task<StacksAtlas.Core.Services.Federation.NodeFleetTelemetryPurgeResult> PurgeAsync(
            string nodeId,
            string? legacyNodeName = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StacksAtlas.Core.Services.Federation.NodeFleetTelemetryPurgeResult());
    }

    internal sealed class StubAuditService : StacksAtlas.Core.Services.Audit.IAuditService
    {
        public void Record(
            string action,
            string resourceType,
            string? resourceId,
            string outcome,
            string? detail = null,
            System.Security.Claims.ClaimsPrincipal? actor = null,
            string? clientIp = null,
            string? actorUsernameOverride = null,
            string? actorUserIdOverride = null,
            string? actorRoleOverride = null)
        {
        }
    }
}
