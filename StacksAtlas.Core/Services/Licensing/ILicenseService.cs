using System;
using System.Threading.Tasks;

namespace StacksAtlas.Core.Services.Licensing;

public interface ILicenseService
{
    event Action<LicenseStatus>? OnLicenseStatusChanged;
    Task<LicenseStatus> GetCurrentStatusAsync();
    Task<LicenseStatus> ActivateAsync(string licenseKey);
    void ApplyInheritedLicense(LicenseTier tier, string payloadJson, string signatureBase64);
    void ClearInheritedLicense();
}
