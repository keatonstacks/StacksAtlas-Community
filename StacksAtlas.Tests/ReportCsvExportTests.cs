using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Abstractions;
using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Reports;
using StacksAtlas.Core.Services.Scanning;

namespace StacksAtlas.Tests;

public class ReportCsvExportTests
{
    [Fact]
    public void GenerateInventoryCsv_UsesFlatHeadersWithoutPreamble()
    {
        var service = CreateReportService([], []);

        var csv = Encoding.UTF8.GetString(service.GenerateInventoryCsv());
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);
        Assert.StartsWith("SiteName,LocationMetadata,ReportingNodeName,DiscoveryInterface,DiscoveryVlanTag,DeviceIP,AssetStatus,FirstDiscoveredUtc,", lines[0]);
        Assert.DoesNotContain("StacksAtlas NETWORK AUDIT", csv);
    }

    [Fact]
    public void GenerateInventoryCsv_DenormalizesSiteMetadataOnEveryRow()
    {
        var devices = new List<Device>
        {
            new()
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                NodeId = "natick-apartment",
                IpAddress = "192.168.1.50",
                Name = "Apple TV",
                Status = "online",
                Client = "NATICK-APARTMENT",
                Building = "Living Room",
                Room = "Media Wall",
                FirstDiscoveredUtc = new DateTime(2026, 5, 23, 12, 4, 0, DateTimeKind.Utc)
            }
        };

        var nodes = new List<FederatedNode>
        {
            new()
            {
                Id = "natick-apartment",
                Name = "Node-Alpha",
                Client = "NATICK-APARTMENT",
                Building = "Living Room",
                Room = "Media Wall"
            }
        };

        var service = CreateReportService(devices, nodes);
        var csv = Encoding.UTF8.GetString(service.GenerateInventoryCsv());
        var dataRow = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];

        Assert.Contains("\"NATICK-APARTMENT\"", dataRow);
        Assert.Contains("\"Living Room / Media Wall\"", dataRow);
        Assert.Contains("\"Node-Alpha\"", dataRow);
        Assert.Contains("192.168.1.50", dataRow);
        Assert.Contains("online", dataRow);
        Assert.Contains("2026-05-23T12:04:00Z", dataRow);
    }

    [Fact]
    public void GenerateInventoryCsv_SortsBySiteLocationAndNodeName()
    {
        var devices = new List<Device>
        {
            CreateDevice("beta-node", "BOSTON-OFFICE", "Server Room", "Rack A", "10.0.10.20", "Node-Beta"),
            CreateDevice("gamma-node", "BOSTON-OFFICE", "Server Room", "Rack B", "10.0.10.15", "Node-Gamma"),
            CreateDevice("alpha-node", "NATICK-APARTMENT", "Office", "Desk", "192.168.1.51", "Node-Alpha")
        };

        var nodes = new List<FederatedNode>
        {
            new() { Id = "alpha-node", Name = "Node-Alpha", Client = "NATICK-APARTMENT", Building = "Office", Room = "Desk" },
            new() { Id = "beta-node", Name = "Node-Beta", Client = "BOSTON-OFFICE", Building = "Server Room", Room = "Rack A" },
            new() { Id = "gamma-node", Name = "Node-Gamma", Client = "BOSTON-OFFICE", Building = "Server Room", Room = "Rack B" }
        };

        var service = CreateReportService(devices, nodes);
        var csv = Encoding.UTF8.GetString(service.GenerateInventoryCsv());
        var dataRows = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        Assert.Equal(3, dataRows.Count);
        Assert.Contains("10.0.10.20", dataRows[0]);
        Assert.Contains("10.0.10.15", dataRows[1]);
        Assert.Contains("192.168.1.51", dataRows[2]);
    }

    [Fact]
    public void GenerateInventoryCsv_IncludesDiscoveryInterfaceColumn()
    {
        var devices = new List<Device>
        {
            new()
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                NodeId = "node-a",
                IpAddress = "192.168.20.10",
                Name = "DSP",
                Status = "online",
                DiscoveryInterfaceName = "eth1",
                DiscoveryInterfaceId = "eth1-id",
                DiscoveryVlanTag = "20"
            }
        };

        var service = CreateReportService(devices, []);
        var csv = Encoding.UTF8.GetString(service.GenerateInventoryCsv());
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("DiscoveryVlanTag", lines[0]);
        Assert.Contains("\"eth1 (eth1-id)\"", lines[1]);
        Assert.Contains("20", lines[1]);
    }

    private static Device CreateDevice(
        string nodeId,
        string client,
        string building,
        string room,
        string ipAddress,
        string name)
    {
        return new Device
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Client = client,
            Building = building,
            Room = room,
            IpAddress = ipAddress,
            Name = name,
            Status = "online",
            FirstDiscoveredUtc = new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private static ReportService CreateReportService(IEnumerable<Device> devices, IEnumerable<FederatedNode> nodes)
    {
        return new ReportService(
            new TestDeviceRepository(devices),
            new TestEventRepository(),
            new TestFederatedNodeRepository(nodes),
            new SecurityAuditService(),
            new TestClock { UtcNow = new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc) },
            NullLogger<ReportService>.Instance);
    }

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public DateTime Now => UtcNow.ToLocalTime();
    }

    private sealed class TestDeviceRepository(IEnumerable<Device> devices) : IDeviceRepository
    {
        private readonly List<Device> _devices = devices.ToList();

        public Device? GetById(Guid id) => _devices.FirstOrDefault(d => d.Id == id);
        public Device? GetByMac(string mac) => _devices.FirstOrDefault(d => d.MacAddress == mac);
        public Device? GetByIp(string ip) => _devices.FirstOrDefault(d => d.IpAddress == ip);
        public void UpsertDevice(Device incoming, bool isUserAction = false) { }
        public void UpsertDevices(IEnumerable<Device> devices, bool isUserAction = false) { }
        public List<Device> GetAll(bool includeDeleted = false, bool includePermanentlyRemoved = false) => _devices;
        public List<Device> GetPaged(int skip, int take, string? search, string? status, bool showArchived, string? nodeId, string? client, string? building, string? room, string[]? vlanTags, out int totalCount, string? attachmentKind = null, string? attachmentPort = null, string? attachmentParentId = null)
        {
            totalCount = _devices.Count;
            return _devices.Skip(skip).Take(take).ToList();
        }
        public List<string> GetDistinctDiscoveryVlanTags(string? nodeId = null) => [];
        public List<Device> GetAttachmentParentCandidates(string? nodeId, Guid? excludeDeviceId = null, int take = 2000) => [];
        public int GetCount() => _devices.Count;
        public bool Delete(Guid id) => false;
        public bool Restore(Guid id) => false;
        public bool HardDelete(Guid id) => false;
        public bool RemoveFromFleet(Guid id, string? removedBy = null, string? reason = null) => false;
        public bool RestoreFromFleet(Guid id) => false;
        public List<Device> GetPermanentlyRemoved() => [];
        public List<Device> GetDeleted() => [];
        public List<DeviceRepository.DeviceSummaryItem> GetSummaryData() => [];
        public bool AcknowledgeSecurityIssue(Guid id, string issue) => false;
        public bool ResetMetrics(Guid id) => false;
    }

    private sealed class TestEventRepository : IEventRepository
    {
        public void AddEvent(SystemEvent evt) { }
        public IEnumerable<SystemEvent> GetRecent(int count = 20, string[]? nodeIds = null) => [];
        public void DeleteOlderThan(DateTime cutoff) { }
        public int ClearAll(string[]? nodeIds = null) => 0;
        public bool DeleteById(string id) => false;
    }

    private sealed class TestFederatedNodeRepository(IEnumerable<FederatedNode> nodes) : IFederatedNodeRepository
    {
        private readonly List<FederatedNode> _nodes = nodes.ToList();

        public FederatedNode? GetById(string id) => _nodes.FirstOrDefault(n => n.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        public FederatedNode? GetByHardwareId(string hardwareId) =>
            _nodes.FirstOrDefault(n => n.HardwareId == hardwareId);
        public List<FederatedNode> GetAll() => _nodes;
        public void UpsertNode(FederatedNode node) { }
        public bool Delete(string id) => false;
    }
}
