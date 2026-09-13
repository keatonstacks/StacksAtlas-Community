using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LiteDB;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Services.Federation;
using StacksAtlas.Core.Services.Licensing;
using StacksAtlas.Core.Settings;
using StacksAtlas.API.Controllers;
using StacksAtlas.API.Hubs;
using StacksAtlas.API.Services;
using Xunit;

namespace StacksAtlas.Tests
{
    [Collection("CertificateAuthorityTests")]
    public class NodeIdentityHandshakeTests : IDisposable
    {
        private static readonly string SharedTempDir;
        private readonly string _tempDir;
        private readonly ServiceProvider _serviceProvider;
        private readonly TestClock _clock;
        private readonly TestDeviceRepository _deviceRepo;
        private readonly FederationSettingsStore _settingsStore;
        private readonly TestFederatedNodeRepository _nodeRepo;
        private readonly EnrollmentTokenManager _tokenManager;

        static NodeIdentityHandshakeTests()
        {
            SharedTempDir = Path.Combine(Path.GetTempPath(), "StacksAtlas_Handshake_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SharedTempDir);
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", SharedTempDir);

            var field = typeof(StacksAtlas.Core.Helpers.PlatformPaths).GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, null);
            }
            HubCertificateAuthority.ResetStaticCache();
        }

