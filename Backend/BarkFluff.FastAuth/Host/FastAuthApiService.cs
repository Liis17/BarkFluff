using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.FastAuth.Host;

public class FastAuthApiService : BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiBase
{
    private readonly MetricsCollector _metrics;

    public FastAuthApiService(MetricsCollector metrics)
    {
        _metrics = metrics;
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<GenerateConnectDeviceTokenResponse> GenerateConnectDeviceToken(GenerateConnectDeviceTokenRequest request, ServerCallContext context)
    {
        _metrics.Increment("tokens_generated");
        return base.GenerateConnectDeviceToken(request, context);
    }
}