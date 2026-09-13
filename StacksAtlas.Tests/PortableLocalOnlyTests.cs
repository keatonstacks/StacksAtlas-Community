using System.Net;

namespace StacksAtlas.Tests;

public class PortableLocalOnlyTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("192.168.1.10", false)]
    public void LoopbackDetection_MatchesPortableMiddlewareRules(string ip, bool expectedLoopback)
    {
        var address = IPAddress.Parse(ip);
        Assert.Equal(expectedLoopback, IsLoopback(address));
    }

    private static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
            return true;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            return IPAddress.IsLoopback(address.MapToIPv4());

        return false;
    }
}
