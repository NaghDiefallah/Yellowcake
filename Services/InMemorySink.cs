using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Yellowcake.Services;

public class InMemorySink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEventData> _logEvents = new();
    private const int MaxLogEvents = 2000;

    public static InMemorySink Instance { get; } = new();

    public void Emit(LogEvent logEvent)
    {
        var data = new LogEventData
        {
            Timestamp = logEvent.Timestamp.DateTime,
            Level = NormalizeLevel(logEvent.Level),
            Message = logEvent.RenderMessage(),
            Exception = logEvent.Exception?.ToString()
        };

        _logEvents.Enqueue(data);

        while (_logEvents.Count > MaxLogEvents)
        {
            _logEvents.TryDequeue(out _);
        }
    }

    private static string NormalizeLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => "Debug",
            LogEventLevel.Debug => "Debug",
            LogEventLevel.Information => "Information",
            LogEventLevel.Warning => "Warning",
            LogEventLevel.Error => "Error",
            LogEventLevel.Fatal => "Fatal",
            _ => "Information"
        };
    }

    public LogEventData[] GetLogs()
    {
        return _logEvents.ToArray();
    }

    public void Clear()
    {
        _logEvents.Clear();
    }
}

public class LogEventData
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
}