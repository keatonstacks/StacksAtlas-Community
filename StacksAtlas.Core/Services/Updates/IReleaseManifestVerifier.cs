using StacksAtlas.Core.Models.Updates;

namespace StacksAtlas.Core.Services.Updates;

public interface IReleaseManifestVerifier
{
    /// <summary>
    /// Verifies detached Ed25519 signature over canonical manifest bytes and deserializes the manifest.
    /// Throws <see cref="ReleaseManifestVerificationException"/> on any trust failure.
    /// </summary>
    ReleaseManifest VerifyAndDeserialize(ReadOnlySpan<byte> manifestJsonUtf8, ReadOnlySpan<byte> detachedSignature);
}

public sealed class ReleaseManifestVerificationException : Exception
{
    public ReleaseManifestVerificationException(string message) : base(message) { }
    public ReleaseManifestVerificationException(string message, Exception inner) : base(message, inner) { }
}
