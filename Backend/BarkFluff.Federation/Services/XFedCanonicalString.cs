using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Federation.Services;

// docs/rearch/02-trust-and-certs.md, "Подпись каждого S2S-запроса":
// {origin}\n{destination}\n{timestamp}\n{grpc-method-full-name}\n{hex(sha256(request-bytes))}
// Общая для клиента (XFedClientInterceptor) и сервера (XFedServerInterceptor) — единственное место
// построения канонической строки.
public static class XFedCanonicalString
{
    public static byte[] Build(string origin, string destination, long timestampMs, string grpcMethodFullName, byte[] requestBytes)
    {
        var hashHex = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant();
        var canonical = $"{origin}\n{destination}\n{timestampMs}\n{grpcMethodFullName}\n{hashHex}";
        return Encoding.UTF8.GetBytes(canonical);
    }
}
