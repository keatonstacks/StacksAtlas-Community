using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.EntityFrameworkCore;
using StacksAtlas.API.Extensions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Data.Hub;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Settings;
using StacksAtlas.Core.State;
using Xunit;

namespace StacksAtlas.Tests
{
    public class EdgeAlarmsSyncTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LiteDatabase _liteDb;

        public EdgeAlarmsSyncTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "StacksAtlas_EdgeAlarms_Tests_" + Guid.NewGuid().ToString("N") + ".db");
            _liteDb = new LiteDatabase($"Filename={_dbPath};Connection=Shared");
        }

        public void Dispose()
        {
            _liteDb.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch {}
            }
        }

        [Fact]
        public void LiteDbAlertEventRepository_LogAlert_QueuesPendingAlertWhenFederationActive()
        {
            // Arrange
            var clock = new TestClock { UtcNow = DateTime.UtcNow };
            var repo = new AlertEventRepository(_liteDb, new TestLogger<AlertEventRepository>(), clock);
            
            // Activate Federation
            ExecutionState.Initialize(ExecutionMode.Standalone, "http://localhost");

            var alert = new AlertEvent
            {
                Id = Guid.NewGuid().ToString(),
                AlertType = AlertEventType.DeviceDown,
                DeviceIp = "192.168.1.50",
                DeviceName = "Test Switch"
            };

            var device = new Device
            {
                Id = Guid.NewGuid(),
                IpAddress = "192.168.1.50"
            };

            // Act
            repo.LogAlert(alert, device, isDelegation: true);

            // Assert
            var pendingCol = _liteDb.GetCollection<PendingAlert>("pending_alerts");
            var pending = pendingCol.FindAll().ToList();
            Assert.Single(pending);
            Assert.True(pending[0].IsDelegation);
            Assert.Equal("192.168.1.50", pending[0].Device?.IpAddress);
            Assert.Equal(alert.Id, pending[0].Alert.Id);

            // Cleanup ExecutionState
            ExecutionState.Initialize(ExecutionMode.Standalone);
        }

        [Fact]
        public void FederationJson_SystemEvent_ObjectId_RoundTrips()
        {
            var id = ObjectId.NewObjectId();
            var evt = new SystemEvent
            {
                Id = id,
                Type = "DeviceOffline",
                Message = "Device went offline",
                DeviceIp = "192.168.1.10"
            };

            var options = new JsonSerializerOptions();
            FederationJson.Configure(options);

            var json = System.Text.Json.JsonSerializer.Serialize(evt, options);
            var back = System.Text.Json.JsonSerializer.Deserialize<SystemEvent>(json, options);

            Assert.NotNull(back);
            Assert.Equal(id, back!.Id);
        }

        [Fact]
        public void LiteDbEventRepository_AddEvent_QueuesPendingEventWhenFederationActive()
        {
            // Arrange
            var clock = new TestClock { UtcNow = DateTime.UtcNow };
            var mockSecurity = new DatabaseSecurityService(new TestLogger<DatabaseSecurityService>());
            var repo = new EventRepository(_liteDb, mockSecurity, new TestLogger<EventRepository>(), clock);

            // Activate Federation
            ExecutionState.Initialize(ExecutionMode.Standalone, "http://localhost");

            var systemEvent = new SystemEvent
            {
                Id = ObjectId.NewObjectId(),
                Type = "DeviceOnline",
                Message = "Device online event",
                DeviceIp = "192.168.1.55"
            };

            // Act
            repo.AddEvent(systemEvent);

            // Assert
            var pendingCol = _liteDb.GetCollection<PendingEvent>("pending_events");
            var pending = pendingCol.FindAll().ToList();
            Assert.Single(pending);
            Assert.Equal("192.168.1.55", pending[0].Event.DeviceIp);
            Assert.Equal(systemEvent.Id, pending[0].Event.Id);

            // Cleanup ExecutionState
            ExecutionState.Initialize(ExecutionMode.Standalone);
        }

        [Fact]
        public async Task SqliteRepositories_PersistAndRetrieveDataSuccessfully()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<HubDbContext>()
                .UseInMemoryDatabase(databaseName: "HubDbContext_" + Guid.NewGuid().ToString())
                .Options;

            var factory = new TestDbContextFactory(options);
            var clock = new TestClock { UtcNow = DateTime.UtcNow };

            var alertRepo = new SqliteAlertEventRepository(factory, clock);
            var eventRepo = new SqliteEventRepository(factory, clock);

            var alert = new AlertEvent
            {
                Id = "alert-123",
                AlertType = AlertEventType.DeviceDown,
                DeviceIp = "10.0.0.5",
                DeviceName = "Router",
                SentToEmails = new List<string> { "admin@stacksatlas.local" },
                SentToWebhooks = new List<string> { "Slack" }
            };

            var systemEvent = new SystemEvent
            {
                Id = ObjectId.NewObjectId(),
                Type = "NewDevice",
                Message = "New device discovered",
                DeviceIp = "10.0.0.5"
            };

            // Act
            alertRepo.LogAlert(alert);
            eventRepo.AddEvent(systemEvent);

            // Assert
            var recentAlerts = alertRepo.GetRecentAlerts().ToList();
            var recentEvents = eventRepo.GetRecent().ToList();

            Assert.Single(recentAlerts);
            Assert.Equal("alert-123", recentAlerts[0].Id);
            Assert.Equal("admin@stacksatlas.local", recentAlerts[0].SentToEmails[0]);
            Assert.Equal("Slack", recentAlerts[0].SentToWebhooks[0]);

            Assert.Single(recentEvents);
            Assert.Equal(systemEvent.Id, recentEvents[0].Id);
        }

        [Fact]
        public void SqliteRepositories_ShouldPersistAndRetrieveProvenanceMetadata()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<HubDbContext>()
                .UseInMemoryDatabase(databaseName: "HubDbContext_Provenance_" + Guid.NewGuid().ToString())
                .Options;

            var factory = new TestDbContextFactory(options);
            var clock = new TestClock { UtcNow = DateTime.UtcNow };

            var alertRepo = new SqliteAlertEventRepository(factory, clock);
            var eventRepo = new SqliteEventRepository(factory, clock);

            var alert = new AlertEvent
            {
                Id = "alert-prov-1",
                AlertType = AlertEventType.DeviceDown,
                DeviceIp = "10.0.0.9",
                DeviceName = "Switch",
                NodeId = "site-a",
                NodeName = "Site A",
                Client = "Keaton",
                Building = "Main",
                Room = "Rack-1"
            };

            var systemEvent = new SystemEvent
            {
                Id = ObjectId.NewObjectId(),
                Type = "NewDevice",
                Message = "New device discovered",
                DeviceIp = "10.0.0.9",
                NodeId = "site-a",
                NodeName = "Site A",
                Client = "Keaton",
                Building = "Main",
                Room = "Rack-1"
            };

            // Act
            alertRepo.LogAlert(alert);
            eventRepo.AddEvent(systemEvent);

            // Assert
            var recentAlerts = alertRepo.GetRecentAlerts(50, new[] { "site-a" }).ToList();
            var recentEvents = eventRepo.GetRecent(50, new[] { "site-a" }).ToList();

            Assert.Single(recentAlerts);
            Assert.Equal("Site A", recentAlerts[0].NodeName);
            Assert.Equal("Keaton", recentAlerts[0].Client);
            Assert.Equal("Main", recentAlerts[0].Building);
            Assert.Equal("Rack-1", recentAlerts[0].Room);

            Assert.Single(recentEvents);
            Assert.Equal("Site A", recentEvents[0].NodeName);
            Assert.Equal("Keaton", recentEvents[0].Client);
            Assert.Equal("Main", recentEvents[0].Building);
            Assert.Equal("Rack-1", recentEvents[0].Room);
        }

        [Fact]
        public void DeviceReconciliation_ShouldInitializeFirstDiscoveredUtcCorrectly()
        {
            // Arrange
            var clock = new TestClock { UtcNow = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc) };
            var service = new DeviceReconciliationService(
                new RecogMatchingService(new TestLogger<RecogMatchingService>()),
                new IntelligenceEngine(new TestLogger<IntelligenceEngine>()),
                new TestLogger<DeviceReconciliationService>(),
                clock,
                new FederationSettingsStore(new TestLogger<FederationSettingsStore>())
            );

            var device = new Device
            {
                Id = Guid.NewGuid(),
                IpAddress = "192.168.1.100",
                FirstDiscoveredUtc = DateTime.MinValue,
                FirstSeen = DateTime.MinValue
            };

            // Act
            var prepared = service.PrepareNewDevice(device);

            // Assert
            Assert.Equal(clock.UtcNow, prepared.FirstDiscoveredUtc);
            Assert.Equal(clock.UtcNow, prepared.FirstSeen);
        }

        [Fact]
        public void DeviceReconciliation_ShouldPreserveEarliestFirstDiscoveredUtc()
        {
            // Arrange
            var clock = new TestClock { UtcNow = new DateTime(2026, 5, 24, 13, 0, 0, DateTimeKind.Utc) };
            var service = new DeviceReconciliationService(
                new RecogMatchingService(new TestLogger<RecogMatchingService>()),
                new IntelligenceEngine(new TestLogger<IntelligenceEngine>()),
                new TestLogger<DeviceReconciliationService>(),
                clock,
                new FederationSettingsStore(new TestLogger<FederationSettingsStore>())
            );

            var originalDiscovered = new DateTime(2026, 5, 23, 10, 0, 0, DateTimeKind.Utc);
            var existing = new Device
            {
                Id = Guid.NewGuid(),
                IpAddress = "192.168.1.100",
                FirstDiscoveredUtc = originalDiscovered,
                FirstSeen = originalDiscovered
            };

            var laterDiscovered = new DateTime(2026, 5, 24, 11, 0, 0, DateTimeKind.Utc);
            var incoming = new Device
            {
                Id = existing.Id,
                IpAddress = "192.168.1.100",
                FirstDiscoveredUtc = laterDiscovered,
                FirstSeen = laterDiscovered
            };

            // Act
            var reconciled = service.ReconcileExisting(existing, incoming, isUserAction: false);

            // Assert
            // The earlier timestamp should be preserved
            Assert.Equal(originalDiscovered, reconciled.FirstDiscoveredUtc);
        }

        private class TestClock : IClock
        {
            public DateTime UtcNow { get; set; }
            public DateTime Now => UtcNow.ToLocalTime();
        }

        private class TestDbContextFactory : IDbContextFactory<HubDbContext>
        {
            private readonly DbContextOptions<HubDbContext> _options;

            public TestDbContextFactory(DbContextOptions<HubDbContext> options)
            {
                _options = options;
            }

            public HubDbContext CreateDbContext()
            {
                return new HubDbContext(_options);
            }
        }

        private class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }
}
