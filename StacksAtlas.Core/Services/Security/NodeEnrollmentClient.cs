using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Security;

public sealed record NodeEnrollmentResult(bool Success, string? ErrorMessage = null);

public class NodeEnrollmentClient
{
    private readonly ILogger<NodeEnrollmentClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly FederationSettingsStore _settingsStore;
    private readonly IHardwareIdProvider _hwidProvider;
    private readonly string _certsDir;
    private readonly string _clientCertEncPath;
    private readonly string _hubRootCrtPath;

    private const string PfxPassword = "NodeSecurePasswordInternal_999";
    private static readonly byte[] SystemStorageSalt = "StacksAtlas_Node_Vault_Entropy_2026"u8.ToArray();

    public NodeEnrollmentClient(
        ILogger<NodeEnrollmentClient> logger, 
        HttpClient httpClient, 
        FederationSettingsStore settingsStore,
        IHardwareIdProvider hwidProvider)
    {
        _logger = logger;
        _httpClient = httpClient;
        _settingsStore = settingsStore;
        _hwidProvider = hwidProvider;
        _certsDir = StacksAtlas.Core.Helpers.PlatformPaths.GetCertDirectory();
        _clientCertEncPath = Path.Combine(_certsDir, "node_client.enc");
        _hubRootCrtPath = Path.Combine(_certsDir, "hub_root.crt");
    }

    public Task<NodeEnrollmentResult> EnrollAsync(string enrollmentString, string friendlyName, string? client, string? building, string? room) =>
        EnrollInternalAsync(enrollmentString, friendlyName, client, building, room);

