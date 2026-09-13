using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.Tests;

[Collection("ExecutionState")]
public class FederatedLifecycleFleetSyncTests : IDisposable
{
    private readonly DbContextOptions<HubDbContext> _options;
    private readonly TestDbContextFactory _factory;
    private readonly TestClock _clock = new()
    {
        UtcNow = new DateTime(2026, 5, 26, 16, 0, 0, DateTimeKind.Utc)
    };

    public FederatedLifecycleFleetSyncTests()
    {
        _options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase("HubLifecycleFleet_" + Guid.NewGuid())
            .Options;
        _factory = new TestDbContextFactory(_options);
    }

    public void Dispose()
    {
        using var context = _factory.CreateDbContext();
        context.Database.EnsureDeleted();
    }

    [Fact]
    public void UpsertDevice_AppliesArchiveToAllScopeCopies_WhenNodeGuidDiffers()
    {
        var priorMode = ExecutionState.Mode;
        ExecutionState.Initialize(ExecutionMode.Hub);

        try
        {
            var nodeId = "site-a";
            var mac = "AABBCCDDEEFF";
            var hubScopeA = Guid.NewGuid();
            var hubScopeB = Guid.NewGuid();

            SeedHubDevice(new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IpAddress = "10.0.0.10",
                Name = "Camera-Vlan10",
                DiscoveryScopeId = hubScopeA.ToString(),
                Status = "online",
                IsDeleted = false,
            });

            SeedHubDevice(new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IpAddress = "10.0.0.10",
                Name = "Camera-Vlan20",
                DiscoveryScopeId = hubScopeB.ToString(),
                Status = "online",
                IsDeleted = false,
            });

            var repo = CreateHubRepo();
            repo.UpsertDevice(new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IpAddress = "10.0.0.10",
                Name = "Camera-Vlan10",
                DiscoveryScopeId = Guid.NewGuid().ToString(),
                Status = "offline",
                IsDeleted = true,
                IsLifecycleGovernancePush = true,
            }, isUserAction: false);

            using var context = _factory.CreateDbContext();
            var rows = context.Devices.Where(d => d.NodeId == nodeId && d.MacAddress == mac).ToList();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.True(r.IsDeleted));
        }
        finally
        {
            ExecutionState.Initialize(priorMode);
        }
    }

    [Fact]
    public void UpsertDevice_AppliesRemoveFromFleetToAllScopeCopies_WithRemovedBy()
    {
        var priorMode = ExecutionState.Mode;
        ExecutionState.Initialize(ExecutionMode.Hub);

        try
        {
            var nodeId = "site-b";
            var mac = "112233445566";
            var removedAt = _clock.UtcNow.AddMinutes(-3);

            SeedHubDevice(new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IpAddress = "10.0.0.20",
                DiscoveryScopeId = "scope-1",
                IsDeleted = false,
            });

            SeedHubDevice(new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IpAddress = "10.0.0.20",
                DiscoveryScopeId = "scope-2",
                IsDeleted = false,
            });

            var repo = CreateHubRepo();
            repo.UpsertDevice(new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IpAddress = "10.0.0.20",
                IsDeleted = true,
                IsPermanentlyRemoved = true,
                RemovedUtc = removedAt,
                RemovedBy = "node-admin",
                IsLifecycleGovernancePush = true,
            }, isUserAction: false);

            using var context = _factory.CreateDbContext();
            var rows = context.Devices.Where(d => d.NodeId == nodeId && d.MacAddress == mac).ToList();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r =>
            {
                Assert.True(r.IsPermanentlyRemoved);
                Assert.True(r.IsDeleted);
                Assert.Equal("node-admin", r.RemovedBy);
            });
        }
        finally
        {
            ExecutionState.Initialize(priorMode);
        }
    }

    private void SeedHubDevice(Device device)
    {
        using var context = _factory.CreateDbContext();
        context.Devices.Add(device);
        context.SaveChanges();
    }

    private SqliteDeviceRepository CreateHubRepo()
    {
        var suppressions = new SqliteDeviceSuppressionRegistry(_factory, _clock);
        var tombstoneGate = new DeviceTombstoneGate(
            suppressions,
            _clock,
            new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance));

        return new SqliteDeviceRepository(
            _factory,
            NullLogger<SqliteDeviceRepository>.Instance,
            CreateReconciliationService(),
            tombstoneGate,
            suppressions,
            _clock);
    }

    private DeviceReconciliationService CreateReconciliationService() =>
        new(
            new RecogMatchingService(NullLogger<RecogMatchingService>.Instance),
            new IntelligenceEngine(NullLogger<IntelligenceEngine>.Instance),
            NullLogger<DeviceReconciliationService>.Instance,
            _clock,
            new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance));

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public DateTime Now => UtcNow.ToLocalTime();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<HubDbContext>
    {
        private readonly DbContextOptions<HubDbContext> _options;

        public TestDbContextFactory(DbContextOptions<HubDbContext> options) => _options = options;

        public HubDbContext CreateDbContext() => new(_options);
    }
}
