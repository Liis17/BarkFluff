using BarkFluff.Federation.Services;

using Google.Protobuf;

using Grpc.Core;

namespace BarkFluff.Federation.Tests.Infrastructure;

// Строит XFed-заголовки вручную (не через XFedClientInterceptor) — тесту нужен полный контроль,
// чтобы конструировать и валидные, и намеренно испорченные запросы.
public static class SignedRequestBuilder
{
    public static Metadata BuildHeaders(
        string origin,
        string destination,
        string keyId,
        byte[] privateKeySeed,
        string methodFullName,
        IMessage request,
        long? timestampMsOverride = null)
    {
        var timestampMs = timestampMsOverride ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var requestBytes = request.ToByteArray();
        var canonical = XFedCanonicalString.Build(origin, destination, timestampMs, methodFullName, requestBytes);
        var signature = SigningKeyService.SignRaw(privateKeySeed, canonical);

        return new Metadata
        {
            { XFedHeaders.Origin, origin },
            { XFedHeaders.Destination, destination },
            { XFedHeaders.Timestamp, timestampMs.ToString() },
            { XFedHeaders.KeyId, keyId },
            { XFedHeaders.Signature, Convert.ToBase64String(signature) },
        };
    }
}
