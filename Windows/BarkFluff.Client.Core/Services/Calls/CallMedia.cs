using System.Text.Json;

namespace BarkFluff.Client.Core.Services.Calls;

public interface ICallMedia
{
    event Action<CallMediaEvent>? Received;
    Task ConnectAsync(Guid callId, Uri url, string token, CancellationToken ct);
    void Send(Guid callId, string type, string? deviceId = null);
    void Disconnect();
}

public sealed record CallAudioDevice(string Id, string Label, string Kind);
public sealed record CallMediaEvent(Guid CallId, string Type, IReadOnlyList<CallAudioDevice>? Devices = null);

public sealed class CallRuntimeException : Exception;

public static class CallMediaBridge
{
    public static bool TryRead(string json, Guid callId, out CallMediaEvent? message)
    {
        message = null;
        if (json.Length > 65536 || callId == Guid.Empty)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 5 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !ReadString(root, "type", 32, out var type) ||
                !ReadString(root, "call_id", 36, out var id) || !Guid.TryParseExact(id, "D", out var parsed) || parsed != callId)
                return false;

            if (type is "connected" or "participant_joined" or "participant_left" or "error" or "disconnected")
            {
                if (root.EnumerateObject().Count() != 2)
                    return false;
                message = new(callId, type);
                return true;
            }

            if (type != "devices" || root.EnumerateObject().Count() != 3 ||
                !root.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array ||
                devices.GetArrayLength() > 128)
                return false;

            var result = new List<CallAudioDevice>();
            foreach (var device in devices.EnumerateArray())
            {
                if (device.ValueKind != JsonValueKind.Object || device.EnumerateObject().Count() != 3 ||
                    !ReadString(device, "id", 512, out var deviceId) || deviceId.Length == 0 ||
                    !ReadString(device, "label", 512, out var label) ||
                    !ReadString(device, "kind", 12, out var kind) || kind is not ("audioinput" or "audiooutput"))
                    return false;
                result.Add(new(deviceId, label, kind));
            }
            message = new(callId, type, result);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryGetEndpoint(string value, out Uri url)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme == "wss" &&
            parsed.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6 &&
            parsed.UserInfo.Length == 0 && parsed.Query.Length == 0 && parsed.Fragment.Length == 0)
        {
            url = parsed;
            return true;
        }
        url = null!;
        return false;
    }

    private static bool ReadString(JsonElement element, string key, int maxLength, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.String)
            return false;
        value = item.GetString()!;
        return value.Length <= maxLength;
    }
}
