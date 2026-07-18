using System.Security.Cryptography;

using BarkFluff.Proto.Federation;

using Google.Protobuf;

using Grpc.Core;
using Grpc.Net.Client;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

// fedping — мини-CLI двух-нодового стенда (docs/rearch/phase-1/step-1.3-xfed-signing.md,
// Изменение 7.3). Дублирует каноническую строку + подпись Ed25519 самостоятельно
// (BarkFluff.Federation.Services.XFedCanonicalString / SigningKeyService.SignRaw) — инструмент
// намеренно вне BarkFluff.sln, без ProjectReference.
//
// Использование:
//   dotnet run -- <address> <origin> <destination> <key-id> <base64-seed>
// Пример (из seed-peers.sql, plaintext-стенд до этапа 1.6):
//   dotnet run -- http://federation2:7030 node1.test node2.test ed25519:1 <base64 seed из сида>

if (args.Length != 5)
{
    Console.Error.WriteLine("Использование: fedping <address> <origin> <destination> <key-id> <base64-seed>");
    return 1;
}

var address = args[0];
var origin = args[1];
var destination = args[2];
var keyId = args[3];
var seed = Convert.FromBase64String(args[4]);

const string method = "/barkfluff.federation.FederationS2SApi/Ping";

var request = new PingRequest { OriginServer = origin };
request.ProtocolVersions.Add(1);

var requestBytes = request.ToByteArray();
var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

var hashHex = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant();
var canonical = $"{origin}\n{destination}\n{timestampMs}\n{method}\n{hashHex}";
var canonicalBytes = System.Text.Encoding.UTF8.GetBytes(canonical);

var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
ISigner signer = new Ed25519Signer();
signer.Init(true, privateKey);
signer.BlockUpdate(canonicalBytes, 0, canonicalBytes.Length);
var signature = signer.GenerateSignature();

var headers = new Metadata
{
    { "x-bf-origin", origin },
    { "x-bf-destination", destination },
    { "x-bf-timestamp", timestampMs.ToString() },
    { "x-bf-key-id", keyId },
    { "x-bf-signature", Convert.ToBase64String(signature) },
};

Console.WriteLine($"→ {address} Ping (origin={origin}, destination={destination}, key={keyId})");

using var channel = GrpcChannel.ForAddress(address);
var client = new FederationS2SApi.FederationS2SApiClient(channel);

try
{
    var response = await client.PingAsync(request, headers);
    Console.WriteLine($"OK: server_name={response.ServerName}, server_time={response.ServerTime}, protocol_versions=[{string.Join(",", response.ProtocolVersions)}]");
    return 0;
}
catch (RpcException ex)
{
    Console.WriteLine($"ОШИБКА: {ex.StatusCode} — {ex.Status.Detail}");
    var errorCode = ex.Trailers.GetValue("x-error-code");
    if (errorCode != null)
        Console.WriteLine($"x-error-code: {errorCode}");
    return 1;
}
