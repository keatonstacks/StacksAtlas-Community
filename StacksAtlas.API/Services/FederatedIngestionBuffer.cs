using System.Threading.Channels;
using StacksAtlas.Core.Models;

namespace StacksAtlas.API.Services;

public sealed record FederatedNodeHeartbeat(string NodeId, DateTime LastSeenUtc, DateTime LastSyncUtc);

/// <summary>
/// A high-performance, thread-safe, lock-free in-memory ingestion buffer
/// for federated device telemetry and remote node diagnostic logs.
/// </summary>
public class FederatedIngestionBuffer
{
    private readonly Channel<Device> _deviceChannel;
    private readonly Channel<FederatedLog> _logChannel;
    private readonly Channel<SystemEvent> _systemEventChannel;
    private readonly Channel<AlertEvent> _alertEventChannel;
    private readonly Channel<AuditEvent> _auditEventChannel;
    private readonly Channel<FederatedNodeHeartbeat> _nodeHeartbeatChannel;

    public volatile bool IsPaused = false;

    public FederatedIngestionBuffer()
    {
        // High-performance channels optimized for multiple concurrent writers (SignalR connections)
        // and a single dedicated background reader.
        _deviceChannel = Channel.CreateUnbounded<Device>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        _logChannel = Channel.CreateUnbounded<FederatedLog>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        _systemEventChannel = Channel.CreateUnbounded<SystemEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        _alertEventChannel = Channel.CreateUnbounded<AlertEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        _auditEventChannel = Channel.CreateUnbounded<AuditEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        _nodeHeartbeatChannel = Channel.CreateUnbounded<FederatedNodeHeartbeat>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
    }

    public ChannelReader<Device> DeviceReader => _deviceChannel.Reader;
    public ChannelReader<FederatedLog> LogReader => _logChannel.Reader;
    public ChannelReader<SystemEvent> SystemEventReader => _systemEventChannel.Reader;
    public ChannelReader<AlertEvent> AlertEventReader => _alertEventChannel.Reader;
    public ChannelReader<AuditEvent> AuditEventReader => _auditEventChannel.Reader;
    public ChannelReader<FederatedNodeHeartbeat> NodeHeartbeatReader => _nodeHeartbeatChannel.Reader;

    /// <summary>
    /// Queues a single device for bulk reconciliation and database upsert.
    /// </summary>
    public void QueueDevice(Device device)
    {
        _deviceChannel.Writer.TryWrite(device);
    }

    /// <summary>
    /// Queues a batch of devices for bulk reconciliation and database upsert.
    /// </summary>
    public void QueueDevices(IEnumerable<Device> devices)
    {
        foreach (var device in devices)
        {
            _deviceChannel.Writer.TryWrite(device);
        }
    }

    /// <summary>
    /// Queues a single remote diagnostic log for bulk database writing.
    /// </summary>
    public void QueueLog(FederatedLog log)
    {
        _logChannel.Writer.TryWrite(log);
    }

    /// <summary>
    /// Queues a batch of remote diagnostic logs for bulk database writing.
    /// </summary>
    public void QueueLogs(IEnumerable<FederatedLog> logs)
    {
        foreach (var log in logs)
        {
            _logChannel.Writer.TryWrite(log);
        }
    }

    /// <summary>
    /// Queues a single system event for bulk database writing.
    /// </summary>
    public void QueueSystemEvent(SystemEvent evt)
    {
        _systemEventChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Queues a batch of system events for bulk database writing.
    /// </summary>
    public void QueueSystemEvents(IEnumerable<SystemEvent> evts)
    {
        foreach (var evt in evts)
        {
            _systemEventChannel.Writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Queues a single alert event for bulk database writing.
    /// </summary>
    public void QueueAlertEvent(AlertEvent evt)
    {
        _alertEventChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Queues a batch of alert events for bulk database writing.
    /// </summary>
    public void QueueAlertEvents(IEnumerable<AlertEvent> evts)
    {
        foreach (var evt in evts)
        {
            _alertEventChannel.Writer.TryWrite(evt);
        }
    }

    public void QueueAuditEvents(IEnumerable<AuditEvent> events)
    {
        foreach (var evt in events)
        {
            _auditEventChannel.Writer.TryWrite(evt);
        }
    }

    public void QueueNodeHeartbeat(string nodeId, DateTime lastSeenUtc, DateTime lastSyncUtc)
    {
        _nodeHeartbeatChannel.Writer.TryWrite(new FederatedNodeHeartbeat(nodeId, lastSeenUtc, lastSyncUtc));
    }

    /// <summary>
    /// Signals to the background workers that no more items will be written.
    /// </summary>
    public void Complete()
    {
        _deviceChannel.Writer.Complete();
        _logChannel.Writer.Complete();
        _systemEventChannel.Writer.Complete();
        _alertEventChannel.Writer.Complete();
        _auditEventChannel.Writer.Complete();
        _nodeHeartbeatChannel.Writer.Complete();
    }
}
