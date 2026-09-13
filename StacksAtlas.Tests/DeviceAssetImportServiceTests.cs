using StacksAtlas.Core.Data;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Devices;

namespace StacksAtlas.Tests;

public class DeviceAssetImportServiceTests
{
    private sealed class InMemoryDeviceRepo : IDeviceRepository
    {
        private readonly List<Device> _devices;

        public InMemoryDeviceRepo(IEnumerable<Device> devices) => _devices = devices.ToList();

        public Device? GetById(Guid id) => _devices.FirstOrDefault(d => d.Id == id);
        public Device? GetByMac(string mac) => _devices.FirstOrDefault(d => d.MacAddress == mac);
        public Device? GetByIp(string ip) => _devices.FirstOrDefault(d => d.IpAddress == ip);
        public void UpsertDevice(Device incoming, bool isUserAction = false)
        {
            var idx = _devices.FindIndex(d => d.Id == incoming.Id);
            if (idx >= 0) _devices[idx] = incoming;
            else _devices.Add(incoming);
        }

        public void UpsertDevices(IEnumerable<Device> devices, bool isUserAction = false)
        {
            foreach (var d in devices) UpsertDevice(d, isUserAction);
        }

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

    [Fact]
    public void ImportCsv_MatchesByMacAndUpdatesAssetTag()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Switch",
            IpAddress = "10.0.0.5",
            MacAddress = "AABBCCDDEEFF",
            NodeId = "node-a",
        };

        var service = new DeviceAssetImportService(
            new InMemoryDeviceRepo([device]),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceAssetImportService>.Instance);

        const string csv = """
            Name,IP Address,MAC Address,Asset Tag
            Switch,10.0.0.5,AA:BB:CC:DD:EE:FF,RACK-42
            """;

        var result = service.ImportCsv(csv);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.NotFound);
        Assert.Equal("RACK-42", device.AssetTag);
        Assert.True(device.IsAssetTagManuallySet);
    }

    [Fact]
    public void ImportCsv_RespectsNodeIdOnHub()
    {
        var deviceA = new Device
        {
            Id = Guid.NewGuid(),
            Name = "A",
            IpAddress = "10.0.0.1",
            MacAddress = "112233445566",
            NodeId = "site-a",
        };
        var deviceB = new Device
        {
            Id = Guid.NewGuid(),
            Name = "B",
            IpAddress = "10.0.0.2",
            MacAddress = "112233445566",
            NodeId = "site-b",
        };

        var service = new DeviceAssetImportService(
            new InMemoryDeviceRepo([deviceA, deviceB]),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceAssetImportService>.Instance);

        const string csv = """
            Node ID,MAC Address,Serial
            site-b,11:22:33:44:55:66,SN-B
            """;

        var result = service.ImportCsv(csv);

        Assert.Equal(1, result.Updated);
        Assert.Equal("SN-B", deviceB.SerialNumber);
        Assert.Null(deviceA.SerialNumber);
    }

    [Fact]
    public void ImportCsv_MatchesByDeviceId()
    {
        var id = Guid.NewGuid();
        var device = new Device
        {
            Id = id,
            Name = "TV",
            IpAddress = "10.0.0.9",
            MacAddress = "AABBCCDDEEFF",
        };

        var service = new DeviceAssetImportService(
            new InMemoryDeviceRepo([device]),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceAssetImportService>.Instance);

        var csv = $"Device ID,Asset Tag\n{id},SITE-9\n";
        var result = service.ImportCsv(csv);

        Assert.Equal(1, result.Updated);
        Assert.Equal("SITE-9", device.AssetTag);
    }

    [Fact]
    public void ImportCsv_SkipsOpenAvcSerialButUpdatesAssetTag()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            IpAddress = "10.0.0.5",
            MacAddress = "AABBCCDDEEFF",
            SerialNumber = "OAVC-SN",
            AssetMetadataSource = AssetMetadataSource.OpenAvc,
            IsSerialNumberManuallySet = false,
        };

        var service = new DeviceAssetImportService(
            new InMemoryDeviceRepo([device]),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceAssetImportService>.Instance);

        const string csv = """
            MAC Address,Serial,Asset Tag
            AA:BB:CC:DD:EE:FF,IMPORT-SN,RACK-1
            """;

        var result = service.ImportCsv(csv);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.SkippedOpenAvc);
        Assert.Equal("OAVC-SN", device.SerialNumber);
        Assert.Equal("RACK-1", device.AssetTag);
    }

    [Fact]
    public void CsvParser_ReadsQuotedFields()
    {
        var line = @"AA:BB:CC:DD:EE:FF,""Tag, with comma""";
        var cells = DeviceAssetCsvParser.ParseLine(line);
        Assert.Equal(2, cells.Count);
        Assert.Equal("Tag, with comma", cells[1]);

        var csv = "MAC Address,Asset Tag\n" + line;
        var parsed = DeviceAssetCsvParser.Parse(csv);

        Assert.Single(parsed.Rows);
        Assert.Equal("Tag, with comma", parsed.Rows[0].AssetTag);
    }
}
