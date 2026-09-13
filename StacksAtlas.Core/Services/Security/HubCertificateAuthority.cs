using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Data.Hub;

namespace StacksAtlas.Core.Services.Security;

public class HubCertificateAuthority : IHubCertificateAuthority
{
    private readonly ILogger<HubCertificateAuthority> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _caDirectory;
    private readonly string _caPfxPath;
    private readonly string _caCrtPath;
    
    private const string CaPassword = "StacksAtlas_CA_Secret_Password_Internal_999";
    private static X509Certificate2? _staticRootCa;
    private static X509Certificate2? _staticServerCert;
    private static readonly object _staticLock = new();
    private static readonly ConcurrentDictionary<string, byte> _staticRevokedSerials = new();

    public HubCertificateAuthority(
        ILogger<HubCertificateAuthority> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _caDirectory = StacksAtlas.Core.Helpers.PlatformPaths.GetCertDirectory();
        _caPfxPath = Path.Combine(_caDirectory, "HubRootCA.pfx");
        _caCrtPath = Path.Combine(_caDirectory, "HubRootCA.crt");
        
        InitializeCrlCache();
    }

    private void InitializeCrlCache()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetService<HubDbContext>();
            if (context == null)
            {
                return;
            }
            var revokedNodes = context.Nodes.AsNoTracking().Where(n => n.IsRevoked && n.CertificateSerialNumber != null).ToList();
            foreach (var node in revokedNodes)
            {
                if (node.CertificateSerialNumber != null)
                {
                    _staticRevokedSerials[node.CertificateSerialNumber] = 0;
                }
            }
            _logger.LogInformation("Initialized CA Revocation Cache with {Count} certificates.", _staticRevokedSerials.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CA CRL cache from database.");
        }
    }

    public static X509Certificate2 GetOrCreateRootCaStatic(Action<string, Exception?>? log = null)
    {
        lock (_staticLock)
        {
            if (_staticRootCa != null) return _staticRootCa;

            var caDirectory = StacksAtlas.Core.Helpers.PlatformPaths.GetCertDirectory();
            var caPfxPath = Path.Combine(caDirectory, "HubRootCA.pfx");
            var caCrtPath = Path.Combine(caDirectory, "HubRootCA.crt");

            Directory.CreateDirectory(caDirectory);

            if (File.Exists(caPfxPath))
            {
                try
                {
                    log?.Invoke($"Loading existing Root CA certificate from {caPfxPath}", null);
                    var cert = X509CertificateLoader.LoadPkcs12FromFile(caPfxPath, CaPassword, Pkcs12LoadOptions.KeyStorageFlags);
                    _staticRootCa = cert;
                    return cert;
                }
                catch (Exception ex)
                {
                    log?.Invoke("Failed to load Root CA certificate. Regenerating...", ex);
                }
            }

            _staticRootCa = GenerateNewRootCaStatic(caDirectory, caPfxPath, caCrtPath, log);
            return _staticRootCa;
        }
    }

    private static X509Certificate2 GenerateNewRootCaStatic(string caDirectory, string caPfxPath, string caCrtPath, Action<string, Exception?>? log)
    {
        log?.Invoke("Generating new self-signed Root CA Certificate...", null);
        
        var subjectName = "CN=StacksAtlas Hub Root Certificate Authority, O=StacksAtlas, OU=Security";
        
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest(subjectName, ecdsa, HashAlgorithmName.SHA384);

        // Add extensions appropriate for CA
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddYears(10); // CA valid for 10 years

        var cert = request.CreateSelfSigned(notBefore, notAfter);
        
        if (OperatingSystem.IsWindows())
        {
            cert.FriendlyName = "StacksAtlas Sovereign Root CA";
        }

        var pfxBytes = cert.Export(X509ContentType.Pfx, CaPassword);
        File.WriteAllBytes(caPfxPath, pfxBytes);

        var certBytes = cert.Export(X509ContentType.Cert);
        File.WriteAllBytes(caCrtPath, certBytes);

        log?.Invoke($"Root CA Certificate generated successfully. Public cert saved to {caCrtPath}", null);
        
        return X509CertificateLoader.LoadPkcs12FromFile(caPfxPath, CaPassword, Pkcs12LoadOptions.KeyStorageFlags);
    }

    public static X509Certificate2 GetOrCreateServerCertificateStatic(
        IEnumerable<string>? additionalDnsNames = null,
        Action<string, Exception?>? log = null)
    {
        lock (_staticLock)
        {
            var normalizedSans = NormalizeDnsNames(additionalDnsNames);

            // Return cached cert if valid and already includes requested SANs
            if (_staticServerCert != null &&
                _staticServerCert.NotAfter > DateTime.UtcNow.AddDays(30) &&
                CertificateIncludesDnsNames(_staticServerCert, normalizedSans))
            {
                return _staticServerCert;
            }

            var rootCert = GetOrCreateRootCaStatic(log);

            log?.Invoke("Generating CA-signed TLS server certificate for mTLS port...", null);

            var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var subjectName = new X500DistinguishedName("CN=StacksAtlas Hub mTLS Server, O=StacksAtlas");
            var request = new CertificateRequest(subjectName, serverKey, HashAlgorithmName.SHA256);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            // Server Authentication EKU (OID 1.3.6.1.5.5.7.3.1)
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            if (normalizedSans.Count > 0)
            {
                var sanBuilder = new SubjectAlternativeNameBuilder();
                foreach (var dns in normalizedSans)
                    sanBuilder.AddDnsName(dns);
                request.CertificateExtensions.Add(sanBuilder.Build());
            }

            // Authority Key Identifier  -  links this cert to the Root CA
            var skiExtension = rootCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault();
            if (skiExtension != null && !string.IsNullOrEmpty(skiExtension.SubjectKeyIdentifier))
            {
                var skiBytes = Convert.FromHexString(skiExtension.SubjectKeyIdentifier);
                request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(skiBytes));
            }

            var serialNumber = new byte[16];
            RandomNumberGenerator.Fill(serialNumber);

            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = DateTimeOffset.UtcNow.AddYears(2);

            var privateKey = rootCert.GetECDsaPrivateKey() ?? throw new CryptographicException("CA private key must be ECDsa.");
            var generator = X509SignatureGenerator.CreateForECDsa(privateKey);
            var serverCertWithoutKey = request.Create(rootCert.SubjectName, generator, notBefore, notAfter, serialNumber);

            // Export to PFX to ensure the private key is fully owned by the certificate 
            // and not disposed when the serverKey is GC'd.
            using var serverCertWithKey = serverCertWithoutKey.CopyWithPrivateKey(serverKey);
            var pfxBytes = serverCertWithKey.Export(X509ContentType.Pfx, CaPassword);
            
            _staticServerCert = X509CertificateLoader.LoadPkcs12(pfxBytes, CaPassword, Pkcs12LoadOptions.KeyStorageFlags);

            log?.Invoke($"CA-signed TLS server certificate generated. Serial: {_staticServerCert.SerialNumber}, Expires: {notAfter}", null);

            serverKey.Dispose();

            return _staticServerCert;
        }
    }

    public X509Certificate2 GetOrCreateRootCa()
    {
        return GetOrCreateRootCaStatic((msg, ex) =>
        {
            if (ex != null) _logger.LogError(ex, "{Message}", msg);
            else _logger.LogInformation("{Message}", msg);
        });
    }

    public X509Certificate2 GetOrCreateServerCertificate()
    {
        return GetOrCreateServerCertificateStatic(null, (msg, ex) =>
        {
            if (ex != null) _logger.LogError(ex, "{Message}", msg);
            else _logger.LogInformation("{Message}", msg);
        });
    }

    public void EnsureServerCertificateIncludesDnsNames(IEnumerable<string>? dnsNames)
    {
        var normalized = NormalizeDnsNames(dnsNames);
        if (normalized.Count == 0)
            return;

        lock (_staticLock)
        {
            if (_staticServerCert != null &&
                _staticServerCert.NotAfter > DateTime.UtcNow.AddDays(30) &&
                CertificateIncludesDnsNames(_staticServerCert, normalized))
            {
                return;
            }

            _staticServerCert = null;
        }

        GetOrCreateServerCertificateStatic(normalized, (msg, ex) =>
        {
            if (ex != null) _logger.LogError(ex, "{Message}", msg);
            else _logger.LogInformation("{Message}", msg);
        });
    }

    private static List<string> NormalizeDnsNames(IEnumerable<string>? dnsNames)
    {
        if (dnsNames == null)
            return [];

        return dnsNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim().TrimEnd('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool CertificateIncludesDnsNames(X509Certificate2 certificate, IReadOnlyCollection<string> requiredNames)
    {
        if (requiredNames.Count == 0)
            return true;

        var extension = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (extension == null)
            return false;

        foreach (var required in requiredNames)
        {
            if (!extension.EnumerateDnsNames().Any(existing =>
                    string.Equals(existing, required, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    public static void ResetStaticCache()
    {
        lock (_staticLock)
        {
            _staticRootCa = null;
            _staticServerCert = null;
            _staticRevokedSerials.Clear();
        }
    }


    public X509Certificate2 SignNodeClientCertificate(byte[] csrBytes, string nodeId, TimeSpan validity)
    {
        var rootCert = GetOrCreateRootCa();
        
        CertificateRequest request;
        if (IsPem(csrBytes))
        {
            var pemText = Encoding.UTF8.GetString(csrBytes);
            var derBytes = ConvertPemToDer(pemText, "CERTIFICATE REQUEST");
            request = CertificateRequest.LoadSigningRequest(derBytes, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default);
        }
        else
        {
            request = CertificateRequest.LoadSigningRequest(csrBytes, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default);
        }

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.Add(validity);

        var serialNumber = new byte[16];
        RandomNumberGenerator.Fill(serialNumber);

        request.CertificateExtensions.Clear();
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var skiExtension = rootCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault();
        if (skiExtension != null && !string.IsNullOrEmpty(skiExtension.SubjectKeyIdentifier))
        {
            var skiBytes = Convert.FromHexString(skiExtension.SubjectKeyIdentifier);
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(skiBytes));
        }

        var privateKey = rootCert.GetECDsaPrivateKey() ?? throw new CryptographicException("CA private key must be ECDsa.");
        var generator = X509SignatureGenerator.CreateForECDsa(privateKey);
        var clientCert = request.Create(rootCert.SubjectName, generator, notBefore, notAfter, serialNumber);

        _logger.LogInformation("Signed client certificate for Node {NodeId}. Serial: {Serial}", nodeId, clientCert.SerialNumber);
        
        return clientCert;
    }

    public void RevokeCertificate(string serialNumber)
    {
        if (string.IsNullOrEmpty(serialNumber)) return;
        
        _staticRevokedSerials[serialNumber] = 0;
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HubDbContext>();
            var node = context.Nodes.FirstOrDefault(n => n.CertificateSerialNumber == serialNumber);
            if (node != null)
            {
                node.IsRevoked = true;
                context.SaveChanges();
                _logger.LogInformation("Revoked certificate serial {Serial} in Hub database.", serialNumber);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist certificate revocation for serial {Serial}", serialNumber);
        }
    }

    public bool IsRevoked(string serialNumber)
    {
        return IsRevokedStatic(serialNumber);
    }

    public static bool IsRevokedStatic(string serialNumber)
    {
        if (string.IsNullOrEmpty(serialNumber)) return false;
        return _staticRevokedSerials.ContainsKey(serialNumber);
    }

    public string SignData(byte[] data)
    {
        var rootCert = GetOrCreateRootCa();
        using var privateKey = rootCert.GetECDsaPrivateKey() ?? throw new CryptographicException("CA private key must be ECDsa.");
        var signatureBytes = privateKey.SignData(data, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signatureBytes);
    }

    private static bool IsPem(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return false;
        var text = Encoding.UTF8.GetString(bytes);
        return text.Contains("-----BEGIN");
    }

    private static byte[] ConvertPemToDer(string pemText, string header)
    {
        var lines = pemText.Split('\n');
        var sb = new StringBuilder();
        bool inside = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith($"-----BEGIN {header}"))
            {
                inside = true;
                continue;
            }
            if (trimmed.StartsWith($"-----END {header}"))
            {
                inside = false;
                break;
            }
            if (inside)
            {
                sb.Append(trimmed);
            }
        }
        return Convert.FromBase64String(sb.ToString());
    }
}
