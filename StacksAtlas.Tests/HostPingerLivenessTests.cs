using Microsoft.Extensions.Logging.Abstractions;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;
using Xunit;

namespace StacksAtlas.Tests;

public class HostPingerLivenessTests
{
    [Fact]
    public async Task OnDemand_does_not_mark_online_when_icmp_and_ports_fail()
    {
        var network = new StaleArpNetworkService();
        var pinger = CreatePinger(network);

        var result = await pinger.PingAsync("192.168.254.254", CancellationToken.None, onDemand: true);

        Assert.False(result.IsOnline);
        Assert.False(network.ActiveArpProbed);
    }

    [Fact]
    public async Task Discovery_does_not_mark_online_on_arp_alone_when_icmp_fails()
    {
        var network = new StaleArpNetworkService { ActiveArpReturnsMac = "AABBCCDDEEFF" };
        var pinger = CreatePinger(network);

        var result = await pinger.PingAsync("192.168.254.254", CancellationToken.None, onDemand: false);

        Assert.False(result.IsOnline);
    }

    private static HostPinger CreatePinger(StaleArpNetworkService network) =>
        new(
            new NetworkSettingsStore(NullLogger<NetworkSettingsStore>.Instance),
            new IntelligenceEngine(NullLogger<IntelligenceEngine>.Instance),
            new HttpTitleGrabber(NullLogger<HttpTitleGrabber>.Instance),
            network,
            NullLogger<HostPinger>.Instance);

    private sealed class StaleArpNetworkService : INetworkService
    {
        public bool ActiveArpProbed { get; private set; }
        public string? ActiveArpReturnsMac { get; init; }

        public string GetLocalIpAddress() => "192.168.1.10";
        public string GetLocalMacAddress() => "001122334455";
        public bool IsIpSelf(string ip) => false;
        public bool IsIpLocal(string ip) => ip.StartsWith("192.168.", StringComparison.Ordinal);
        public bool IsIpInSubnet(string ipAddress, string cidr) => true;
        public string GetScannerCidr() => "192.168.1.0/24";

        public Task<List<(string Ip, string Mac)>> GetPassiveNeighborsAsync(CancellationToken token) =>
            Task.FromResult(new List<(string, string)> { ("192.168.254.254", "STALEMACADDR") });

        public Task<string?> GetMacAddressAsync(string ipAddress, CancellationToken token, bool activeOnly = false)
        {
            if (activeOnly)
            {
                ActiveArpProbed = true;
                return Task.FromResult(ActiveArpReturnsMac);
            }

            return Task.FromResult<string?>("STALEMACADDR");
        }

        public Task<bool> ProbeArpAsync(string ipAddress, CancellationToken token) => Task.FromResult(false);
    }
}
