using System.Text.RegularExpressions;
using Chaos.NaCl;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

public sealed partial class ReleaseManifestVerifier(ILogger<ReleaseManifestVerifier> logger) : IReleaseManifestVerifier
{
    public const int Ed25519SignatureLength = 64;
    public const int MaxManifestJsonBytes = 512 * 1024;
    public const int MaxSignatureBytes = 1024;

    public ReleaseManifest VerifyAndDeserialize(ReadOnlySpan<byte> manifestJsonUtf8, ReadOnlySpan<byte> detachedSignature)
    {
        if (manifestJsonUtf8.Length == 0)
            throw new ReleaseManifestVerificationException("Manifest body is empty.");
        if (manifestJsonUtf8.Length > MaxManifestJsonBytes)
            throw new ReleaseManifestVerificationException("Manifest exceeds maximum allowed size.");
        if (detachedSignature.Length == 0 || detachedSignature.Length > MaxSignatureBytes)
            throw new ReleaseManifestVerificationException("Signature is missing or exceeds maximum allowed size.");

        var signatureBytes = NormalizeSignature(detachedSignature);
        var canonicalBytes = ReleaseManifestCanonicalJson.ToCanonicalUtf8(manifestJsonUtf8);

        if (!VerifyEd25519(canonicalBytes, signatureBytes))
        {
            logger.LogWarning("Release manifest Ed25519 signature verification failed.");
            throw new ReleaseManifestVerificationException("Manifest signature is invalid.");
        }

        var manifest = ReleaseManifestCanonicalJson.DeserializeVerified(manifestJsonUtf8);
        ValidateManifestShape(manifest);
        return manifest;
    }

    private static byte[] NormalizeSignature(ReadOnlySpan<byte> detachedSignature)
    {
        if (detachedSignature.Length == Ed25519SignatureLength)
            return detachedSignature.ToArray();

        var asText = System.Text.Encoding.UTF8.GetString(detachedSignature).Trim();
        if (Base64SignatureRegex().IsMatch(asText))
        {
            try
            {
                var decoded = Convert.FromBase64String(asText);
                if (decoded.Length == Ed25519SignatureLength)
                    return decoded;
            }
            catch (FormatException ex)
            {
                throw new ReleaseManifestVerificationException("Signature is not valid base64.", ex);
            }
        }

        throw new ReleaseManifestVerificationException(
            $"Signature must be {Ed25519SignatureLength} raw bytes or base64 encoding thereof.");
    }

    private static bool VerifyEd25519(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        try
        {
            var publicKeySpki = ReleaseManifestTrust.ResolveTrustedPublicKeySpki();
            var rawPublicKey = ReleaseManifestTrust.ExtractRawPublicKey(publicKeySpki);
            return Ed25519.Verify(signature.ToArray(), message.ToArray(), rawPublicKey);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidateManifestShape(ReleaseManifest manifest)
    {
        if (manifest.ManifestVersion < 1)
            throw new ReleaseManifestVerificationException("Unsupported manifestVersion.");
        if (string.IsNullOrWhiteSpace(manifest.Channel))
            throw new ReleaseManifestVerificationException("Manifest channel is required.");
        if (manifest.PublishedUtc == default)
            throw new ReleaseManifestVerificationException("Manifest publishedUtc is required.");
        if (manifest.Artifacts.Count == 0)
            throw new ReleaseManifestVerificationException("Manifest must include at least one artifact.");

        foreach (var (key, entry) in manifest.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ReleaseManifestVerificationException("Artifact key cannot be empty.");
            if (!SemverUtility.TryParse(entry.Version, out _))
                throw new ReleaseManifestVerificationException($"Artifact '{key}' has invalid version.");
            if (!string.IsNullOrWhiteSpace(entry.MinUpgradeFrom) &&
                !SemverUtility.TryParse(entry.MinUpgradeFrom, out _))
                throw new ReleaseManifestVerificationException($"Artifact '{key}' has invalid minUpgradeFrom.");

            if (entry.IsDockerArtifact)
            {
                if (string.IsNullOrWhiteSpace(entry.Image) || string.IsNullOrWhiteSpace(entry.Digest))
                    throw new ReleaseManifestVerificationException($"Docker artifact '{key}' requires image and digest.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Url) || string.IsNullOrWhiteSpace(entry.Sha256))
                throw new ReleaseManifestVerificationException($"File artifact '{key}' requires url and sha256.");
            if (!Sha256HexRegex().IsMatch(entry.Sha256))
                throw new ReleaseManifestVerificationException($"Artifact '{key}' sha256 must be 64 lowercase hex digits.");
            if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps)
                throw new ReleaseManifestVerificationException($"Artifact '{key}' url must be an absolute HTTPS URL.");
        }
    }

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256HexRegex();

    [GeneratedRegex("^[A-Za-z0-9+/]+={0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Base64SignatureRegex();
}
