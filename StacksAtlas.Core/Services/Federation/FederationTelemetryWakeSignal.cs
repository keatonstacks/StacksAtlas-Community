using System.Threading.Channels;

namespace StacksAtlas.Core.Services.Federation;

/// <summary>
/// Wakes the Node federation sync worker to drain pending telemetry immediately.
/// </summary>
public sealed class FederationTelemetryWakeSignal
{
    private readonly Channel<bool> _wake = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<bool> Reader => _wake.Reader;

    public void Notify() => _wake.Writer.TryWrite(true);
}