    private async Task<NodeEnrollmentResult> EnrollInternalAsync(string enrollmentString, string friendlyName, string? client, string? building, string? room)
    {
        try
        {
            if (!enrollmentString.StartsWith("sa-enroll://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Invalid enrollment connection string format.");
                return new NodeEnrollmentResult(false, "Invalid enrollment connection string format.");
            }

            var uri = new Uri(enrollmentString);
            var hubHost = uri.Host;
            var mtlsPort = uri.Port;
            
            // Manual query string parsing to prevent framework dependencies
            var queryString = uri.Query;
            if (queryString.StartsWith("?")) queryString = queryString[1..];
            string token = string.Empty;
            foreach (var part in queryString.Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0].Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    token = Uri.UnescapeDataString(kv[1]);
                    break;
                }
            }

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("Enrollment token is missing from the connection string.");
                return new NodeEnrollmentResult(false, "Enrollment token is missing from the connection string.");
            }

            var bootstrapUrl = HubEnrollmentEndpointResolver.BuildBootstrapEnrollUrl(hubHost);

            _logger.LogInformation("Generating ECDsa P-256 local key pair...");
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            
            var decodedBytes = Convert.FromBase64String(token);
            var decodedString = Encoding.UTF8.GetString(decodedBytes);
            var tokenParts = decodedString.Split(':');
            var nodeId = tokenParts[0];

            var subjectName = $"CN={nodeId}";
            var csrRequest = new CertificateRequest(subjectName, ecdsa, HashAlgorithmName.SHA256);
            var csrDer = csrRequest.CreateSigningRequest();
            var csrPem = $"-----BEGIN CERTIFICATE REQUEST-----\n{Convert.ToBase64String(csrDer, Base64FormattingOptions.InsertLineBreaks)}\n-----END CERTIFICATE REQUEST-----";

            _logger.LogInformation("Signing enrollment token for proof-of-possession...");
            var tokenBytes = Convert.FromBase64String(token);
            var signatureBytes = ecdsa.SignData(tokenBytes, HashAlgorithmName.SHA256);
            var signatureBase64 = Convert.ToBase64String(signatureBytes);

            var enrollPayload = new EnrollRequestDto
            {
                NodeId = nodeId,
                Token = token,
                Csr = csrPem,
                Signature = signatureBase64,
                FriendlyName = friendlyName,
                Client = client,
                Building = building,
                Room = room,
                MtlsPort = mtlsPort,
                HardwareId = _hwidProvider.GetHardwareId()
            };

            _logger.LogInformation("Sending Certificate Signing Request to Hub bootstrap endpoint {Url}...", bootstrapUrl);
            
            using var handler = _settingsStore.Current.AllowUntrustedHubs
                ? new SocketsHttpHandler { SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true } }
                : null;
            using var customClient = handler != null ? new HttpClient(handler) : null;
            var activeClient = customClient ?? _httpClient;

            var response = await activeClient.PostAsJsonAsync(bootstrapUrl, enrollPayload);
            if (!response.IsSuccessStatusCode)
            {
                var errMsg = await response.Content.ReadAsStringAsync();
                _logger.LogError("Enrollment request failed: {Status} - {Message}", response.StatusCode, errMsg);
                return new NodeEnrollmentResult(false, ParseEnrollmentError(errMsg));
            }

            var result = await response.Content.ReadFromJsonAsync<EnrollResponseDto>();
            if (result == null || string.IsNullOrEmpty(result.ClientCertificateBase64) || string.IsNullOrEmpty(result.HubRootCertificateBase64))
            {
                _logger.LogError("Enrollment response was empty or invalid.");
                return new NodeEnrollmentResult(false, "Enrollment response from Hub was empty or invalid.");
            }

            _logger.LogInformation("CSR signed successfully by Hub CA. Processing certificates...");
            
            var clientCertBytes = Convert.FromBase64String(result.ClientCertificateBase64);
            var hubRootCertBytes = Convert.FromBase64String(result.HubRootCertificateBase64);

            using var clientCertWithoutKey = X509CertificateLoader.LoadCertificate(clientCertBytes);
            using var clientCertWithKey = clientCertWithoutKey.CopyWithPrivateKey(ecdsa);

            var pfxBytes = clientCertWithKey.Export(X509ContentType.Pfx, PfxPassword);
            var encryptedPfx = EncryptPayload(pfxBytes);

            Directory.CreateDirectory(_certsDir);
            await File.WriteAllBytesAsync(_clientCertEncPath, encryptedPfx);
            await File.WriteAllBytesAsync(_hubRootCrtPath, hubRootCertBytes);

            _logger.LogInformation("Node client certificate and Hub CA public certificate securely saved to disk.");
            return new NodeEnrollmentResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node enrollment failed due to an unhandled exception.");
            return new NodeEnrollmentResult(false, ex.Message);
        }
    }

    internal static string ParseEnrollmentError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return "Enrollment was rejected by the Hub.";

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("message", out var messageProp))
            {
                var message = messageProp.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                    return message;
            }
        }
        catch
        {
            // Plain-text error body from Hub
        }

        return responseBody.Trim().Trim('"');
    }

    public (X509Certificate2? clientCert, X509Certificate2? hubRootCert) LoadCertificates()
    {
        try
        {
            if (!File.Exists(_clientCertEncPath) || !File.Exists(_hubRootCrtPath))
            {
                return (null, null);
            }

            var encryptedPfx = File.ReadAllBytes(_clientCertEncPath);
            var pfxBytes = DecryptPayload(encryptedPfx);

            var clientCert = X509CertificateLoader.LoadPkcs12(pfxBytes, PfxPassword, Pkcs12LoadOptions.KeyStorageFlags);
            
            var hubRootBytes = File.ReadAllBytes(_hubRootCrtPath);
            var hubRootCert = X509CertificateLoader.LoadCertificate(hubRootBytes);

            return (clientCert, hubRootCert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load node mTLS certificates from disk.");
            return (null, null);
        }
    }

    public void PurgeCertificates()
    {
        try
        {
            if (File.Exists(_clientCertEncPath)) File.Delete(_clientCertEncPath);
            if (File.Exists(_hubRootCrtPath)) File.Delete(_hubRootCrtPath);
            _logger.LogInformation("Node mTLS certificates purged from disk.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge node certificates.");
        }
    }

    private byte[] EncryptPayload(byte[] data)
    {
        var key = DeriveMachineKey();
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var cipher = new byte[data.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, cipher, tag, SystemStorageSalt);

        var result = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, result, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length + cipher.Length, tag.Length);
        return result;
    }

    private byte[] DecryptPayload(byte[] data)
    {
        if (data.Length < 12 + 16)
            throw new CryptographicException("Invalid protected payload length.");

        var key = DeriveMachineKey();
        var nonce = data[..12];
        var tag = data[^16..];
        var cipher = data[12..^16];
        var decrypted = new byte[cipher.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, decrypted, SystemStorageSalt);
        return decrypted;
    }

    private byte[] DeriveMachineKey()
    {
        var uuid = GetSystemUuid();
        return Rfc2898DeriveBytes.Pbkdf2(uuid, SystemStorageSalt, 10000, HashAlgorithmName.SHA256, 32);
    }

    private static string GetSystemUuid()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrEmpty(guid)) return guid;
            }
            catch { /* Fallback */ }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                if (File.Exists("/etc/machine-id"))
                {
                    return File.ReadAllText("/etc/machine-id").Trim();
                }
                if (File.Exists("/var/lib/dbus/machine-id"))
                {
                    return File.ReadAllText("/var/lib/dbus/machine-id").Trim();
                }
            }
            catch { /* Fallback */ }
        }
        return "stacksatlas-fallback-machine-uuid-default-2026";
    }

    public class EnrollRequestDto
    {
        public string NodeId { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Csr { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string? Client { get; set; }
        public string? Building { get; set; }
        public string? Room { get; set; }
        public int MtlsPort { get; set; }
        public string? HardwareId { get; set; }
    }

    public class EnrollResponseDto
    {
        public string ClientCertificateBase64 { get; set; } = string.Empty;
        public string HubRootCertificateBase64 { get; set; } = string.Empty;
    }
}
