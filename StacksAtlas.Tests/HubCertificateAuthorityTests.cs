using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Services.Security;
using Xunit;

namespace StacksAtlas.Tests
{
    [Collection("CertificateAuthorityTests")]
    public class HubCertificateAuthorityTests : IDisposable
    {
        private static readonly string SharedTempDir;
        private readonly string _tempDir;
        private readonly ServiceProvider _serviceProvider;

        static HubCertificateAuthorityTests()
        {
            // Setup a clean temporary directory for testing once per test run
            SharedTempDir = Path.Combine(Path.GetTempPath(), "StacksAtlas_CA_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SharedTempDir);
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", SharedTempDir);

            var field = typeof(StacksAtlas.Core.Helpers.PlatformPaths).GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, null);
            }
            HubCertificateAuthority.ResetStaticCache();
        }

        public HubCertificateAuthorityTests()
        {
            _tempDir = SharedTempDir;
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", _tempDir);

            var field = typeof(StacksAtlas.Core.Helpers.PlatformPaths).GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, null);
            }
            HubCertificateAuthority.ResetStaticCache();

            // Configure services
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<DatabaseSecurityService>();
            services.AddSingleton<EnrollmentTokenManager>();
            
            // Mock IServiceScopeFactory for CA CRL checks
            var scopeMock = new MockServiceScopeFactory();
            services.AddSingleton<IServiceScopeFactory>(scopeMock);

            _serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            _serviceProvider.Dispose();
        }

        [Fact]
        public void RootCA_ShouldBeGeneratedAndSaved()
        {
            var logger = NullLogger<HubCertificateAuthority>.Instance;
            var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            
            // Diagnostic assertions
            var baseDataDir = StacksAtlas.Core.Helpers.PlatformPaths.BaseDataDir;
            Assert.Equal(_tempDir, baseDataDir);

            var ca = new HubCertificateAuthority(logger, scopeFactory);
            var rootCa = ca.GetOrCreateRootCa();

            Assert.NotNull(rootCa);
            Assert.Contains("CN=StacksAtlas Hub Root Certificate Authority", rootCa.Subject);
            
            var pfxPath = Path.Combine(_tempDir, "certs", "HubRootCA.pfx");
            var crtPath = Path.Combine(_tempDir, "certs", "HubRootCA.crt");

            Assert.True(File.Exists(pfxPath), $"PFX file not found at expected path: {pfxPath}. Actual BaseDataDir: {baseDataDir}");
            Assert.True(File.Exists(crtPath), $"CRT file not found at expected path: {crtPath}.");
        }

        [Fact]
        public void CA_ShouldSignClientCertificateSuccessfully()
        {
            var logger = NullLogger<HubCertificateAuthority>.Instance;
            var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var ca = new HubCertificateAuthority(logger, scopeFactory);

            var rootCa = ca.GetOrCreateRootCa();

            // Generate an ECDsa key pair for the Edge Node
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var subjectName = "CN=node-test-12345, O=StacksAtlas, OU=Node";
            var csrRequest = new CertificateRequest(subjectName, ecdsa, HashAlgorithmName.SHA256);
            csrRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            
            var csrDer = csrRequest.CreateSigningRequest();
            
            // Sign the CSR using the Hub CA
            var validity = TimeSpan.FromDays(30);
            var clientCert = ca.SignNodeClientCertificate(csrDer, "node-test-12345", validity);

            Assert.NotNull(clientCert);
            Assert.Equal("CN=StacksAtlas Hub Root Certificate Authority, O=StacksAtlas, OU=Security", clientCert.Issuer);
            
            // Verify custom trust chain
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(rootCa);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            bool isValidChain = chain.Build(clientCert);
            Assert.True(isValidChain);
        }

        [Fact]
        public void TokenManager_ShouldGenerateAndValidateTokens()
        {
            var tokenManager = _serviceProvider.GetRequiredService<EnrollmentTokenManager>();
            var nodeId = "chicago-branch-01";
            var validity = TimeSpan.FromMinutes(10);

            var token = tokenManager.GenerateToken(nodeId, validity);
            Assert.False(string.IsNullOrWhiteSpace(token));

            bool isValid = tokenManager.TryValidateToken(token, out var validatedNodeId);
            Assert.True(isValid);
            Assert.Equal(nodeId, validatedNodeId);
        }

        [Fact]
        public void ProofOfPossession_Handshake_ShouldValidateCryptographically()
        {
            var tokenManager = _serviceProvider.GetRequiredService<EnrollmentTokenManager>();
            var nodeId = "austin-branch-02";
            var validity = TimeSpan.FromMinutes(15);

            // 1. Generate token on Hub
            var tokenBase64 = tokenManager.GenerateToken(nodeId, validity);
            
            // 2. Decode the token on Node side
            var tokenBytes = Convert.FromBase64String(tokenBase64);

            // 3. Node generates ECDsa key pair and signs the token bytes
            using var nodeEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var signatureBytes = nodeEcdsa.SignData(tokenBytes, HashAlgorithmName.SHA256);
            var signatureBase64 = Convert.ToBase64String(signatureBytes);

            // 4. Hub extracts the public key from the CSR (simulating the key extracted on enrollment)
            var subjectName = $"CN={nodeId}, O=StacksAtlas, OU=Node";
            var csrRequest = new CertificateRequest(subjectName, nodeEcdsa, HashAlgorithmName.SHA256);
            var csrDer = csrRequest.CreateSigningRequest();

            var csr = CertificateRequest.LoadSigningRequest(
                csrDer, 
                HashAlgorithmName.SHA256, 
                CertificateRequestLoadOptions.Default);

            using var extractedPublicKey = csr.PublicKey.GetECDsaPublicKey();
            Assert.NotNull(extractedPublicKey);

            // 5. Hub verifies the signature using the extracted public key
            var incomingTokenBytes = Convert.FromBase64String(tokenBase64);
            var incomingSignatureBytes = Convert.FromBase64String(signatureBase64);

            bool isSignatureVerified = extractedPublicKey.VerifyData(
                incomingTokenBytes, 
                incomingSignatureBytes, 
                HashAlgorithmName.SHA256);

            Assert.True(isSignatureVerified);
        }

        private class MockServiceScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
        {
            public IServiceScope CreateScope() => this;
            public IServiceProvider ServiceProvider => this;
            public void Dispose() { }
            public object? GetService(Type serviceType) => null; // Gracefully return null for DbContext resolving
        }
    }
}
