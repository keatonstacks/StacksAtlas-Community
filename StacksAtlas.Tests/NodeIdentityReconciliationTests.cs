using LiteDB;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Federation;
using Xunit;

namespace StacksAtlas.Tests;

public class NodeIdentityReconciliationGovernanceTests
{
    [Fact]
    public void IsPriorNodeOnline_IsTrue_When_ConnectionIdSet()
    {
        var node = new FederatedNode { Id = "a", ConnectionId = "conn-1" };
        Assert.True(NodeIdentityReconciliationGovernance.IsPriorNodeOnline(node));
    }

    [Fact]
    public void MergeRetiredMetadata_PreservesHumanFieldsFromRetired()
    {
        var canonical = new FederatedNode { Id = "new-id", Name = "Node-abc123" };
        var retired = new FederatedNode
        {
            Id = "old-id",
            Name = "Branch Office",
            Client = "Acme",
            Building = "HQ",
            Room = "Lab",
            SyncUsers = true
        };

        NodeIdentityReconciliationGovernance.MergeRetiredMetadata(canonical, retired);

        Assert.Equal("Branch Office", canonical.Name);
        Assert.Equal("Acme", canonical.Client);
        Assert.True(canonical.SyncUsers);
    }
}

public class NodeIdentityReconciliationTests
{
    [Fact]
    public async Task TryReconcileRegistrationAsync_MergesOfflinePriorNodeAndRepointsDevices()
    {
        var hwid = "HWID-RECON-001";
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var seed = new HubDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Nodes.Add(new FederatedNode
            {
                Id = "site-old",
                Name = "Legacy Branch",
                HardwareId = hwid,
                Client = "Acme"
            });
            seed.Devices.Add(new Device
            {
                Id = Guid.NewGuid(),
                NodeId = "site-old",
                MacAddress = "AABBCCDDEEFF",
                IpAddress = "10.0.0.10",
                Name = "Switch-1",
                LastModifiedUtc = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var factory = new TestHubDbContextFactory(options);
        var nodeRepo = new SqliteFederatedNodeRepository(factory, new TestClock());
        var liteDb = new LiteDatabase("Filename=:memory:");
        var alertRepo = new AlertEventRepository(liteDb, NullLogger<AlertEventRepository>.Instance, new TestClock());

        var service = new NodeIdentityReconciliationService(
            nodeRepo,
            factory,
            liteDb,
            alertRepo,
            new TestClock(),
            NullLogger<NodeIdentityReconciliationService>.Instance);

        var result = await service.TryReconcileRegistrationAsync("site-new", hwid, "test");

        Assert.True(result.Success);
        Assert.True(result.Reconciled);
        Assert.Equal("site-old", result.RetiredNodeId);
        Assert.Equal(1, result.DevicesRepointed);
        Assert.Null(nodeRepo.GetById("site-old"));

        await using var verify = factory.CreateDbContext();
        var canonical = await verify.Nodes.FirstOrDefaultAsync(n => n.Id == "site-new");
        Assert.NotNull(canonical);
        Assert.Equal("Legacy Branch", canonical!.Name);
        Assert.Equal("Acme", canonical.Client);

        var devices = verify.Devices.Where(d => d.MacAddress == "AABBCCDDEEFF").ToList();
        Assert.Single(devices);
        Assert.Equal("site-new", devices[0].NodeId);

        await connection.CloseAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task TryReconcileRegistrationAsync_RejectsWhenPriorNodeIsOnline()
    {
        var hwid = "HWID-RECON-002";
        var nodeRepo = new InMemoryFederatedNodeRepository();
        nodeRepo.UpsertNode(new FederatedNode
        {
            Id = "site-live",
            Name = "Live Site",
            HardwareId = hwid,
            ConnectionId = "active-conn"
        });

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase("ReconcileReject_" + Guid.NewGuid())
            .Options;

        var service = new NodeIdentityReconciliationService(
            nodeRepo,
            new TestHubDbContextFactory(options),
            new LiteDatabase("Filename=:memory:"),
            new AlertEventRepository(new LiteDatabase("Filename=:memory:"), NullLogger<AlertEventRepository>.Instance, new TestClock()),
            new TestClock(),
            NullLogger<NodeIdentityReconciliationService>.Instance);

        var result = await service.TryReconcileRegistrationAsync("site-new", hwid, "test");

        Assert.False(result.Success);
        Assert.True(result.RejectedOnlineConflict);
        Assert.Contains("site-live", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DedupeDevicesByMacForNode_KeepsNewestRow()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseInMemoryDatabase("Dedupe_" + Guid.NewGuid())
            .Options;

        using var context = new HubDbContext(options);
        var mac = "112233445566";
        var older = new Device
        {
            Id = Guid.NewGuid(),
            NodeId = "site-a",
            MacAddress = mac,
            Name = "Old",
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1)
        };
        var newer = new Device
        {
            Id = Guid.NewGuid(),
            NodeId = "site-a",
            MacAddress = mac,
            Name = "New",
            LastModifiedUtc = DateTime.UtcNow
        };
        context.Devices.AddRange(older, newer);
        context.SaveChanges();

        var removed = NodeIdentityReconciliationService.DedupeDevicesByMacForNode(context, "site-a");
        context.SaveChanges();

        Assert.Equal(1, removed);
        Assert.Single(context.Devices.ToList());
        Assert.Equal("New", context.Devices.Single().Name);
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime Now => DateTime.UtcNow.ToLocalTime();
        public long UtcNowUnix => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private sealed class TestHubDbContextFactory(DbContextOptions<HubDbContext> options) : IDbContextFactory<HubDbContext>
    {
        public HubDbContext CreateDbContext() => new(options);
    }

    private sealed class InMemoryFederatedNodeRepository : IFederatedNodeRepository
    {
        private readonly Dictionary<string, FederatedNode> _nodes = new(StringComparer.OrdinalIgnoreCase);

        public FederatedNode? GetById(string id) => _nodes.TryGetValue(id, out var n) ? n : null;

        public FederatedNode? GetByHardwareId(string hardwareId) =>
            _nodes.Values.FirstOrDefault(n => n.HardwareId == hardwareId);

        public List<FederatedNode> GetAll() => _nodes.Values.ToList();

        public void UpsertNode(FederatedNode node) => _nodes[node.Id] = node;

        public bool Delete(string id) => _nodes.Remove(id);
    }
}
