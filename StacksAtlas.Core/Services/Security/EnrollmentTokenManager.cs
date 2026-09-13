using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Security;

public class EnrollmentTokenManager
{
    private readonly ILogger<EnrollmentTokenManager> _logger;
    private readonly DatabaseSecurityService _dbSecurity;

    public EnrollmentTokenManager(ILogger<EnrollmentTokenManager> logger, DatabaseSecurityService dbSecurity)
    {
        _logger = logger;
        _dbSecurity = dbSecurity;
    }

    public string GenerateToken(string nodeId, TimeSpan validity)
    {
        var expiry = DateTimeOffset.UtcNow.Add(validity).ToUnixTimeSeconds();
        var key = Encoding.UTF8.GetBytes(_dbSecurity.GetDatabasePassword());
        
        var payload = $"{nodeId}:{expiry}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(key);
        var signatureBytes = hmac.ComputeHash(payloadBytes);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        var token = $"{payload}:{signatureBase64}";
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        return Convert.ToBase64String(tokenBytes);
    }

    public bool TryValidateToken(string tokenStr, out string nodeId)
    {
        nodeId = string.Empty;
        if (string.IsNullOrWhiteSpace(tokenStr)) return false;

        try
        {
            var decodedBytes = Convert.FromBase64String(tokenStr);
            var decodedString = Encoding.UTF8.GetString(decodedBytes);

            var parts = decodedString.Split(':');
            if (parts.Length != 3) return false;

            var tNodeId = parts[0];
            var expiryStr = parts[1];
            var signatureBase64 = parts[2];

            if (!long.TryParse(expiryStr, out var expiry) || expiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                _logger.LogWarning("Token validation failed: expired or invalid expiry.");
                return false;
            }

            var key = Encoding.UTF8.GetBytes(_dbSecurity.GetDatabasePassword());
            var payload = $"{tNodeId}:{expiryStr}";
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            using var hmac = new HMACSHA256(key);
            var computedSignatureBytes = hmac.ComputeHash(payloadBytes);
            var computedSignatureBase64 = Convert.ToBase64String(computedSignatureBytes);

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignatureBase64), 
                Encoding.UTF8.GetBytes(signatureBase64)))
            {
                _logger.LogWarning("Token validation failed: signature mismatch.");
                return false;
            }

            nodeId = tNodeId;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed: unhandled exception.");
            return false;
        }
    }
}
