using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;

namespace StacksAtlas.Tests;

[Collection("ExecutionState")]
public class FederatedLifecycleHubSyncTests
{
    private readonly TestClock _clock = new()
    {
        UtcNow = new DateTime(2026, 5, 26, 14, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void TombstoneGate_AllowsRestoreLifecycleSync_FromNodeTelemetry()
    {
        var priorMode = ExecutionState.Mode;
        ExecutionState.Initialize(ExecutionMode.Hub);

        try
        {
            var gate = new DeviceTombstoneGate(
                new NoOpSuppressionRegistry(),
                _clock);

            var nodeId = "node-1";
            var mac = "AABBCCDDEEFF";

            var existing = new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IsPermanentlyRemoved = true,
                IsDeleted = true,
            };

            var incoming = new Device
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                MacAddress = mac,
                IsPermanentlyRemoved = false,
                IsDeleted = false,
            };

            var blocked = gate.ShouldBlockDiscoveryUpsert(existing, incoming, isUserAction: false, out _);
            Assert.False(blocked);
        }
        finally
        {
            ExecutionState.Initialize(priorMode);
        }
    }

    [Fact]
    public void ApplyLifecycleFields_SyncsArchive_WhenGuidsDiffer()
    {
        var hubRow = new Device
        {
            Id = Guid.NewGuid(),
            NodeId = "node-1",
            MacAddress = "112233445566",
            IsDeleted = false,
        };

        var nodeRow = new Device
        {
            Id = Guid.NewGuid(),
            NodeId = "node-1",
            MacAddress = "112233445566",
            IsDeleted = true,
        };

        FederationDeviceIdentity.ApplyLifecycleFields(hubRow, nodeRow);
        Assert.True(hubRow.IsDeleted);
    }

    [Fact]
    public void ReconcileExisting_OnHub_AppliesNodeArchiveState()
    {
        var priorMode = ExecutionState.Mode;
        ExecutionState.Initialize(ExecutionMode.Hub);

        try
        {
            var service = CreateReconciliationService();
            var id = Guid.NewGuid();

            var existing = new Device
            {
                Id = id,
                NodeId = "node-1",
                IpAddress = "10.0.0.10",
                MacAddress = "AABBCCDDEEFF",
                Name = "Camera-1",
                Status = "online",
                IsDeleted = false,
            };

            var incoming = new Device
            {
                Id = id,
                NodeId = "node-1",
                IpAddress = "10.0.0.10",
                MacAddress = "AABBCCDDEEFF",
                Name = "Camera-1",
                Status = "offline",
                IsDeleted = true,
            };

            var result = service.ReconcileExisting(existing, incoming, isUserAction: false);
            Assert.True(result.IsDeleted);
            Assert.False(result.IsPermanentlyRemoved);
        }
        finally
        {
            ExecutionState.Initialize(priorMode);
        }
    }

    [Fact]
    public void ReconcileExisting_OnHub_AppliesNodeRemoveFromFleet_WithRemovedBy()
    {
        var priorMode = ExecutionState.Mode;
        ExecutionState.Initialize(ExecutionMode.Hub);

        try
        {
            var service = CreateReconciliationService();
            var id = Guid.NewGuid();
            var removedAt = _clock.UtcNow.AddMinutes(-5);

            var existing = new Device
            {
                Id = id,
                NodeId = "node-1",
                IpAddress = "10.0.0.11",
                MacAddress = "112233445566",
                Name = "Switch-1",
                IsDeleted = false,
            };

            var incoming = new Device
            {
                Id = id,
                NodeId = "node-1",
                IpAddress = "10.0.0.11",
                MacAddress = "112233445566",
                Name = "Switch-1",
                IsDeleted = true,
                IsPermanentlyRemoved = true,
                RemovedUtc = removedAt,
                RemovedBy = "site-admin",
            };

            var result = service.ReconcileExisting(existing, incoming, isUserAction: false);
            Assert.True(result.IsPermanentlyRemoved);
            Assert.True(result.IsDeleted);
            Assert.Equal("site-admin", result.RemovedBy);
            Assert.Equal(removedAt, result.RemovedUtc);
        }
        finally
        {
            ExecutionState.Initialize(priorMode);
        }
    }

    [Fact]
    public void ReconcileExisting_OnHub_AppliesNodeRestoreFromFleet()
    {
        var priorMode = ExecutionState.Mode;
        ExecutionState.Initialize(ExecutionMode.Hub);

        try
        {
            var service = CreateReconciliationService();
            var id = Guid.NewGuid();

            var existing = new Device
            {
                Id = id,
                NodeId = "node-1",
                IpAddress = "10.0.0.12",
                MacAddress = "FFEEDDCCBBAA",
                IsDeleted = true,
                IsPermanentlyRemoved = true,
                RemovedBy = "admin",
                RediscoveryHitCount = 3,
            };

            var incoming = new Device
            {
                Id = id,
                NodeId = "node-1",
                IpAddress = "10.0.0.12",
                MacAddress = "FFEEDDCCBBAA",
                IsDeleted = false,
                IsPermanentlyRemoved = false,
                Status = "online",
            };

            var result = service.ReconcileExisting(existing, incoming, isUserAction: false);
            Assert.False(result.IsPermanentlyRemoved);
            Assert.False(result.IsDeleted);
            Assert.Null(result.RemovedBy);
            Assert.Equal(0, result.RediscoveryHitCount);
        }
        finally
        {
            ExecutionState.Initialize(priorMode);
        }
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

    private sealed class NoOpSuppressionRegistry : StacksAtlas.Core.Data.IDeviceSuppressionRegistry
    {
        public void Register(string nodeId, string macAddress, Guid deviceId, string? removedBy) { }
        public void Clear(string nodeId, string macAddress) { }
        public bool IsSuppressed(string nodeId, string? macAddress) => false;
    }
}
