using System.Security.Cryptography.X509Certificates;

namespace StacksAtlas.Core.Services.Security;

public interface IHubCertificateAuthority
{
    X509Certificate2 GetOrCreateRootCa();
    X509Certificate2 GetOrCreateServerCertificate();
    void EnsureServerCertificateIncludesDnsNames(IEnumerable<string>? dnsNames);
    X509Certificate2 SignNodeClientCertificate(byte[] csrBytes, string nodeId, TimeSpan validity);
    void RevokeCertificate(string serialNumber);
    bool IsRevoked(string serialNumber);
    string SignData(byte[] data);
}
