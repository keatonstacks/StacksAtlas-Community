using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LiteDB;
using Microsoft.Extensions.Configuration; // Standard IConfiguration
using Microsoft.IdentityModel.Tokens;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Core.Services.Auth;

public interface IAuthService
{
    bool AnyUsers();
    List<User> GetAllUsers();
    User? GetUserById(Guid id);
    bool UserExists(string username);
    User? RegisterAdmin(string username, string password);
    User? CreateUser(string username, string? password, string role, string? email = null, string provider = "Local", string? externalId = null);
    User GetOrCreateExternalUser(string provider, string externalId, string username, string? email, List<string>? groups = null);
    bool DeleteUser(Guid id);
    bool UpdateUser(Guid id, string? email, string? role);
    User? ValidateUser(string username, string password);
    bool ResetUserPassword(Guid userId, string newPassword);
    void ForceResetPassword(string username, string newPassword);
    bool UpdateAlertPreferences(Guid userId, string? alertEmail, bool alertsEnabled, bool alertOnDeviceDown, bool alertOnDeviceUp, bool alertOnNewDevice, string alertSeverity, bool webhookEnabled = false, Guid? preferredWebhookId = null, List<Guid>? preferredWebhookIds = null);
    string GenerateToken(User user, int expiryDays = 7);
    ClaimsPrincipal? GetPrincipalFromToken(string token, bool validateLifetime = true);
}

public class AuthService : IAuthService
{
    private readonly LiteDatabase _db;
    private readonly IConfiguration _config;
    private readonly SystemSettingsStore _settings;
    private readonly LdapService _ldap;
    private readonly StacksAtlas.Core.Abstractions.IClock _clock;

    public AuthService(LiteDatabase db, IConfiguration config, SystemSettingsStore settings, LdapService ldap, StacksAtlas.Core.Abstractions.IClock clock)
    {
        _db = db;
        _config = config;
        _settings = settings;
        _ldap = ldap;
        _clock = clock;
        Users.EnsureIndex(x => x.Username);
        Users.EnsureIndex(x => x.ExternalId);
    }

    private ILiteCollection<User> Users => _db.GetCollection<User>("users");

    public virtual bool AnyUsers() => Users.Count() > 0;
    public virtual List<User> GetAllUsers() => Users.FindAll().ToList();
    public User? GetUserById(Guid id) => Users.FindById(id);
    public bool UserExists(string username) => Users.Exists(u => u.Username.ToLower() == username.ToLower());

    public User? RegisterAdmin(string username, string password)
    {
        if (AnyUsers()) return null; // Only allow one setup for now

        var salt = GenerateSalt();
        var hashed = HashPassword(password, salt, 600000);

        var user = new User
        {
            Username = username,
            PasswordHash = hashed,
            Salt = $"v2:{salt}",
            Role = "Admin",
            IsActive = true,
            LastLoginAt = _clock.UtcNow
        };

        Users.Insert(user);
        return user;
    }

