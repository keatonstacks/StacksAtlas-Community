using StacksAtlas.Core.Models;

namespace StacksAtlas.Core.Services.Network
{
    public interface ITracerouteService
    {
        IAsyncEnumerable<TracerouteHop> RunTracerouteAsync(string targetIp, int maxHops = 30, int timeout = 1000);
    }
}
