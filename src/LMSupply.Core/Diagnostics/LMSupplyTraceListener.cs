using System.Diagnostics;
using System.Text;

namespace LMSupply.Diagnostics;

/// <summary>
/// A <see cref="TraceListener"/> that forwards LMSupply's internal trace lines
/// (e.g. <c>[LocalGenerator.auto]</c>, <c>[GpuDetector]</c>, <c>[GgufModelDownloader]</c>)
/// to a user-supplied delegate.
///
/// LMSupply uses <see cref="Trace.TraceInformation(string)"/>/<see cref="Trace.TraceWarning(string)"/>
/// for its diagnostic output rather than <c>Microsoft.Extensions.Logging</c>, which means
/// <c>Logging__LogLevel__LMSupply=Trace</c> alone does not surface these lines. Attach this
/// listener at startup to bridge them into any <c>ILogger</c>-style sink.
///
/// Usage:
/// <code>
/// LMSupplyTraceListener.Attach((message, severity) =>
///     logger.Log(severity switch
///     {
///         TraceEventType.Warning => LogLevel.Warning,
///         TraceEventType.Error => LogLevel.Error,
///         _ => LogLevel.Information
///     }, message));
/// </code>
/// </summary>
public sealed class LMSupplyTraceListener : TraceListener
{
    private readonly Action<string, TraceEventType> _sink;
    private readonly StringBuilder _buffer = new();

    /// <summary>
    /// Creates a listener that forwards each completed trace line to <paramref name="sink"/>.
    /// </summary>
    public LMSupplyTraceListener(Action<string, TraceEventType> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>
    /// Attaches a forwarding listener to the global <see cref="Trace.Listeners"/> collection
    /// and returns it. Call <see cref="TraceListener.Dispose()"/> (or remove from
    /// <c>Trace.Listeners</c>) to stop forwarding.
    /// </summary>
    public static LMSupplyTraceListener Attach(Action<string, TraceEventType> sink)
    {
        var listener = new LMSupplyTraceListener(sink);
        Trace.Listeners.Add(listener);
        return listener;
    }

    /// <inheritdoc />
    public override void Write(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        _buffer.Append(message);
    }

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        _buffer.Append(message);
        Flush(TraceEventType.Information);
    }

    /// <inheritdoc />
    public override void TraceEvent(
        TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        if (_buffer.Length > 0)
            Flush(TraceEventType.Information);
        _sink(message, eventType);
    }

    /// <inheritdoc />
    public override void TraceEvent(
        TraceEventCache? eventCache, string source, TraceEventType eventType, int id,
        string? format, params object?[]? args)
    {
        var message = (format is null || args is null || args.Length == 0)
            ? format ?? string.Empty
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
        TraceEvent(eventCache, source, eventType, id, message);
    }

    private void Flush(TraceEventType severity)
    {
        if (_buffer.Length == 0)
            return;
        var text = _buffer.ToString();
        _buffer.Clear();
        _sink(text, severity);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Trace.Listeners.Remove(this);
        }
        base.Dispose(disposing);
    }
}
