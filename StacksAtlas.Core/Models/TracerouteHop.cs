using System.Net.NetworkInformation;

namespace StacksAtlas.Core.Models
{
    public class TracerouteHop
    {
        public int HopNumber { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public long RoundTripTime { get; set; }
        public IPStatus Status { get; set; }
        public bool IsTarget { get; set; }
    }
}
