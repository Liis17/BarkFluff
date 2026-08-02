using System.Text.Json;

namespace Barkfluff.AdminPanel.Services;

/// <summary>Парсит оба представления Properties, которые возвращает Seq Events API.</summary>
public static class ServiceMetricsEventParser
{
    public static bool TryParse(JsonElement evt, out ServiceMetricsEvent snapshot)
    {
        snapshot = default!;
        if (!evt.TryGetProperty("Properties", out var properties)) return false;
        var wrapper = GetProperty(properties, "Metrics");
        if (wrapper is null || wrapper.Value.ValueKind != JsonValueKind.Object) return false;

        var metrics = wrapper.Value;
        if (metrics.TryGetProperty("Metrics", out var nested) && nested.ValueKind == JsonValueKind.Object)
            metrics = nested;
        if (!metrics.TryGetProperty("SchemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 2)
            return false;

        var serviceName = GetString(metrics, "ServiceName") ?? GetString(GetProperty(properties, "Application"));
        if (string.IsNullOrWhiteSpace(serviceName)) return false;

        var timestamp = GetTimestamp(evt) ?? DateTime.MinValue;
        snapshot = new ServiceMetricsEvent(
            serviceName,
            ExtractValues(metrics, "Counters"),
            ExtractValues(metrics, "Gauges"),
            timestamp);
        return true;
    }

    private static Dictionary<string, long> ExtractValues(JsonElement source, string property)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!source.TryGetProperty(property, out var objectValue) || objectValue.ValueKind != JsonValueKind.Object)
            return values;
        foreach (var item in objectValue.EnumerateObject())
            if (item.Value.ValueKind == JsonValueKind.Number && item.Value.TryGetInt64(out var value))
                values[item.Name] = value;
        return values;
    }

    private static string? GetString(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() : null;

    private static string? GetString(JsonElement source, string property) =>
        source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTime? GetTimestamp(JsonElement evt) =>
        evt.TryGetProperty("Timestamp", out var timestamp) && timestamp.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(timestamp.GetString(), out var value) ? value.ToUniversalTime() : null;

    private static JsonElement? GetProperty(JsonElement properties, string name)
    {
        if (properties.ValueKind == JsonValueKind.Object)
            return properties.TryGetProperty(name, out var value) ? value : null;
        if (properties.ValueKind != JsonValueKind.Array) return null;
        foreach (var property in properties.EnumerateArray())
            if (property.TryGetProperty("Name", out var propertyName) && propertyName.GetString() == name &&
                property.TryGetProperty("Value", out var value))
                return value;
        return null;
    }
}

public sealed record ServiceMetricsEvent(
    string ServiceName,
    Dictionary<string, long> Counters,
    Dictionary<string, long> Gauges,
    DateTime Timestamp);
