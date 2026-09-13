using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.Tests;

public class FederatedDeviceLifecycleTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LiteDatabase _liteDb;
    private readonly DeviceRepository _repo;
    private readonly TestClock _clock = new() { UtcNow = new DateTime(2026, 5, 26, 12, 0, 0, DateTimeKind.Utc) };

    public FederatedDeviceLifecycleTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StacksAtlas_Lifecycle_" + Guid.NewGuid().ToString("N") + ".db");
        _liteDb = new LiteDatabase($"Filename={_dbPath};Connection=Shared");
        var suppressions = new LiteDbDeviceSuppressionRegistry(_liteDb, _clock);
        var tombstoneGate = new DeviceTombstoneGate(
            suppressions,
            _clock,
            new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance));
        _repo = new DeviceRepository(
            _liteDb,
            NullLogger<DeviceRepository>.Instance,
            CreateReconciliationService(),
            tombstoneGate,
            _clock);
    }

    public void Dispose()
    {
        _liteDb.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public void UpsertDevice_SkipsArchivedDevice_DuringDiscoverySweep()
    {
        var id = Guid.NewGuid();
        var originalLastSeen = _clock.UtcNow.AddDays(-2);

        _repo.UpsertDevice(new Device
        {
            Id = id,
            IpAddress = "192.168.1.50",
            MacAddress = "AABBCCDDEEFF",
            Status = "online",
            LastSeen = originalLastSeen,
        }, isUserAction: true);

        Assert.True(_repo.Delete(id));

        var archived = _repo.GetById(id);
        Assert.NotNull(archived);
        var frozenLastSeen = archived!.LastSeen;

        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        _repo.UpsertDevice(new Device
        {
            Id = id,
            IpAddress = "192.168.1.50",
            MacAddress = "AABBCCDDEEFF",
            Status = "online",
            LastSeen = _clock.UtcNow,
        }, isUserAction: false);

        var stored = _repo.GetById(id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);
        Assert.Equal(frozenLastSeen, stored.LastSeen);
        Assert.Equal("offline", stored.Status);
    }

    [Fact]
    public void UpsertDevice_AllowsUserAction_OnArchivedDevice()
    {
        var id = Guid.NewGuid();

        _repo.UpsertDevice(new Device
        {
            Id = id,
            IpAddress = "192.168.1.51",
            MacAddress = "112233445566",
            Name = "Switch-A",
            Status = "online",
            LastSeen = _clock.UtcNow,
        }, isUserAction: true);

        Assert.True(_repo.Delete(id));

        _repo.UpsertDevice(new Device
        {
            Id = id,
            IpAddress = "192.168.1.51",
            MacAddress = "112233445566",
            Name = "Switch-A-Renamed",
            Status = "online",
            LastSeen = _clock.UtcNow,
        }, isUserAction: true);

        var stored = _repo.GetById(id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);
        Assert.Equal("Switch-A-Renamed", stored.Name);
    }

    [Fact]
    public void Restore_ClearsArchiveFlag()
    {
        var id = Guid.NewGuid();
        _repo.UpsertDevice(new Device
        {
            Id = id,
            IpAddress = "192.168.1.52",
            MacAddress = "FFEEDDCCBBAA",
            Status = "offline",
            LastSeen = _clock.UtcNow,
        }, isUserAction: true);

        Assert.True(_repo.Delete(id));
        Assert.True(_repo.Restore(id));

        var stored = _repo.GetById(id);
        Assert.NotNull(stored);
        Assert.False(stored!.IsDeleted);
    }

    [Fact]
    public void GetAll_IncludePermanentlyRemoved_ReturnsTombstonesForFederationSync()
    {
        var id = Guid.NewGuid();
        _repo.UpsertDevice(new Device
        {
            Id = id,
            IpAddress = "192.168.1.53",
            MacAddress = "AABBCCDDEE99",
            Status = "online",
            LastSeen = _clock.UtcNow,
        }, isUserAction: true);

        Assert.True(_repo.RemoveFromFleet(id, "node-admin"));

        var activeOnly = _repo.GetAll();
        Assert.DoesNotContain(activeOnly, d => d.Id == id);

        var withTombstones = _repo.GetAll(includeDeleted: true, includePermanentlyRemoved: true);
        var tombstone = withTombstones.FirstOrDefault(d => d.Id == id);
        Assert.NotNull(tombstone);
        Assert.True(tombstone!.IsPermanentlyRemoved);
        Assert.Equal("node-admin", tombstone.RemovedBy);
    }

    private static DeviceReconciliationService CreateReconciliationService() =>
        new(
            new RecogMatchingService(NullLogger<RecogMatchingService>.Instance),
            new IntelligenceEngine(NullLogger<IntelligenceEngine>.Instance),
            NullLogger<DeviceReconciliationService>.Instance,
            new TestClock { UtcNow = DateTime.UtcNow },
            new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance));

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public DateTime Now => UtcNow.ToLocalTime();
    }
}
