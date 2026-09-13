using System.Net;
using System.Net.Sockets;
using System.Text;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.API.Services;

/// <summary>
/// Forwards structured admin audit rows to the configured SIEM syslog endpoint (Business).
/// Separate from Serilog engine noise.
/// </summary>
public interface IAuditSyslogForwarder
{
    void Forward(AuditEvent auditEvent);
}

public class AuditSyslogForwarder(
    SystemSettingsStore settingsStore,
    IServiceProvider serviceProvider) : IAuditSyslogForwarder
{
    private readonly SystemSettingsStore _settingsStore = settingsStore;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly object _lock = new();
    private UdpClient? _udpClient;
    private IPAddress? _boundIp;
    private string? _host;
    private int _port;

    public void Forward(AuditEvent auditEvent)
    {
        var settings = _settingsStore.Current;
        if (!settings.SyslogEnabled || string.IsNullOrWhiteSpace(settings.SyslogHost))
            return;

        try
        {
            lock (_lock)
            {
                EnsureClient(settings.SyslogHost, settings.SyslogPort);

                var appName = string.IsNullOrWhiteSpace(settings.SyslogAppName)
                    ? "StacksAtlas"
                    : settings.SyslogAppName;
                var detail = string.IsNullOrWhiteSpace(auditEvent.Detail)
                    ? string.Empty
                    : $" detail=\"{Sanitize(auditEvent.Detail)}\"";
                var resource = string.IsNullOrWhiteSpace(auditEvent.ResourceId)
                    ? auditEvent.ResourceType
                    : $"{auditEvent.ResourceType}/{auditEvent.ResourceId}";

                // Priority 14 = user-level informational (facility 1, severity 6)
                var message =
                    $"<14>{auditEvent.TimestampUtc:yyyy-MM-ddTHH:mm:ssZ} {appName}: AUDIT action={auditEvent.Action} " +
                    $"actor={Sanitize(auditEvent.ActorUsername)} role={Sanitize(auditEvent.ActorRole)} " +
                    $"outcome={auditEvent.Outcome} resource={Sanitize(resource)} " +
                    $"ip={Sanitize(auditEvent.ClientIp)} node={Sanitize(auditEvent.NodeName)}{detail}";

                var bytes = Encoding.UTF8.GetBytes(message);
                _udpClient!.Send(bytes, bytes.Length, _host, _port);
            }
        }
        catch
        {
            // Never fail the audit write path because SIEM is unreachable.
        }
    }

    private void EnsureClient(string host, int port)
    {
        INetworkBindingService? bindingService = null;
        try
        {
            bindingService = _serviceProvider.GetService(typeof(INetworkBindingService)) as INetworkBindingService;
        }
        catch
        {
            // Container may not be fully built during early startup.
        }

        var currentIp = bindingService?.GetIpForRole(NetworkRole.AlertsAndSiem);
        if (_udpClient != null &&
            string.Equals(_host, host, StringComparison.OrdinalIgnoreCase) &&
            _port == port &&
            Equals(_boundIp, currentIp))
        {
            return;
        }

        _udpClient?.Dispose();
        _host = host;
        _port = port;
        _boundIp = currentIp;
        _udpClient = _boundIp != null
            ? new UdpClient(new IPEndPoint(_boundIp, 0))
            : new UdpClient();
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "-";
        return value.Replace('\r', ' ').Replace('\n', ' ').Replace('"', '\'');
    }
}
