using System.Text;
using Chaos.NaCl;
using StacksAtlas.Core.Services.Updates;
using Xunit;

namespace StacksAtlas.Tests;

public sealed class Ed25519InteropTests
{
    // Ephemeral test keypair  -  NOT used in production; validates Node <-> Chaos.NaCl interop.
    private const string TestPublicSpkiBase64 = "MCowBQYDK2VwAyEAvNTh+15AOlHbRbaywHlkdP1uCtLAZwIuRog2doB9/Ew=";
    private const string TestPrivatePkcs8Base64 = "MC4CAQAwBQYDK2VwBCIEIFsPhIXqIAyS4NSdoBc1Nu2IiPrYzrADu/9unM1k7oyU";
    private const string NodeSignedTestMessageBase64 = "luy8iqeOi4IFDJWYfWvZ2xlgHXPOjRbeVu7aPeDAOx2r6k/3f3mNH4RZMqqqB5WejOJpuHeMD10YEfvPm+LSBA==";

    [Fact]
    public void NodeDetachedSignature_IsVerifiedByChaosNaCl()
    {
        var message = Encoding.UTF8.GetBytes("test");
        var signature = Convert.FromBase64String(NodeSignedTestMessageBase64);
        var publicSpki = Convert.FromBase64String(TestPublicSpkiBase64);
        var rawPublicKey = ReleaseManifestTrust.ExtractRawPublicKey(publicSpki);

        Assert.True(Ed25519.Verify(signature, message, rawPublicKey));
    }

    [Fact]
    public void ChaosNaClSign_MatchesNodeForSameSeed()
    {
        var message = Encoding.UTF8.GetBytes("test");
        var privatePkcs8 = Convert.FromBase64String(TestPrivatePkcs8Base64);
        var seed = ReleaseManifestTrust.ExtractRawPrivateSeed(privatePkcs8);
        var expanded = Ed25519.ExpandedPrivateKeyFromSeed(seed);
        var signature = Ed25519.Sign(message, expanded);
        var nodeSignature = Convert.FromBase64String(NodeSignedTestMessageBase64);

        Assert.Equal(nodeSignature, signature);
    }
}
