using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Security;

public class DatabaseSecurityService
{
    private readonly ILogger<DatabaseSecurityService> _logger;
    private readonly string _keyFilePath;
    private readonly byte[]? _rootKey;
    private readonly string? _rootKeySource;

    private const string DbKeyEnvName = "STACKSATLAS_DB_KEY";
    private const string DbKeyPathEnvName = "STACKSATLAS_DB_KEY_PATH";
    private static readonly byte[] InternalSalt = "StacksAtlas_Core_DB_Entropy_2024"u8.ToArray();

    public DatabaseSecurityService(ILogger<DatabaseSecurityService> logger)
    {
        _logger = logger;
        _keyFilePath = StacksAtlas.Core.Helpers.PlatformPaths.GetDatabaseKeyPath();
        (_rootKey, _rootKeySource) = ResolveRootKey();
    }

    public string GetDatabasePassword()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                var encryptedData = File.ReadAllBytes(_keyFilePath);
                var decryptedData = Unprotect(encryptedData);
                try
                {
                    return Encoding.UTF8.GetString(decryptedData);
                }
                finally
                {
                    // Wipe the sensitive byte array from memory
                    Array.Clear(decryptedData, 0, decryptedData.Length);
                }
            }

            // Generate a high-entropy 64-character hex key (256 bits of entropy)
            _logger.LogInformation("Generating high-entropy database encryption key...");
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            
            var newKey = Convert.ToHexString(bytes);
            var keyBytes = Encoding.UTF8.GetBytes(newKey);
            var encryptedKey = Protect(keyBytes);

            try
            {
                var keyDir = Path.GetDirectoryName(_keyFilePath);
                if (!string.IsNullOrEmpty(keyDir)) Directory.CreateDirectory(keyDir);
                File.WriteAllBytes(_keyFilePath, encryptedKey);
                SetSecureFilePermissions(_keyFilePath);
                _logger.LogInformation("Database encryption key generated and secured at {Path}", _keyFilePath);
                return newKey;
            }
            finally
            {
                Array.Clear(keyBytes, 0, keyBytes.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Security Failure: Could not manage database encryption keys.");
            throw new Exception("Critical Security Failure: Database key vault access denied.", ex);
        }
    }

    public void SetDatabasePassword(string newPassword)
    {
        try
        {
            _logger.LogInformation("Updating database encryption key vault with custom secret...");
            var keyBytes = Encoding.UTF8.GetBytes(newPassword);
            var encryptedKey = Protect(keyBytes);

            var keyDir = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(keyDir)) Directory.CreateDirectory(keyDir);

            File.WriteAllBytes(_keyFilePath, encryptedKey);
            SetSecureFilePermissions(_keyFilePath);
            _logger.LogInformation("Database encryption key vault updated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set custom database encryption key.");
            throw new Exception("Security Failure: Could not persist new database encryption key.", ex);
        }
    }

    private (byte[]? rootKey, string? source) ResolveRootKey()
    {
        var envKey = Environment.GetEnvironmentVariable(DbKeyEnvName);
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            _logger.LogInformation("Using explicit {EnvVar} for database key vault protection.", DbKeyEnvName);
            return (DeriveRootKey(envKey.Trim()), DbKeyEnvName);
        }

        var envPath = Environment.GetEnvironmentVariable(DbKeyPathEnvName);
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            var secret = File.ReadAllText(envPath).Trim();
            if (!string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogInformation("Using explicit secret file from {EnvVar} for database key vault protection.", DbKeyPathEnvName);
                return (DeriveRootKey(secret), DbKeyPathEnvName);
            }
        }

        return (null, null);
    }

    private static byte[] DeriveRootKey(string secret)
    {
        using var hmac = new HMACSHA256(InternalSalt);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(secret));
    }

    public byte[] Protect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(data, InternalSalt, DataProtectionScope.LocalMachine);
        }

        if (_rootKey != null)
        {
            return ProtectWithRootKey(data, _rootKey);
        }

        _logger.LogWarning(
            "No explicit database key vault secret configured for Linux/macOS. Writing raw key material with restricted file permissions. " +
            "Set {EnvVar} or {EnvVarPath} for stronger protection.",
            DbKeyEnvName, DbKeyPathEnvName);

        return data;
    }

    public byte[] Unprotect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return ProtectedData.Unprotect(data, InternalSalt, DataProtectionScope.LocalMachine);
            }
            catch (CryptographicException)
            {
                _logger.LogWarning("Detected legacy DPAPI key without salt. Falling back to unpeppered decryption.");
                return ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);
            }
        }

        if (_rootKey != null)
        {
            try
            {
                return UnprotectWithRootKey(data, _rootKey);
            }
            catch (CryptographicException ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt database key with explicit root secret. Falling back to legacy Linux migration checks.");
            }
        }

        if (IsAsciiHexString(data))
        {
            return data;
        }

        if (TryUnscrambleLegacyXor(data, out var legacyBytes))
        {
            return legacyBytes;
        }

        return data;
    }

    private static byte[] ProtectWithRootKey(byte[] data, byte[] rootKey)
    {
        var nonce = new byte[12];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(nonce);

        var cipher = new byte[data.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(rootKey, 16);
        aes.Encrypt(nonce, data, cipher, tag, InternalSalt);

        var result = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, result, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length + cipher.Length, tag.Length);
        return result;
    }

    private static byte[] UnprotectWithRootKey(byte[] data, byte[] rootKey)
    {
        if (data.Length < 12 + 16)
            throw new CryptographicException("Invalid protected payload length.");

        var nonce = data[..12];
        var tag = data[^16..];
        var cipher = data[12..^16];
        var decrypted = new byte[cipher.Length];

        using var aes = new AesGcm(rootKey, 16);
        aes.Decrypt(nonce, cipher, tag, decrypted, InternalSalt);
        return decrypted;
    }

    private static bool TryUnscrambleLegacyXor(byte[] data, out byte[] result)
    {
        result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ InternalSalt[i % InternalSalt.Length]);
        }

        return IsAsciiHexString(result);
    }

    private static bool IsAsciiHexString(byte[] data)
    {
        if (data == null || data.Length != 64) return false;

        foreach (var b in data)
        {
            bool isDigit = b >= '0' && b <= '9';
            bool isLower = b >= 'a' && b <= 'f';
            bool isUpper = b >= 'A' && b <= 'F';
            if (!isDigit && !isLower && !isUpper)
            {
                return false;
            }
        }

        return true;
    }

    private static void SetSecureFilePermissions(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Best effort only; do not block startup on platforms that may not support Unix file modes.
            }
        }
    }
}
