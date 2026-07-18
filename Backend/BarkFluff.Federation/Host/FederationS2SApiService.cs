using BarkFluff.Proto.Federation;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

namespace BarkFluff.Federation.Host;

// Авторизация — НЕ XAuth: Ed25519-подпись S2S-запросов (XFed, этап 1.3).
// В 1.1 Ping временно доступен без подписи.
public class FederationS2SApiService : FederationS2SApi.FederationS2SApiBase
{
    private readonly IConfiguration _configuration;

    public FederationS2SApiService(IConfiguration configuration)
    {
        _configuration = configuration;
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
}
