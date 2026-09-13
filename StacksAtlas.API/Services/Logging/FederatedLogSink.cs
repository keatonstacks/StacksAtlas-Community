using System;
using Serilog.Core;
using Serilog.Events;
using StacksAtlas.Core.Models;
using StacksAtlas.Core.Services.Logging;
using StacksAtlas.Core.Settings;

namespace StacksAtlas.API.Services.Logging;

/// <summary>
/// A Serilog ILogEventSink that forwards operational logs to the FederatedLogBuffer for Hub ingestion.
/// </summary>
public class FederatedLogSink : ILogEventSink
{
    private readonly FederatedLogBuffer _buffer;
    private readonly IServiceProvider _serviceProvider;
    private SystemSettingsStore? _settingsStore;

    public FederatedLogSink(FederatedLogBuffer buffer, IServiceProvider serviceProvider)
    {
        _buffer = buffer;
        _serviceProvider = serviceProvider;
    }

    public void Emit(LogEvent logEvent)
    {
        if (_settingsStore == null)
        {
            try
            {
                _settingsStore = _serviceProvider.GetService(typeof(SystemSettingsStore)) as SystemSettingsStore;
            }
            catch
            {
                // Ignore resolution errors if container is not fully built yet during early startup
            }
        }

        var isDebugEnabled = _settingsStore?.Current?.IsDebugLoggingEnabled ?? false;
        var minLevel = isDebugEnabled
            ? LogEventLevel.Debug
            : LogEventLevel.Information;

        if (logEvent.Level < minLevel) return;

        var message = logEvent.RenderMessage();
        var exception = logEvent.Exception?.ToString();

        _buffer.Enqueue(new FederatedLog
        {
            Timestamp = logEvent.Timestamp.UtcDateTime,
            LogLevel = logEvent.Level.ToString(),
            Message = message,
            Exception = exception
        });
    }
}
