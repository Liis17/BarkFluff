using System.Globalization;

using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;

namespace BarkFluff.GrpcServer;

/// <summary>Lognara's canonical event fields; extra properties remain structured attributes.</summary>
public sealed class LognaraJsonFormatter : ITextFormatter
{
    private readonly JsonValueFormatter _values = new(typeTagName: null);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        output.Write("{\"timestamp\":");
        WriteString(logEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture), output);
        output.Write(",\"level\":");
        WriteString(logEvent.Level.ToString(), output);
        output.Write(",\"message\":");
        WriteString(logEvent.RenderMessage(CultureInfo.InvariantCulture), output);

        // Read IDs captured at event creation, never Activity.Current on the shipping thread.
        WriteField("trace_id", logEvent.TraceId?.ToHexString() ?? Scalar(logEvent, "TraceId"), output);
        WriteField("span_id", logEvent.SpanId?.ToHexString() ?? Scalar(logEvent, "SpanId"), output);
        WriteField("parent_span_id", Scalar(logEvent, "ParentSpanId"), output);
        WriteField("request_id", Scalar(logEvent, "RequestId"), output);

        output.Write(",\"attributes\":{");
        var separator = "";
        foreach (var property in logEvent.Properties)
        {
            output.Write(separator);
            WriteString(property.Key, output);
            output.Write(':');
            _values.Format(property.Value, output);
            separator = ",";
        }

        if (logEvent.Exception is { } exception)
        {
            output.Write(separator);
            output.Write("\"exception.type\":");
            WriteString(exception.GetType().FullName!, output);
            output.Write(",\"exception.stacktrace\":");
            WriteString(exception.ToString(), output);
        }
        output.Write("}}");
        output.WriteLine();
    }

    private void WriteField(string name, string? value, TextWriter output)
    {
        if (string.IsNullOrEmpty(value))
            return;
        output.Write(',');
        WriteString(name, output);
        output.Write(':');
        WriteString(value, output);
    }

    private void WriteString(string value, TextWriter output) => _values.Format(new ScalarValue(value), output);

    private static string? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string text }
            ? text
            : null;
}
