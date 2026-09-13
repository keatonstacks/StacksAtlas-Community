using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Federation;

namespace StacksAtlas.Tests;

public class FederationBacklogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LiteDatabase _db;

    public FederationBacklogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StacksAtlas_Backlog_Tests_" + Guid.NewGuid().ToString("N") + ".db");
        _db = new LiteDatabase($"Filename={_dbPath};Connection=Shared");
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void EnsureMigrationComplete_TrimsPendingTelemetryOnce()
    {
        var service = CreateService();
        _db.GetCollection<PendingTelemetry>("pending_telemetry").Insert(new PendingTelemetry
        {
            Devices = [new Device { Id = Guid.NewGuid(), IpAddress = "10.0.0.1", Name = "Test" }]
        });

        service.EnsureMigrationComplete();
        service.EnsureMigrationComplete();

        Assert.Empty(_db.GetCollection<PendingTelemetry>("pending_telemetry").FindAll());
        Assert.True(service.GetState().BacklogMigrationComplete);
        Assert.Equal(DateTime.MinValue, service.GetState().LastSyncUtc);
    }

    [Fact]
    public void TryQueueTelemetryBatch_BlocksWhenCeilingReached()
    {
        var service = CreateService();
        var pending = _db.GetCollection<PendingTelemetry>("pending_telemetry");

        for (var i = 0; i < 1000; i++)
        {
            pending.Insert(new PendingTelemetry
            {
                Devices = Enumerable.Range(0, 50).Select(_ => new Device
                {
                    Id = Guid.NewGuid(),
                    IpAddress = $"10.0.0.{i % 250}",
                    Name = $"Device-{i}"
                }).ToList()
            });
        }

        var queued = service.TryQueueTelemetryBatch([new Device { Id = Guid.NewGuid(), IpAddress = "10.0.0.99", Name = "Overflow" }]);
        Assert.False(queued);
        Assert.True(service.GetState().FullSyncRequired);
    }

    [Fact]
    public void TryQueueLifecycleBatch_QueuesEvenWhenFullSyncRequired()
    {
        var service = CreateService();
        service.RequestFullDeviceSync("test");

        var queued = service.TryQueueLifecycleBatch([
            new Device
            {
                Id = Guid.NewGuid(),
                IpAddress = "10.0.0.50",
                MacAddress = "AABBCCDDEEFF",
                IsDeleted = true,
                IsLifecycleGovernancePush = true,
            }
        ]);

        Assert.True(queued);
        Assert.Single(_db.GetCollection<PendingTelemetry>("pending_telemetry").FindAll());
        Assert.True(service.GetState().FullSyncRequired);
    }

    [Fact]
    public void ShouldCatchUpOnReconnect_WhenDisconnectedBeyondThreshold()
    {
        var clock = new TestClock { UtcNow = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc) };
        var service = new FederationBacklogService(_db, clock, NullLogger<FederationBacklogService>.Instance);

        service.MarkHubDisconnected();
        clock.UtcNow = clock.UtcNow.AddMinutes(20);

        Assert.True(service.ShouldCatchUpOnReconnect());
    }

    [Fact]
    public void PrepareDeviceSnapshotOnReconnect_ClearsFullSyncRequired()
    {
        var service = CreateService();
        service.RequestFullDeviceSync("test");
        Assert.True(service.GetState().FullSyncRequired);

        service.PrepareDeviceSnapshotOnReconnect();

        Assert.False(service.GetState().FullSyncRequired);
        Assert.Equal(DateTime.MinValue, service.GetState().LastSyncUtc);
    }

    [Fact]
    public void GetAnomaliesForBackfill_FiltersWarningAndCriticalOnly()
    {
        var service = CreateService();
        var events = _db.GetCollection<SystemEvent>("events");
        events.Insert(new SystemEvent { Type = "Performance", Severity = "Info", Message = "noise", Timestamp = DateTime.UtcNow });
        events.Insert(new SystemEvent { Type = "DeviceDown", Severity = "Critical", Message = "switch down", Timestamp = DateTime.UtcNow });

        var backfill = service.GetAnomaliesForBackfill();
        Assert.Single(backfill);
        Assert.Equal("Critical", backfill[0].Severity);
    }

    private FederationBacklogService CreateService()
    {
        return new FederationBacklogService(_db, new TestClock { UtcNow = DateTime.UtcNow }, NullLogger<FederationBacklogService>.Instance);
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public DateTime Now => UtcNow.ToLocalTime();
    }
}
