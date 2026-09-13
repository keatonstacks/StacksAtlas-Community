using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using LiteDB;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Auth;

/// <summary>
/// Result object used when a new API key is generated.
/// The RawKey is ONLY returned once and never stored.
/// </summary>
public record GeneratedApiKeyResult(Guid Id, string RawKey, string Prefix);

public class ApiKeyService
{
    private readonly LiteDatabase _db;
    private readonly ILogger<ApiKeyService> _logger;
    
    // Prefix to easily identify StacksAtlas API keys in logs and headers
    private const string KeyPrefix = "sa_"; 

    private readonly StacksAtlas.Core.Abstractions.IClock _clock;

    public ApiKeyService(LiteDatabase db, ILogger<ApiKeyService> logger, StacksAtlas.Core.Abstractions.IClock clock)
    {
        _db = db;
        _logger = logger;
        _clock = clock;
        // Build indices for quick lookups
        ApiKeys.EnsureIndex(x => x.UserId);
        ApiKeys.EnsureIndex(x => x.KeyHash);
    }

    private ILiteCollection<ApiKey> ApiKeys => _db.GetCollection<ApiKey>("api_keys");

    /// <summary>
    /// Generates a new cryptographically secure opaque token for a user.
    /// This is the ONLY time the raw token will be available.
    /// </summary>
    public GeneratedApiKeyResult CreateApiKey(Guid userId, string label)
    {
        // 1. Generate 32 bytes of secure entropy
        var secretBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(secretBytes);
        
        // 2. Format the raw secret using Base64Url encoding to make it HTTP safe
        var rawSecret = Base64UrlEncode(secretBytes);
        var fullRawKey = $"{KeyPrefix}{rawSecret}";
        
        // 3. Extract a safe, readable prefix for display purposes
        var displayPrefix = fullRawKey.Substring(0, 8) + "...";

        // 4. Hash the raw secret for storage using fast SHA-256 (no salt needed for 256-bit entropy)
        var keyHash = FastHashSecret(fullRawKey);

        var record = new ApiKey
        {
            UserId = userId,
            Label = label,
            KeyHash = keyHash,
            Salt = "v2", // Marker for new fast-hash keys
            KeyPrefix = displayPrefix,
            CreatedAt = _clock.UtcNow,
            IsActive = true
        };

        ApiKeys.Insert(record);

        return new GeneratedApiKeyResult(record.Id, fullRawKey, displayPrefix);
    }

    /// <summary>
    /// Retrieves all API keys belonging to a specific user.
    /// Used for the Settings UI.
    /// </summary>
    public List<ApiKey> GetUserKeys(Guid userId)
    {
        return ApiKeys.Find(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ToList();
    }
    
    /// <summary>
    /// Revokes an API Key, immediately neutralizing its access.
    /// </summary>
    public bool RevokeApiKey(Guid userId, Guid keyId)
    {
        // Ensure the user actually owns the key before deleting
        var key = ApiKeys.FindById(keyId);
        if (key == null || key.UserId != userId) return false;

        return ApiKeys.Delete(keyId);
    }

    /// <summary>
    /// Validates a raw API key submitted in an HTTP request.
    /// If valid, the LastUsedAt timestamp is updated to provide auditing feedback.
    /// </summary>
    public ApiKey? ValidateAndTrackKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(KeyPrefix))
            return null;

        var fastHash = FastHashSecret(rawKey);

        // 1. FAST PATH: O(1) Indexed Lookup for V2 keys
        var key = ApiKeys.FindOne(x => x.KeyHash == fastHash && x.IsActive);
        
        if (key == null)
        {
            // 2. SLOW PATH (Fallback): Check legacy PBKDF2 keys
            var legacyKeys = ApiKeys.Find(x => x.IsActive && x.Salt != "v2").ToList();
            foreach (var legacyKey in legacyKeys)
            {
                var computedHash = HashSecret(rawKey, legacyKey.Salt);
                
                // Constant-time comparison
                if (CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(computedHash), 
                    Convert.FromBase64String(legacyKey.KeyHash)))
                {
                    key = legacyKey;
                    
                    // Transparently upgrade to V2 on successful auth
                    key.KeyHash = fastHash;
                    key.Salt = "v2";
                    ApiKeys.Update(key);
                    break;
                }
            }
        }
        else
        {
            // Ensure constant time comparison for fast path too
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(fastHash), 
                Convert.FromBase64String(key.KeyHash)))
            {
                key = null;
            }
        }

        if (key != null)
        {
            // Debounce DB updates (only write if > 5 minutes since last write)
            if (key.LastUsedAt == null || (_clock.UtcNow - key.LastUsedAt.Value).TotalMinutes > 5)
            {
                key.LastUsedAt = _clock.UtcNow;
                var updated = ApiKeys.Update(key);
                _logger.LogInformation("API KEY: Authenticated successfully. Prefix: {Prefix}, DB Updated: {Updated}", key.KeyPrefix, updated);
            }
            return key;
        }

        return null;
    }

    // --- Cryptographic Helpers ---

    private static string FastHashSecret(string secret)
    {
        var bytes = global::System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string GenerateSalt()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashSecret(string secret, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        // Using high iteration count for strong security against brute force
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, saltBytes, 100000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(hash);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
