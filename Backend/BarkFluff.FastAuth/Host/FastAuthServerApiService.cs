using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.FastAuth.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class FastAuthServerApiService : BarkFluff.Proto.FastAuth.FastAuthServerApi.FastAuthServerApiBase
{
    public override Task<GetFastAuthInfoResponse> GetFastAuthInfo(
        GetFastAuthInfoRequest request, ServerCallContext context)
    {
        // Не реализован в первой итерации — оставлено как точка расширения для админки/отладки.
        throw new RpcException(new Status(StatusCode.Unimplemented, "GetFastAuthInfo is not implemented yet"));
    }
}
