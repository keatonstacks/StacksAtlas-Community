using Serilog.Core;
using Serilog.Events;
using System.Net;
using System.Net.Sockets;
using System.Text;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Scanning;

namespace StacksAtlas.API.Services.Logging;

public class BoundUdpSyslogSink : ILogEventSink, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _host;
    private readonly int _port;
    private readonly string _appName;
    private UdpClient? _udpClient;
    private IPAddress? _boundIp;
    private readonly object _lock = new();

    public BoundUdpSyslogSink(IServiceProvider serviceProvider, string host, int port, string appName)
    {
        _serviceProvider = serviceProvider;
        _host = host;
        _port = port;
        _appName = appName;
    }

    public void Emit(LogEvent logEvent)
    {
        try
        {
            lock (_lock)
            {
                INetworkBindingService? bindingService = null;
                try
                {
                    bindingService = _serviceProvider.GetService(typeof(INetworkBindingService)) as INetworkBindingService;
                }
                catch
                {
                    // Ignore resolution errors if container is not fully built yet during early startup
                }

                var currentIp = bindingService?.GetIpForRole(NetworkRole.AlertsAndSiem);

                // Recreate UDP client if the IP binding changed or wasn't set yet
                if (_udpClient == null || !Equals(_boundIp, currentIp))
                {
                    _udpClient?.Dispose();
                    _boundIp = currentIp;
                    if (_boundIp != null)
                    {
                        _udpClient = new UdpClient(new IPEndPoint(_boundIp, 0));
                    }
                    else
                    {
                        _udpClient = new UdpClient(); // Default routing
                    }
                }

                var message = logEvent.RenderMessage();
                // Format standard Syslog message: <14>TIMESTAMP APPNAME: MESSAGE
                var syslogMsg = $"<14>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {_appName}: {message}";
                var bytes = Encoding.UTF8.GetBytes(syslogMsg);

                _udpClient.Send(bytes, bytes.Length, _host, _port);
            }
        }
        catch
        {
            // Ignore logging errors to prevent application crashes
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _udpClient?.Dispose();
        }
    }
}
