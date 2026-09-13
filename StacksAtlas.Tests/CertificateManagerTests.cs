using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Services.Security;

namespace StacksAtlas.Tests;

public class CertificateManagerTests
{
    [Fact]
    public void GetOrCreateCertificate_ReturnsCertWithPrivateKey()
    {
        var previousPortable = Environment.GetEnvironmentVariable("STACKSATLAS_PORTABLE");
        var previousDataDir = Environment.GetEnvironmentVariable("STACKSATLAS_DATADIR");
        var temp = Path.Combine(Path.GetTempPath(), "stacksatlas-cert-" + Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", "1");
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", temp);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();

            using var cert = new CertificateManager(NullLogger<CertificateManager>.Instance).GetOrCreateCertificate();

            Assert.True(cert.HasPrivateKey);
            Assert.True(File.Exists(Path.Combine(temp, "certs", "StacksAtlas.pfx")));
            Assert.True(File.Exists(Path.Combine(temp, "certs", "StacksAtlas-LocalCA.pfx")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STACKSATLAS_PORTABLE", previousPortable);
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", previousDataDir);
            PortableMode.InitializeFromEnvironment();
            ResetPlatformPathsCache();
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static void ResetPlatformPathsCache()
    {
        var field = typeof(PlatformPaths).GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }
}
