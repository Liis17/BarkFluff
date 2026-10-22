using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.Prekeys;
using BarkFluff.Users.Mapping;
using BarkFluff.Users.Persistence.Services;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.GetOwnPrekeyStatus;

public class GetOwnPrekeyStatusQueryHandler(
    PrekeyStorage prekeyStorage,
    UserContext userContext)
    : IRequestHandler<GetOwnPrekeyStatusQuery, GetOwnPrekeyStatusResponse>
{
    public async Task<GetOwnPrekeyStatusResponse> Handle(
        GetOwnPrekeyStatusQuery query,
        CancellationToken cancellationToken)
    {
        var deviceId = PrekeyRequestContext.RequireDeviceId(userContext);
        var status = await prekeyStorage.GetOwnPrekeyStatusAsync(deviceId, userContext.UserId, cancellationToken);
        var bundle = status.Bundle;
        var response = new GetOwnPrekeyStatusResponse
        {
            HasBundle = bundle != null,
            RemainingOneTimePrekeys = status.RemainingEcPrekeys,
            RemainingOneTimeKyberPrekeys = status.RemainingKyberPrekeys,
        };

        if (bundle != null)
        {
            response.IdentityPubkey = Google.Protobuf.ByteString.CopyFrom(bundle.IdentityPubkey);
            response.RegistrationId = (uint)bundle.RegistrationId;
            response.SupportsPqxdh = bundle.LastResortKyberPrekeyId != null &&
                bundle.LastResortKyberPrekeyPublic != null &&
                bundle.LastResortKyberPrekeySignature != null;
            if (response.SupportsPqxdh)
            {
                response.LastResortKyberPrekey = bundle.ToLastResortKyberGrpc();
            }
        }

        return response;
    }
}
