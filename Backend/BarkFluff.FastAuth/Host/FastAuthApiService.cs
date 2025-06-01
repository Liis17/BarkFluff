using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.FastAuth.Host;

public class FastAuthApiService : BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiBase
{
    
    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<GenerateConnectDeviceTokenResponse> GenerateConnectDeviceToken(GenerateConnectDeviceTokenRequest request, ServerCallContext context)
    {
        
        return base.GenerateConnectDeviceToken(request, context);
    }
}