    public User? CreateUser(string username, string? password, string role, string? email = null, string provider = "Local", string? externalId = null)
    {
        // Don't allow duplicate usernames
        if (Users.Exists(u => u.Username.ToLower() == username.ToLower())) 
            return null;

        string? salt = null;
        string? hashed = null;

        if (provider == "Local" && !string.IsNullOrWhiteSpace(password))
        {
            salt = GenerateSalt();
            hashed = HashPassword(password, salt, 600000);
            salt = $"v2:{salt}";
        }
        else
        {
            // For SSO, AlertOnly or other passwordless accounts
            salt = "N/A";
            hashed = "N/A";
        }

        var user = new User
        {
            Username = username,
            PasswordHash = hashed ?? "N/A",
            Salt = salt ?? "N/A",
            Role = role,
            Email = email,
            Provider = provider,
            ExternalId = externalId,
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        Users.Insert(user);
        return user;
    }

    public User GetOrCreateExternalUser(string provider, string externalId, string username, string? email, List<string>? groups = null)
    {
        var user = Users.FindOne(u => u.Provider == provider && u.ExternalId == externalId);
        
        if (user == null)
        {
            // JIT Provisioning
            var role = ResolveRoleFromGroups(groups) ?? _settings.Current.Auth.DefaultRole;
            
            user = CreateUser(username, null, role, email, provider, externalId);
            
            // If username was taken, try to make it unique
            if (user == null)
            {
                var uniqueUsername = $"{username}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
                user = CreateUser(uniqueUsername, null, role, email, provider, externalId);
            }
        }
        else
        {
            // Update existing user info if needed
            bool changed = false;
            if (email != null && user.Email != email) { user.Email = email; changed = true; }
            
            // Re-evaluate role from groups on every login if role mapping is configured
            if (groups != null && _settings.Current.Auth.RoleMappings.Count > 0)
            {
                var newRole = ResolveRoleFromGroups(groups);
                if (newRole != null && user.Role != newRole)
                {
                    user.Role = newRole;
                    changed = true;
                }
            }

            if (changed) Users.Update(user);
        }

        user!.LastLoginAt = _clock.UtcNow;
        Users.Update(user);
        return user;
    }

    private string? ResolveRoleFromGroups(List<string>? groups)
    {
        if (groups == null || groups.Count == 0) return null;

        var mappings = _settings.Current.Auth.RoleMappings;
        if (mappings == null || mappings.Count == 0) return null;

        // Priority: Admin > Standard > Viewer
        var matchedRoles = mappings
            .Where(m => groups.Contains(m.ExternalGroup, StringComparer.OrdinalIgnoreCase))
            .Select(m => m.StacksAtlasRole)
            .ToList();

        if (matchedRoles.Contains("Admin")) return "Admin";
        if (matchedRoles.Contains("Standard")) return "Standard";
        if (matchedRoles.Contains("Viewer")) return "Viewer";

        return null;
    }

    public bool DeleteUser(Guid id) => Users.Delete(id);

    public bool UpdateUser(Guid id, string? email, string? role)
    {
        var user = Users.FindById(id);
        if (user == null) return false;
        
        if (email != null) user.Email = email;
        if (role != null) user.Role = role;
        
        return Users.Update(user);
    }

    public User? ValidateUser(string username, string password)
    {
        var user = Users.FindOne(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return null;
        if (!user.IsActive) return null; // Block inactive users
        
        // Prevent login attempts for AlertOnly or accounts with purposefully broken hashes
        if (user.Role == "AlertOnly" || user.Salt == "N/A" || user.PasswordHash == "N/A")
            return null;

        var isV2 = user.Salt.StartsWith("v2:");
        var actualSalt = isV2 ? user.Salt.Substring(3) : user.Salt;
        var iterations = isV2 ? 600000 : 10000;

        var hashed = HashPassword(password, actualSalt, iterations);
        
        var hashBytes = Convert.FromBase64String(hashed);
        var userHashBytes = Convert.FromBase64String(user.PasswordHash);

        if (CryptographicOperations.FixedTimeEquals(hashBytes, userHashBytes)) 
        {
            // Transparently upgrade legacy 10k hashes to 600k hashes
            if (!isV2)
            {
                var newSalt = GenerateSalt();
                user.Salt = $"v2:{newSalt}";
                user.PasswordHash = HashPassword(password, newSalt, 600000);
            }

            user.LastLoginAt = _clock.UtcNow;
            Users.Update(user);
            return user;
        }

        // --- LDAP Fallback ---
        if (_settings.Current.Auth.SsoEnabled && _settings.Current.Auth.Provider == "LDAP")
        {
            if (_ldap.ValidateUser(username, password, out var email, out var groups))
            {
                // JIT Provisioning / Sync
                return GetOrCreateExternalUser("LDAP", username, username, email, groups);
            }
        }

        return null;
    }

    public bool ResetUserPassword(Guid userId, string newPassword)
    {
        var user = Users.FindById(userId);
        if (user == null) return false;

        var salt = GenerateSalt();
        user.Salt = $"v2:{salt}";
        user.PasswordHash = HashPassword(newPassword, salt, 600000);
        return Users.Update(user);
    }

    // CLI Reset capability
    public void ForceResetPassword(string username, string newPassword)
    {
        var user = Users.FindOne(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return;

        var salt = GenerateSalt();
        user.Salt = $"v2:{salt}";
        user.PasswordHash = HashPassword(newPassword, salt, 600000);
        user.IsActive = true; // Ensure they can log in
        Users.Update(user);
    }

    public bool UpdateAlertPreferences(Guid userId, string? alertEmail, bool alertsEnabled, 
        bool alertOnDeviceDown, bool alertOnDeviceUp, bool alertOnNewDevice, string alertSeverity,
        bool webhookEnabled = false, Guid? preferredWebhookId = null, List<Guid>? preferredWebhookIds = null)
    {
        var user = Users.FindById(userId);
        if (user == null) return false;

        user.AlertEmail = alertEmail;
        user.AlertsEnabled = alertsEnabled;
        user.AlertOnDeviceDown = alertOnDeviceDown;
        user.AlertOnDeviceUp = alertOnDeviceUp;
        user.AlertOnNewDevice = alertOnNewDevice;
        user.AlertSeverity = alertSeverity;
        user.WebhookEnabled = webhookEnabled;
        user.PreferredWebhookId = preferredWebhookId;
        user.PreferredWebhookIds = preferredWebhookIds ?? new();
        
        Users.Update(user);
        return true;
    }

    public User? GetUserByIdTyped(Guid id) => Users.FindById(id);

    private static byte[]? _fallbackJwtKey;

    /// <summary>
    /// Centralized JWT key resolution. Used by both Program.cs (validation) and GenerateToken (signing)
    /// to guarantee both sides always agree on the same key material.
    /// </summary>
    public static byte[] ResolveSigningKey(IConfiguration config)
    {
        var configuredKey = config["Jwt:Key"];
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            return Encoding.UTF8.GetBytes(configuredKey);
        }

        // Generate a secure in-memory fallback key if not configured.
        // Tradeoff: Users will need to re-login after an app restart, but
        // the appliance is immune to token forgery from open-source code.
        if (_fallbackJwtKey == null)
        {
            _fallbackJwtKey = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(_fallbackJwtKey);
        }
        return _fallbackJwtKey;
    }

    public string GenerateToken(User user, int expiryDays = 7)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("id", user.Id.ToString()),
            new Claim("provider", user.Provider)
        };

        return GenerateTokenFromClaims(claims, expiryDays);
    }

    private string GenerateTokenFromClaims(IEnumerable<Claim> claims, int expiryDays)
    {
        var keyBytes = ResolveSigningKey(_config);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "StacksAtlas",
            audience: "StacksAtlasUsers",
            claims: claims,
            expires: _clock.Now.AddDays(expiryDays),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? GetPrincipalFromToken(string token, bool validateLifetime = true)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var keyBytes = ResolveSigningKey(_config);

        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                ValidateIssuer = true,
                ValidIssuer = "StacksAtlas",
                ValidateAudience = true,
                ValidAudience = "StacksAtlasUsers",
                ValidateLifetime = validateLifetime,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    // --- Crypto Helpers ---

    private static string GenerateSalt()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt, int iterations)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterations, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(hash);
    }
}
