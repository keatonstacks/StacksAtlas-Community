using System.Security.Cryptography.X509Certificates;

namespace StacksAtlas.Core.Services.Security;

internal static class Pkcs12LoadOptions
{
    /// <summary>
    /// Windows: persist in CAPI/CNG. Linux: ephemeral in-memory key. macOS: exportable temp on-disk key (no Keychain).
    /// </summary>
    internal static X509KeyStorageFlags KeyStorageFlags
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet;
            if (OperatingSystem.IsMacOS())
                return X509KeyStorageFlags.Exportable;
            return X509KeyStorageFlags.EphemeralKeySet;
        }
    }
}
