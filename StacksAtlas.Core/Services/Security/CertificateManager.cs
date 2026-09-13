using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Security;

public class CertificateManager(ILogger<CertificateManager> logger)
{
    private readonly ILogger<CertificateManager> _logger = logger;
    private const string CertFolder = "certs";
    internal const string CaFileName = "StacksAtlas-LocalCA.pfx";
    internal const string LeafFileName = "StacksAtlas.pfx";
    internal const string PfxPassword = "StacksAtlas_Internal_Secure_999";

    public X509Certificate2 GetOrCreateCertificate()
    {
        var certPath = StacksAtlas.Core.Helpers.PlatformPaths.GetCertDirectory();
        var leafPath = Path.Combine(certPath, LeafFileName);
        var caPath = Path.Combine(certPath, CaFileName);

        Directory.CreateDirectory(certPath);

        if (File.Exists(leafPath) && File.Exists(caPath))
        {
            try
            {
                _logger.LogInformation("Loading existing SSL certificate from {Path}", leafPath);
                var existing = LoadPkcs12FromDisk(leafPath);
                if (existing.HasPrivateKey)
                    return existing;

                _logger.LogError("Certificate at {Path} is missing its private key. Regenerating...", leafPath);
                existing.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load existing certificate. Regenerating...");
            }

            TryDeleteCertificateFile(leafPath);
            TryDeleteCertificateFile(caPath);
        }
        else if (File.Exists(leafPath))
        {
            _logger.LogInformation("Legacy single-file TLS cert detected  -  regenerating CA-backed pair.");
            TryDeleteCertificateFile(leafPath);
        }

        return GenerateCaSignedLeafCertificate(leafPath, caPath);
    }

    private X509Certificate2 GenerateCaSignedLeafCertificate(string leafPath, string caPath)
    {
        _logger.LogInformation("Generating CA-backed TLS certificate for StacksAtlas...");

        var hostName = Dns.GetHostName();
        var notBefore = DateTimeOffset.Now.AddDays(-1);
        var caNotAfter = DateTimeOffset.Now.AddYears(10);
        var leafNotAfter = DateTimeOffset.Now.AddYears(1);

        using var caRsa = RSA.Create(4096);
        var caSubject = new X500DistinguishedName("CN=StacksAtlas Local CA");
        var caRequest = new CertificateRequest(caSubject, caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        using var caCert = caRequest.CreateSelfSigned(notBefore, caNotAfter);

        using var leafRsa = RSA.Create(2048);
        var leafSubject = new X500DistinguishedName($"CN=StacksAtlas-{hostName}");
        var leafRequest = new CertificateRequest(leafSubject, leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName("127.0.0.1");
        if (!string.IsNullOrWhiteSpace(hostName))
            sanBuilder.AddDnsName(hostName);

        foreach (var ip in GetLocalIPv4Addresses(hostName))
            sanBuilder.AddIpAddress(ip);

        leafRequest.CertificateExtensions.Add(sanBuilder.Build());
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;

        using var leafPublic = leafRequest.Create(caCert, notBefore, leafNotAfter, serial);
        using var leafCert = leafPublic.CopyWithPrivateKey(leafRsa);

        if (OperatingSystem.IsWindows())
            leafCert.FriendlyName = "StacksAtlas Appliance Certificate";

        File.WriteAllBytes(caPath, caCert.Export(X509ContentType.Pfx, PfxPassword));
        File.WriteAllBytes(leafPath, ExportLeafPfxWithChain(leafCert, caCert));

        _logger.LogInformation(
            "CA-backed TLS certificate generated. Trust StacksAtlas Local CA (not the leaf) for browser padlock.");

        return LoadPkcs12FromDisk(leafPath);
    }

    private static byte[] ExportLeafPfxWithChain(X509Certificate2 leaf, X509Certificate2 ca)
    {
        var caPublic = X509CertificateLoader.LoadCertificate(ca.Export(X509ContentType.Cert));
        var chain = new X509Certificate2Collection { leaf, caPublic };
        return chain.Export(X509ContentType.Pfx, PfxPassword)
            ?? throw new CryptographicException("Failed to export leaf PFX with CA chain.");
    }

    private X509Certificate2 LoadPkcs12FromDisk(string path) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, PfxPassword, Pkcs12LoadOptions.KeyStorageFlags);

    private static void TryDeleteCertificateFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }

    private IReadOnlyList<IPAddress> GetLocalIPv4Addresses(string hostName)
    {
        var ips = new List<IPAddress> { IPAddress.Loopback };
        var seen = new HashSet<string>(StringComparer.Ordinal) { IPAddress.Loopback.ToString() };

        try
        {
            foreach (var ip in Dns.GetHostAddresses(hostName))
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && seen.Add(ip.ToString()))
                    ips.Add(ip);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve {HostName} for certificate SANs; using network interfaces.", hostName);
        }

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = addr.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                if (seen.Add(ip.ToString()))
                    ips.Add(ip);
            }
        }

        return ips;
    }
}
