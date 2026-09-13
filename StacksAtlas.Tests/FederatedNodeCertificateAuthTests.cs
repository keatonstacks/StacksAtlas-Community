using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using StacksAtlas.API.Services;

namespace StacksAtlas.Tests;

public sealed class FederatedNodeCertificateAuthTests
{
    [Fact]
    public void TryGetCommonName_reads_cn_from_subject()
    {
        using var cert = CreateSelfSigned("CN=node-abc-123, O=StacksAtlas");
        Assert.Equal("node-abc-123", FederatedNodeCertificateAuth.TryGetCommonName(cert));
    }

    [Fact]
    public void TryGetCommonName_returns_null_without_cn()
    {
        using var cert = CreateSelfSigned("O=StacksAtlas");
        Assert.Null(FederatedNodeCertificateAuth.TryGetCommonName(cert));
    }

    private static X509Certificate2 CreateSelfSigned(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}
