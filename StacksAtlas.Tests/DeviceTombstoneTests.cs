using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.Tests;

public class DeviceTombstoneTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LiteDatabase _liteDb;
    private readonly DeviceRepository _repo;
    private readonly TestClock _clock = new() { UtcNow = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc) };

    public DeviceTombstoneTests()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
        _dbPath = Path.Combine(Path.GetTempPath(), "StacksAtlas_Tombstone_" + Guid.NewGuid().ToString("N") + ".db");
        _liteDb = new LiteDatabase($"Filename={_dbPath};Connection=Shared");
        var suppressions = new LiteDbDeviceSuppressionRegistry(_liteDb, _clock);
        var tombstoneGate = new DeviceTombstoneGate(suppressions, _clock, new FederationSettingsStore(NullLogger<FederationSettingsStore>.Instance));
        _repo = new DeviceRepository(
            _liteDb,
            NullLogger<DeviceRepository>.Instance,
            CreateReconciliationService(),
            tombstoneGate,
            _clock);
    }

    public void Dispose()
    {
        ExecutionState.Initialize(ExecutionMode.Standalone);
        _liteDb.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public void RemoveFromFleet_TombstonesDevice_AndBlocksRediscovery()
    {
        var id = Guid.NewGuid();
        _repo.UpsertDevice(new Device
        {
            Id = id,
            NodeId = "site-a",
            IpAddress = "192.168.1.100",
            MacAddress = "AABBCCDDEE01",
            Name = "Camera-1",
            Status = "online",
            LastSeen = _clock.UtcNow,
        }, isUserAction: true);

        Assert.True(_repo.RemoveFromFleet(id, "admin@test"));

        var tombstone = _repo.GetById(id);
        Assert.NotNull(tombstone);
        Assert.True(tombstone!.IsPermanentlyRemoved);
        Assert.True(tombstone.IsDeleted);
        Assert.Single(_repo.GetPermanentlyRemoved());
        Assert.Empty(_repo.GetPaged(0, 50, null, null, showArchived: false, null, null, null, null, null, out _));
        Assert.Empty(_repo.GetPaged(0, 50, null, null, showArchived: true, null, null, null, null, null, out _));

        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        _repo.UpsertDevice(new Device
        {
            Id = Guid.NewGuid(),
            NodeId = "site-a",
            IpAddress = "192.168.1.100",
            MacAddress = "AABBCCDDEE01",
            Status = "online",
            LastSeen = _clock.UtcNow,
        }, isUserAction: false);

        var afterScan = _repo.GetById(id);
        Assert.NotNull(afterScan);
        Assert.True(afterScan!.IsPermanentlyRemoved);
        Assert.Equal(1, afterScan.RediscoveryHitCount);
        Assert.Equal("192.168.1.100", afterScan.LastRediscoveryIp);
        Assert.Equal(0, _repo.GetCount());
    }

    [Fact]
    public void RestoreFromFleet_ClearsTombstone_AndAllowsRediscovery()
    {
        var id = Guid.NewGuid();
        _repo.UpsertDevice(new Device
        {
            Id = id,
            NodeId = "site-b",
            IpAddress = "192.168.1.101",
            MacAddress = "AABBCCDDEE02",
            Status = "online",
            LastSeen = _clock.UtcNow,
        }, isUserAction: true);

        Assert.True(_repo.RemoveFromFleet(id, "admin@test"));
        Assert.True(_repo.RestoreFromFleet(id));

        var restored = _repo.GetById(id);
        Assert.NotNull(restored);
        Assert.False(restored!.IsPermanentlyRemoved);
        Assert.False(restored.IsDeleted);
        Assert.Empty(_repo.GetPermanentlyRemoved());

        _repo.UpsertDevice(new Device
        {
            Id = id,
            NodeId = "site-b",
            IpAddress = "192.168.1.101",
            MacAddress = "AABBCCDDEE02",
            Status = "online",
            LastSeen = _clock.UtcNow.AddMinutes(5),
        }, isUserAction: false);

        var updated = _repo.GetById(id);
        Assert.NotNull(updated);
        Assert.Equal("online", updated!.Status);
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
