using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

public sealed record SeqEventContext(
    string? CorrelationId,
    string? RequestId,
    string? TraceId,
    string? UserId);

public sealed record SeqErrorGroup(
    string Key,
    int Count,
    string Level,
    string? Application,
    string Message,
    string? MessageTemplate,
    string? Exception,
    DateTime? FirstSeenUtc,
    DateTime? LastSeenUtc,
    string? RepresentativeEventId,
    string? CorrelationId,
    string? RequestId,
    string? TraceId,
    string? UserId);

public static class SeqEventAnalyzer
{
    public static IReadOnlyList<SeqErrorGroup> GroupErrors(IEnumerable<JsonElement> events)
    {
        var groups = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);

        foreach (var evt in events)
        {
            var level = ReadTopLevelString(evt, "Level") ?? string.Empty;
            if (!level.Equals("Error", StringComparison.OrdinalIgnoreCase) &&
                !level.Equals("Fatal", StringComparison.OrdinalIgnoreCase) &&
                !level.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var application = ReadProperty(evt, "Application");
            var template = ReadTopLevelString(evt, "MessageTemplate");
            var message = ReadTopLevelString(evt, "RenderedMessage")
                ?? ReadTopLevelString(evt, "Message")
                ?? template
                ?? string.Empty;
            var exception = ReadTopLevelString(evt, "Exception");
            var identity = string.Join('\u001f',
                application ?? string.Empty,
                template ?? message,
                ExceptionType(exception));

            if (!groups.TryGetValue(identity, out var group))
            {
                group = new GroupAccumulator(StableKey(identity));
                groups.Add(identity, group);
            }

            group.Add(evt, level, application, message, template, exception);
        }

        return groups.Values
            .Select(group => group.ToResult())
            .OrderByDescending(group => group.LastSeenUtc ?? DateTime.MinValue)
            .ThenByDescending(group => group.Count)
            .ToList();
    }

    public static SeqEventContext ReadContext(JsonElement evt)
    {
        var traceId = FirstProperty(evt, "TraceId", "traceId");
        var correlationId = FirstProperty(evt,
            "CorrelationId", "CorrelationID", "X-Correlation-ID", "X-Correlation-Id")
            ?? traceId;
        var requestId = FirstProperty(evt,
            "RequestId", "RequestID", "TraceIdentifier", "X-Request-ID", "X-Request-Id");
        var userId = FirstProperty(evt,
            "UserId", "AffectedUserId", "TargetUserId", "SubjectUserId");

        return new SeqEventContext(correlationId, requestId, traceId, userId);
    }

    public static string? ReadProperty(JsonElement evt, string name)
    {
        if (TryGetProperty(evt, name, out var direct))
            return ScalarToString(direct);

        if (!TryGetProperty(evt, "Properties", out var properties))
            return null;

        if (properties.ValueKind == JsonValueKind.Object && TryGetProperty(properties, name, out var value))
            return ScalarToString(value);

        if (properties.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in properties.EnumerateArray())
            {
                if (!TryGetProperty(item, "Name", out var propertyName) ||
                    !string.Equals(ScalarToString(propertyName), name, StringComparison.OrdinalIgnoreCase) ||
                    !TryGetProperty(item, "Value", out var propertyValue))
                {
                    continue;
                }

                return ScalarToString(propertyValue);
            }
        }

        return null;
    }

    private static string? FirstProperty(JsonElement evt, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadProperty(evt, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ReadTopLevelString(JsonElement evt, string name) =>
        TryGetProperty(evt, name, out var value) ? ScalarToString(value) : null;

    private static DateTime? ReadTimestamp(JsonElement evt)
    {
        var raw = ReadTopLevelString(evt, "Timestamp")
            ?? ReadTopLevelString(evt, "TimestampUtc");
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp.ToUniversalTime()
            : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string? ScalarToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText()
    };

    private static string ExceptionType(string? exception)
    {
        if (string.IsNullOrWhiteSpace(exception))
            return string.Empty;

        var firstLine = exception.Split('\n', 2)[0].Trim();
        var separator = firstLine.IndexOf(':');
        return separator > 0 ? firstLine[..separator].Trim() : firstLine;
    }

    private static string StableKey(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private sealed class GroupAccumulator(string key)
    {
        private int _count;
        private DateTime? _firstSeenUtc;
        private DateTime? _lastSeenUtc;
        private string _level = "Error";
        private string? _application;
        private string _message = string.Empty;
        private string? _template;
        private string? _exception;
        private string? _eventId;
        private SeqEventContext _context = new(null, null, null, null);

        public void Add(
            JsonElement evt,
            string level,
            string? application,
            string message,
            string? template,
            string? exception)
        {
            _count++;
            var timestamp = ReadTimestamp(evt);
            if (timestamp.HasValue && (!_firstSeenUtc.HasValue || timestamp < _firstSeenUtc))
                _firstSeenUtc = timestamp;

            if (_count == 1 || (timestamp.HasValue && (!_lastSeenUtc.HasValue || timestamp >= _lastSeenUtc)))
            {
                _lastSeenUtc = timestamp ?? _lastSeenUtc;
                _level = level;
                _application = application;
                _message = message;
                _template = template;
                _exception = exception;
                _eventId = ReadTopLevelString(evt, "Id");
                _context = ReadContext(evt);
            }
        }

        public SeqErrorGroup ToResult() => new(
            key,
            _count,
            _level,
            _application,
            _message,
            _template,
            _exception,
            _firstSeenUtc,
            _lastSeenUtc,
            _eventId,
            _context.CorrelationId,
            _context.RequestId,
            _context.TraceId,
            _context.UserId);
    }
}
