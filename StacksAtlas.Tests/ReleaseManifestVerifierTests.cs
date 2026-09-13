using System.Security.Cryptography;
using System.Text;
using Chaos.NaCl;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Models.Updates;
using StacksAtlas.Core.Services.Updates;
using Xunit;

namespace StacksAtlas.Tests;

public sealed class ReleaseManifestVerifierTests : IDisposable
{
    private readonly string? _previousPublicKeyEnv;

    public ReleaseManifestVerifierTests()
    {
        _previousPublicKeyEnv = Environment.GetEnvironmentVariable("STACKSATLAS_MANIFEST_PUBLIC_KEY_B64");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("STACKSATLAS_MANIFEST_PUBLIC_KEY_B64", _previousPublicKeyEnv);
    }

    [Fact]
    public void VerifyAndDeserialize_AcceptsValidSignedManifest()
    {
        var (publicSpki, privatePkcs8) = GenerateEphemeralEd25519KeyPair();
        Environment.SetEnvironmentVariable("STACKSATLAS_MANIFEST_PUBLIC_KEY_B64", Convert.ToBase64String(publicSpki));

        var manifestJson = """
            {
              "manifestVersion": 1,
              "channel": "stable",
              "publishedUtc": "2026-05-31T00:00:00Z",
              "releaseNotesUrl": "https://stacksatlas.com/changelog",
              "artifacts": {
                "win-x64-msi": {
                  "version": "1.6.0",
                  "url": "https://releases.stacksatlas.com/stable/win-x64-msi",
                  "sha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                  "criticality": "recommended"
                }
              }
            }
            """;

        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var canonical = ReleaseManifestCanonicalJson.ToCanonicalUtf8(manifestBytes);
        var signature = SignEd25519(canonical, privatePkcs8);

        var verifier = new ReleaseManifestVerifier(NullLogger<ReleaseManifestVerifier>.Instance);
        var manifest = verifier.VerifyAndDeserialize(manifestBytes, signature);

        Assert.Equal("stable", manifest.Channel);
        Assert.True(manifest.Artifacts.ContainsKey("win-x64-msi"));
        Assert.Equal("1.6.0", manifest.Artifacts["win-x64-msi"].Version);
    }

    [Fact]
    public void VerifyAndDeserialize_RejectsTamperedManifest()
    {
        var (publicSpki, privatePkcs8) = GenerateEphemeralEd25519KeyPair();
        Environment.SetEnvironmentVariable("STACKSATLAS_MANIFEST_PUBLIC_KEY_B64", Convert.ToBase64String(publicSpki));

        var manifestJson = """
            {
              "manifestVersion": 1,
              "channel": "stable",
              "publishedUtc": "2026-05-31T00:00:00Z",
              "artifacts": {
                "win-x64-msi": {
                  "version": "1.6.0",
                  "url": "https://releases.stacksatlas.com/stable/win-x64-msi",
                  "sha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
                }
              }
            }
            """;

        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var signature = SignEd25519(ReleaseManifestCanonicalJson.ToCanonicalUtf8(manifestBytes), privatePkcs8);

        manifestBytes = Encoding.UTF8.GetBytes(manifestJson.Replace("1.6.0", "9.9.9", StringComparison.Ordinal));

        var verifier = new ReleaseManifestVerifier(NullLogger<ReleaseManifestVerifier>.Instance);
        var ex = Assert.Throws<ReleaseManifestVerificationException>(() =>
            verifier.VerifyAndDeserialize(manifestBytes, signature));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndDeserialize_RejectsInvalidSha256()
    {
        var (publicSpki, privatePkcs8) = GenerateEphemeralEd25519KeyPair();
        Environment.SetEnvironmentVariable("STACKSATLAS_MANIFEST_PUBLIC_KEY_B64", Convert.ToBase64String(publicSpki));

        var manifestJson = """
            {
              "manifestVersion": 1,
              "channel": "stable",
              "publishedUtc": "2026-05-31T00:00:00Z",
              "artifacts": {
                "win-x64-msi": {
                  "version": "1.6.0",
                  "url": "https://releases.stacksatlas.com/stable/win-x64-msi",
                  "sha256": "NOT_A_VALID_HASH"
                }
              }
            }
            """;

        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
        var signature = SignEd25519(ReleaseManifestCanonicalJson.ToCanonicalUtf8(manifestBytes), privatePkcs8);

        var verifier = new ReleaseManifestVerifier(NullLogger<ReleaseManifestVerifier>.Instance);
        var ex = Assert.Throws<ReleaseManifestVerificationException>(() =>
            verifier.VerifyAndDeserialize(manifestBytes, signature));
        Assert.Contains("sha256", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalJson_IsStableRegardlessOfKeyOrder()
    {
        var ordered = """
            {"manifestVersion":1,"channel":"stable","publishedUtc":"2026-05-31T00:00:00Z","artifacts":{"win-x64-msi":{"version":"1.6.0","url":"https://releases.stacksatlas.com/stable/win-x64-msi","sha256":"abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"}}}
            """;
        var shuffled = """
            {
              "artifacts": {
                "win-x64-msi": {
                  "sha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                  "url": "https://releases.stacksatlas.com/stable/win-x64-msi",
                  "version": "1.6.0"
                }
              },
              "publishedUtc": "2026-05-31T00:00:00Z",
              "channel": "stable",
              "manifestVersion": 1
            }
            """;

        var a = ReleaseManifestCanonicalJson.ToCanonicalUtf8(Encoding.UTF8.GetBytes(ordered));
        var b = ReleaseManifestCanonicalJson.ToCanonicalUtf8(Encoding.UTF8.GetBytes(shuffled));
        Assert.Equal(Encoding.UTF8.GetString(a), Encoding.UTF8.GetString(b));
    }

    private static byte[] SignEd25519(ReadOnlySpan<byte> message, byte[] expandedKeyPair)
    {
        return Ed25519.Sign(message.ToArray(), expandedKeyPair);
    }

    private static (byte[] PublicKeySpki, byte[] ExpandedKeyPair) GenerateEphemeralEd25519KeyPair()
    {
        var seed = new byte[32];
        RandomNumberGenerator.Fill(seed);
        var publicKey = Ed25519.PublicKeyFromSeed(seed);
        var expanded = Ed25519.ExpandedPrivateKeyFromSeed(seed);
        var spki = WrapEd25519Spki(publicKey);
        return (spki, expanded);
    }

    private static byte[] WrapEd25519Spki(byte[] rawPublicKey)
    {
        // RFC 8410 SPKI prefix + 32-byte raw key (matches Node crypto export SPKI DER).
        var prefix = new byte[] { 0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00 };
        var spki = new byte[prefix.Length + rawPublicKey.Length];
        prefix.CopyTo(spki, 0);
        rawPublicKey.CopyTo(spki, prefix.Length);
        return spki;
    }
}
