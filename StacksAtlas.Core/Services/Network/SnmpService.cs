using System.Collections.Concurrent;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.Logging;

namespace StacksAtlas.Core.Services.Network;

public interface ISnmpService
{
    Task<(string? SysName, string? SysDescr)> GetDeviceIdentityAsync(string ipAddress, string community = "public", int timeoutMs = 1000);
}

public class SnmpService : ISnmpService
{
    private readonly ILogger<SnmpService> _logger;
    private static readonly ConcurrentDictionary<string, DateTime> _negativeCache = new();
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromHours(1);

    private readonly StacksAtlas.Core.Abstractions.IClock _clock;

    public SnmpService(ILogger<SnmpService> logger, StacksAtlas.Core.Abstractions.IClock clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public async Task<(string? SysName, string? SysDescr)> GetDeviceIdentityAsync(string ipAddress, string community = "public", int timeoutMs = 1000)
    {
        if (!IPAddress.TryParse(ipAddress, out var address)) return (null, null);

        // 1. Check Negative Cache
        if (_negativeCache.TryGetValue(ipAddress, out var lastAttempt))
        {
            if (_clock.UtcNow - lastAttempt < NegativeCacheDuration)
            {
                return (null, null); // Skip - previously failed
            }
            _negativeCache.TryRemove(ipAddress, out _);
        }

        try
        {
            // OIDs for System Name and System Description
            using var cts = new CancellationTokenSource(timeoutMs);
            var result = await Messenger.GetAsync(
                VersionCode.V2,
                new IPEndPoint(address, 161),
                new OctetString(community),
                [
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.1.5.0")), // sysName
                    new Variable(new ObjectIdentifier(".1.3.6.1.2.1.1.1.0"))  // sysDescr
                ],
                cts.Token
            );

            if (result == null || result.Count < 2) 
            {
                _negativeCache[ipAddress] = _clock.UtcNow;
                return (null, null);
            }

            var sysName = result[0].Data.ToString();
            var sysDescr = result[1].Data.ToString();

            return (sysName, sysDescr);
        }
        catch (Exception ex)
        {
            _logger.LogTrace("SNMP check failed for {IP}: {Message}", ipAddress, ex.Message);
            _negativeCache[ipAddress] = _clock.UtcNow;
            return (null, null);
        }
    }
}
