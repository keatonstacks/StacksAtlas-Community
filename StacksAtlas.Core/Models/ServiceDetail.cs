using LiteDB;

namespace StacksAtlas.Core.Models;

public class ServiceDetail
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeviceId { get; set; }

    public int Port { get; set; }
    public string Protocol { get; set; } = "tcp"; // tcp or udp
    public string State { get; set; } = "open"; // open, closed, filtered

    // Nmap Service Detection Fields
    public string? ServiceName { get; set; } // http, ssh, etc.
    public string? Product { get; set; } // Apache httpd, OpenSSH
    public string? Version { get; set; } // 2.4.49, 8.2p1
    public string? ExtraInfo { get; set; } // OS info, device type hints

    public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;
}
