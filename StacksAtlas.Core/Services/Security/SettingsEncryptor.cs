using System.Text;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Core.Services.Security;

public class SettingsEncryptor(DatabaseSecurityService databaseSecurity)
{
    private readonly DatabaseSecurityService _databaseSecurity = databaseSecurity;
    private const string Prefix = "ENC:";

    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (IsEncrypted(value)) return value;

        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = _databaseSecurity.Protect(bytes);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (!IsEncrypted(value)) return value;

        try
        {
            var base64 = value.Substring(Prefix.Length);
            var protectedBytes = Convert.FromBase64String(base64);
            var decryptedBytes = _databaseSecurity.Unprotect(protectedBytes);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public bool IsEncrypted(string? value) => value?.StartsWith(Prefix) == true;
}
