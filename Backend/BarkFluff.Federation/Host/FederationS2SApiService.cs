using BarkFluff.Federation.Services;
using BarkFluff.Proto.Federation;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

namespace BarkFluff.Federation.Host;

// Авторизация — НЕ XAuth: Ed25519-подпись S2S-запросов (XFed, этап 1.3).
// В 1.1/1.2 Ping и GetServerKeys временно доступны без подписи (GetServerKeys — bootstrap-канал,
// останется неподписанным и после 1.3, см. docs/rearch/phase-1/step-1.2-keys-wellknown.md).
public class FederationS2SApiService : FederationS2SApi.FederationS2SApiBase
{
    private readonly IConfiguration _configuration;
    private readonly SigningKeyService _signingKeyService;

    public FederationS2SApiService(IConfiguration configuration, SigningKeyService signingKeyService)
    {
        _configuration = configuration;
        _signingKeyService = signingKeyService;
    }

    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        var response = new PingResponse
        {
            ServerName = _configuration["Federation:ServerName"] ?? string.Empty,
            ServerTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        response.ProtocolVersions.Add(1);

        return Task.FromResult(response);
    }

    public override async Task<GetServerKeysResponse> GetServerKeys(GetServerKeysRequest request, ServerCallContext context)
    {
        var keys = await _signingKeyService.GetNonRevokedKeysAsync(context.CancellationToken);

        var response = new GetServerKeysResponse
        {
            ServerName = _configuration["Federation:ServerName"] ?? string.Empty,
        };

        response.Keys.AddRange(keys.Select(k => new SigningKey
        {
            KeyId = k.KeyId,
            PublicKey = Google.Protobuf.ByteString.CopyFrom(k.PublicKey),
            ExpiredAt = k.ExpiredAt.HasValue
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(k.ExpiredAt.Value, DateTimeKind.Utc))
                : null,
        }));

        return response;
    }
}