        public NodeIdentityHandshakeTests()
        {
            _tempDir = Path.Combine(SharedTempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            Environment.SetEnvironmentVariable("STACKSATLAS_DATADIR", _tempDir);

            var field = typeof(StacksAtlas.Core.Helpers.PlatformPaths).GetField("_baseDataDir", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, null);
            }
            HubCertificateAuthority.ResetStaticCache();

            // Clear SignalR active nodes cache
            var activeNodesField = typeof(FederationHub).GetField("ActiveNodes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (activeNodesField != null)
            {
                var dict = activeNodesField.GetValue(null) as System.Collections.Concurrent.ConcurrentDictionary<string, string>;
                dict?.Clear();
            }
            // Reset ExecutionState
            StacksAtlas.Core.State.ExecutionState.Initialize(StacksAtlas.Core.Models.ExecutionMode.Standalone);

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            _clock = new TestClock();
            services.AddSingleton<IClock>(_clock);

            _deviceRepo = new TestDeviceRepository();
            services.AddSingleton<IDeviceRepository>(_deviceRepo);

            _nodeRepo = new TestFederatedNodeRepository();
            services.AddSingleton<IFederatedNodeRepository>(_nodeRepo);

            var configDict = new Dictionary<string, string?>
            {
                { "StacksAtlas:LicenseKey", "" }
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
            services.AddSingleton<IConfiguration>(config);

            var fedSettingsLogger = NullLogger<FederationSettingsStore>.Instance;
            _settingsStore = new FederationSettingsStore(fedSettingsLogger);
            services.AddSingleton(_settingsStore);

            var scopeMock = new MockServiceScopeFactory();
            services.AddSingleton<IServiceScopeFactory>(scopeMock);

            services.AddSingleton<IHubCertificateAuthority, HubCertificateAuthority>();
            services.AddSingleton<DatabaseSecurityService>();
            services.AddSingleton<EnrollmentTokenManager>();

            var httpClient = new HttpClient();
            services.AddSingleton(httpClient);

            var mockHwidProvider = new TestHardwareIdProvider("NodeHWID_12345");
            services.AddSingleton<IHardwareIdProvider>(mockHwidProvider);

            services.AddSingleton<ILicenseService>(new TestLicenseService(LicenseTestFixtures.HubFleetLicense()));

            _serviceProvider = services.BuildServiceProvider();
            _tokenManager = _serviceProvider.GetRequiredService<EnrollmentTokenManager>();
        }

        public void Dispose()
        {
            _serviceProvider.Dispose();
        }

        [Fact]
        public async Task Enroll_ShouldSucceedAndRecordHardwareId()
        {
            var ca = _serviceProvider.GetRequiredService<IHubCertificateAuthority>();
            var licenseService = _serviceProvider.GetRequiredService<ILicenseService>();
            
            var liteDb = new LiteDatabase("Filename=:memory:;shared=true");
            var controller = new FederationController(
                _nodeRepo,
                _deviceRepo,
                _settingsStore,
                new NetworkSettingsStore(NullLogger<NetworkSettingsStore>.Instance),
                new SystemSettingsStore(NullLogger<SystemSettingsStore>.Instance),
                new TestHubContext(),
                new TestFederationIdentitySyncService(),
                new TestFederationSettingsSyncService(),
                liteDb,
                NullLogger<FederationController>.Instance,
                ca,
                _tokenManager,
                licenseService,
                new NoOpTailscaleStatusService(),
                new NoOpTailscaleReachabilityService(),
                new StacksAtlas.Core.Services.Network.NoOpNetworkInterfaceService(),
                new LicenseTestFixtures.StubNodeFleetTelemetryPurgeService(),
                new NoOpNodeIdentityReconciliationService(),
                new StacksAtlas.Core.Database.AlertEventRepository(liteDb, NullLogger<StacksAtlas.Core.Database.AlertEventRepository>.Instance, _clock),
                _clock,
                new LicenseTestFixtures.StubAuditService(),
                new StacksAtlas.API.Services.FederationNodePulseService(
                    _settingsStore,
                    NullLogger<StacksAtlas.API.Services.FederationNodePulseService>.Instance)
            );

            // ExecuteState configuration
            StacksAtlas.Core.State.ExecutionState.Initialize(StacksAtlas.Core.Models.ExecutionMode.Hub);

            var nodeId = "branch-node-01";
            var token = _tokenManager.GenerateToken(nodeId, TimeSpan.FromMinutes(10));
            
            // Generate a CSR on Node
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var subjectName = $"CN={nodeId}";
            var csrRequest = new CertificateRequest(subjectName, ecdsa, HashAlgorithmName.SHA256);
            var csrDer = csrRequest.CreateSigningRequest();
            var csrPem = $"-----BEGIN CERTIFICATE REQUEST-----\n{Convert.ToBase64String(csrDer, Base64FormattingOptions.InsertLineBreaks)}\n-----END CERTIFICATE REQUEST-----";

            var tokenBytes = Convert.FromBase64String(token);
            var signatureBytes = ecdsa.SignData(tokenBytes, HashAlgorithmName.SHA256);
            var signatureBase64 = Convert.ToBase64String(signatureBytes);

            var enrollRequestDto = new NodeEnrollmentClient.EnrollRequestDto
            {
                NodeId = nodeId,
                Token = token,
                Csr = csrPem,
                Signature = signatureBase64,
                FriendlyName = "Natick Apartment",
                Client = "Keaton",
                Building = "Main",
                Room = "Living",
                HardwareId = "HWID_BRANCH_01"
            };

            var result = await controller.Enroll(enrollRequestDto);
            var okResult = Assert.IsType<OkObjectResult>(result);
            var responseDto = Assert.IsType<NodeEnrollmentClient.EnrollResponseDto>(okResult.Value);

            Assert.NotNull(responseDto.ClientCertificateBase64);
            Assert.NotNull(responseDto.HubRootCertificateBase64);

            var savedNode = _nodeRepo.GetById(nodeId);
            Assert.NotNull(savedNode);
            Assert.Equal("HWID_BRANCH_01", savedNode.HardwareId);
            Assert.Equal("Natick Apartment", savedNode.Name);
            Assert.Equal("Keaton", savedNode.Client);
        }

        [Fact]
        public async Task Enroll_WithMismatchedHardwareId_ShouldReject()
        {
            var ca = _serviceProvider.GetRequiredService<IHubCertificateAuthority>();
            var licenseService = _serviceProvider.GetRequiredService<ILicenseService>();

            // Setup existing node with HWID
            var nodeId = "branch-node-02";
            _nodeRepo.UpsertNode(new FederatedNode
            {
                Id = nodeId,
                Name = "Existing Node",
                HardwareId = "ORIGINAL_HWID_999"
            });
            
            var liteDb = new LiteDatabase("Filename=:memory:;shared=true");
            var controller = new FederationController(
                _nodeRepo,
                _deviceRepo,
                _settingsStore,
                new NetworkSettingsStore(NullLogger<NetworkSettingsStore>.Instance),
                new SystemSettingsStore(NullLogger<SystemSettingsStore>.Instance),
                new TestHubContext(),
                new TestFederationIdentitySyncService(),
                new TestFederationSettingsSyncService(),
                liteDb,
                NullLogger<FederationController>.Instance,
                ca,
                _tokenManager,
                licenseService,
                new NoOpTailscaleStatusService(),
                new NoOpTailscaleReachabilityService(),
                new StacksAtlas.Core.Services.Network.NoOpNetworkInterfaceService(),
                new LicenseTestFixtures.StubNodeFleetTelemetryPurgeService(),
                new NoOpNodeIdentityReconciliationService(),
                new StacksAtlas.Core.Database.AlertEventRepository(liteDb, NullLogger<StacksAtlas.Core.Database.AlertEventRepository>.Instance, _clock),
                _clock,
                new LicenseTestFixtures.StubAuditService(),
                new StacksAtlas.API.Services.FederationNodePulseService(
                    _settingsStore,
                    NullLogger<StacksAtlas.API.Services.FederationNodePulseService>.Instance)
            );

            StacksAtlas.Core.State.ExecutionState.Initialize(StacksAtlas.Core.Models.ExecutionMode.Hub);

            var token = _tokenManager.GenerateToken(nodeId, TimeSpan.FromMinutes(10));
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var subjectName = $"CN={nodeId}";
            var csrRequest = new CertificateRequest(subjectName, ecdsa, HashAlgorithmName.SHA256);
            var csrDer = csrRequest.CreateSigningRequest();
            var csrPem = $"-----BEGIN CERTIFICATE REQUEST-----\n{Convert.ToBase64String(csrDer, Base64FormattingOptions.InsertLineBreaks)}\n-----END CERTIFICATE REQUEST-----";

            var tokenBytes = Convert.FromBase64String(token);
            var signatureBytes = ecdsa.SignData(tokenBytes, HashAlgorithmName.SHA256);
            var signatureBase64 = Convert.ToBase64String(signatureBytes);

            var enrollRequestDto = new NodeEnrollmentClient.EnrollRequestDto
            {
                NodeId = nodeId,
                Token = token,
                Csr = csrPem,
                Signature = signatureBase64,
                FriendlyName = "Mismatched Node",
                HardwareId = "DIFFERENT_HWID_777" // Mismatch!
            };

            var result = await controller.Enroll(enrollRequestDto);
            var badResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("hardware identifier mismatch", badResult.Value?.ToString());
        }

        [Fact]
        public async Task AuthenticateNode_ShouldValidateHardwareIdAndSyncLocationMetadata()
        {
            var ca = _serviceProvider.GetRequiredService<IHubCertificateAuthority>();
            var alertRepo = new AlertEventRepository(new LiteDatabase("Filename=:memory:;shared=true"), NullLogger<AlertEventRepository>.Instance, _clock);

            var fedSettings = _settingsStore.Current;
            fedSettings.FederationToken = "test-federation-token";
            _settingsStore.Save(fedSettings);

            var nodeId = "branch-node-03";
            _nodeRepo.UpsertNode(new FederatedNode
            {
                Id = nodeId,
                Name = "Branch Node 3",
                HardwareId = "HWID_BRANCH_03",
                Client = "Authoritative Client",
                Building = "Authoritative Building",
                Room = "Authoritative Room"
            });

            var hub = new FederationHub(
                NullLogger<FederationHub>.Instance,
                _deviceRepo,
                _nodeRepo,
                _settingsStore,
                new TestFederationIdentitySyncService(),
                new TestFederationSettingsSyncService(),
                new TestHubContext(),
                new LiteDatabase("Filename=:memory:;shared=true"),
                alertRepo,
                new TestLicenseService(LicenseTestFixtures.HubFleetLicense(nodeLimit: 10)),
                ca,
                new NoOpNodeIdentityReconciliationService()
            );

            hub.Groups = new TestGroupManager();
            hub.Context = new TestHubCallerContext("Conn_Branch_3");

            // 1. Authenticate with correct HWID
            var request = new FederationRegistrationRequest
            {
                NodeId = nodeId,
                HardwareId = "HWID_BRANCH_03",
                FederationToken = "test-federation-token",
                Client = "Old Client Value",
                Building = "Old Building Value",
                Room = "Old Room Value"
            };

            var response = await hub.AuthenticateNode(request);
            Assert.True(response.Success);
            Assert.Equal("Authoritative Client", response.Client);
            Assert.Equal("Authoritative Building", response.Building);
            Assert.Equal("Authoritative Room", response.Room);

            // 2. Authenticate with mismatched HWID
            var requestMismatched = new FederationRegistrationRequest
            {
                NodeId = nodeId,
                HardwareId = "SPURIOUS_HWID_999",
                FederationToken = "test-federation-token"
            };

            var responseMismatched = await hub.AuthenticateNode(requestMismatched);
            Assert.False(responseMismatched.Success);
            Assert.Contains("hardware identifier mismatch", responseMismatched.Message);
        }

        [Fact]
        public async Task AuthenticateNode_ShouldAdoptHardwareIdOnFirstConnectForLegacyNode()
        {
            var ca = _serviceProvider.GetRequiredService<IHubCertificateAuthority>();
            var alertRepo = new AlertEventRepository(new LiteDatabase("Filename=:memory:;shared=true"), NullLogger<AlertEventRepository>.Instance, _clock);

            var fedSettings = _settingsStore.Current;
            fedSettings.FederationToken = "test-federation-token";
            _settingsStore.Save(fedSettings);

            var nodeId = "legacy-node-04";
            _nodeRepo.UpsertNode(new FederatedNode
            {
                Id = nodeId,
                Name = "Legacy Node",
                HardwareId = null // Legacy node has no HWID stored
            });

            var hub = new FederationHub(
                NullLogger<FederationHub>.Instance,
                _deviceRepo,
                _nodeRepo,
                _settingsStore,
                new TestFederationIdentitySyncService(),
                new TestFederationSettingsSyncService(),
                new TestHubContext(),
                new LiteDatabase("Filename=:memory:;shared=true"),
                alertRepo,
                new TestLicenseService(LicenseTestFixtures.HubFleetLicense(nodeLimit: 10)),
                ca,
                new NoOpNodeIdentityReconciliationService()
            );

            hub.Groups = new TestGroupManager();
            hub.Context = new TestHubCallerContext("Conn_Legacy_4");

            var request = new FederationRegistrationRequest
            {
                NodeId = nodeId,
                HardwareId = "ADOPTED_HWID_888",
                FederationToken = "test-federation-token"
            };

            var response = await hub.AuthenticateNode(request);
            Assert.True(response.Success, response.Message ?? "AuthenticateNode returned Success=false with no message.");

            // Verify it was saved to the DB
            var savedNode = _nodeRepo.GetById(nodeId);
            Assert.NotNull(savedNode);
            Assert.Equal("ADOPTED_HWID_888", savedNode.HardwareId);
        }

        // --- STUBS AND MOCKS ---

        private class TestClock : IClock
        {
            private DateTime _utcNow = DateTime.UtcNow;
            public DateTime UtcNow => _utcNow;
            public DateTime Now => _utcNow.ToLocalTime();
            public long UtcNowUnix => new DateTimeOffset(_utcNow).ToUnixTimeSeconds();

            public void SetTime(DateTime time) => _utcNow = time;
            public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
        }

        private class TestHardwareIdProvider : IHardwareIdProvider
        {
            private readonly string _hwid;
            public TestHardwareIdProvider(string hwid) => _hwid = hwid;
            public string GetHardwareId() => _hwid;
        }

        private class TestDeviceRepository : IDeviceRepository
        {
            public int GetCount() => 0;
            public Device? GetById(Guid id) => null;
            public Device? GetByMac(string mac) => null;
            public Device? GetByIp(string ip) => null;
            public void UpsertDevice(Device incoming, bool isUserAction = false) { }
            public void UpsertDevices(IEnumerable<Device> devices, bool isUserAction = false) { }
            public List<Device> GetAll(bool includeDeleted = false, bool includePermanentlyRemoved = false) => new();
            public List<Device> GetPaged(int skip, int take, string? search, string? status, bool showArchived, string? nodeId, string? client, string? building, string? room, string[]? vlanTags, out int totalCount, string? attachmentKind = null, string? attachmentPort = null, string? attachmentParentId = null)
            {
                totalCount = 0;
                return new List<Device>();
            }
            public List<string> GetDistinctDiscoveryVlanTags(string? nodeId = null) => [];
            public List<Device> GetAttachmentParentCandidates(string? nodeId, Guid? excludeDeviceId = null, int take = 2000) => [];
            public bool Delete(Guid id) => false;
            public bool Restore(Guid id) => false;
            public bool HardDelete(Guid id) => false;
            public bool RemoveFromFleet(Guid id, string? removedBy = null, string? reason = null) => false;
            public bool RestoreFromFleet(Guid id) => false;
            public List<Device> GetPermanentlyRemoved() => [];
            public List<Device> GetDeleted() => new();
            public List<DeviceRepository.DeviceSummaryItem> GetSummaryData() => new();
            public bool AcknowledgeSecurityIssue(Guid id, string issue) => false;
            public bool ResetMetrics(Guid id) => false;
        }

        private class TestFederatedNodeRepository : IFederatedNodeRepository
        {
            public Dictionary<string, FederatedNode> Nodes { get; } = new();
            public FederatedNode? GetById(string id) => Nodes.TryGetValue(id, out var node) ? node : null;
            public FederatedNode? GetByHardwareId(string hardwareId) =>
                Nodes.Values.FirstOrDefault(n => n.HardwareId == hardwareId);
            public List<FederatedNode> GetAll() => Nodes.Values.ToList();
            public void UpsertNode(FederatedNode node) => Nodes[node.Id] = node;
            public bool Delete(string id) => Nodes.Remove(id);
        }

        private class MockServiceScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
        {
            public IServiceScope CreateScope() => this;
            public IServiceProvider ServiceProvider => this;
            public void Dispose() { }
            public object? GetService(Type serviceType) => null;
        }

        private class TestLicenseService : ILicenseService
        {
            public event Action<LicenseStatus>? OnLicenseStatusChanged { add { } remove { } }
            private readonly LicenseStatus _status;
            public TestLicenseService(LicenseStatus status) => _status = status;
            public Task<LicenseStatus> GetCurrentStatusAsync() => Task.FromResult(_status);
            public Task<LicenseStatus> ActivateAsync(string licenseKey) => Task.FromResult(_status);
            public void ApplyInheritedLicense(LicenseTier tier, string payloadJson, string signatureBase64) { }
            public void ClearInheritedLicense() { }
        }

        private class TestFederationIdentitySyncService : IFederationIdentitySyncService
        {
            public Task BroadcastIdentityStateAsync() => Task.CompletedTask;
            public Task PushIdentityStateToNodeAsync(string connectionId) => Task.CompletedTask;
        }

        private class TestFederationSettingsSyncService : IFederationSettingsSyncService
        {
            public Task BroadcastSettingsStateAsync() => Task.CompletedTask;
            public Task PushSettingsStateToNodeAsync(string connectionId) => Task.CompletedTask;
        }

        private class TestHubContext : IHubContext<FederationHub>
        {
            public IHubClients Clients => new TestHubClients();
            public IGroupManager Groups => new TestGroupManager();
        }

        private class TestHubClients : IHubClients
        {
            public IClientProxy All => new TestClientProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new TestClientProxy();
            public IClientProxy Client(string connectionId) => new TestClientProxy();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new TestClientProxy();
            public IClientProxy Group(string groupName) => new TestClientProxy();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new TestClientProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new TestClientProxy();
            public IClientProxy User(string userId) => new TestClientProxy();
            public IClientProxy Users(IReadOnlyList<string> userIds) => new TestClientProxy();
        }

        private class TestClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class TestGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class TestHubCallerContext : HubCallerContext
        {
            private readonly string _connectionId;
            public TestHubCallerContext(string connectionId) => _connectionId = connectionId;
            public override string ConnectionId => _connectionId;
            public override string? UserIdentifier => null;
            public override System.Security.Claims.ClaimsPrincipal? User => null;
            public override IDictionary<object, object?> Items => new Dictionary<object, object?>();
            public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features => new Microsoft.AspNetCore.Http.Features.FeatureCollection();
            public override CancellationToken ConnectionAborted => CancellationToken.None;
            public override void Abort() { }
        }
    }

    internal sealed class NoOpTailscaleStatusService : ITailscaleStatusService
    {
        public Task<TailscaleStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TailscaleStatus());
    }

    internal sealed class NoOpTailscaleReachabilityService : ITailscaleReachabilityService
    {
        public Task<TailscaleReachabilityResult> TestHubReachabilityAsync(
            TailscaleReachabilityProbeRequest? draft = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TailscaleReachabilityResult());
    }
}
