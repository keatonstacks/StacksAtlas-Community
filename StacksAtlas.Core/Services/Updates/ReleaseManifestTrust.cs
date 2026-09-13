namespace StacksAtlas.Core.Services.Updates;

/// <summary>
/// Ed25519 public keys trusted to sign release manifests.
/// Embedded SPKI is synced at build from scripts/sync-manifest-trust.mjs (signing key in scripts/.env.release).
/// Emergency override: STACKSATLAS_MANIFEST_PUBLIC_KEY_B64 environment variable.
/// </summary>
public static partial class ReleaseManifestTrust
{
    public const int RawPublicKeyLength = 32;

    public static byte[] ResolveTrustedPublicKeySpki()
    {
        var overrideKey = Environment.GetEnvironmentVariable("STACKSATLAS_MANIFEST_PUBLIC_KEY_B64");
        if (!string.IsNullOrWhiteSpace(overrideKey))
            return Convert.FromBase64String(overrideKey.Trim());

        return Convert.FromBase64String(EmbeddedPublicKeySpkiBase64);
    }

    /// <summary>Extracts the 32-byte Ed25519 seed from PKCS#8 DER (RFC 8410).</summary>
    public static byte[] ExtractRawPrivateSeed(ReadOnlySpan<byte> privateKeyPkcs8)
    {
        if (privateKeyPkcs8.Length < RawPublicKeyLength)
            throw new FormatException("Ed25519 PKCS#8 is too short.");

        return privateKeyPkcs8[^RawPublicKeyLength..].ToArray();
    }

    /// <summary>Extracts the 32-byte Ed25519 public key from SPKI DER (RFC 8410).</summary>
    public static byte[] ExtractRawPublicKey(ReadOnlySpan<byte> publicKeySpki)
    {
        if (publicKeySpki.Length < RawPublicKeyLength)
            throw new FormatException("Ed25519 SPKI is too short.");

        var raw = publicKeySpki[^RawPublicKeyLength..].ToArray();
        if (raw.Length != RawPublicKeyLength)
            throw new FormatException("Ed25519 public key must be 32 bytes.");

        return raw;
    }
}
