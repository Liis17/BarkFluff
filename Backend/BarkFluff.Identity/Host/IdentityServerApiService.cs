using BarkFluff.Identity.Features.DisableOtpVerificationServer;
using BarkFluff.Identity.Features.GetActiveSessionsServer;
using BarkFluff.Identity.Features.ListOtpVerificationServer;
using BarkFluff.Identity.Features.RemoveActiveSessionServer;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Identity;
using Grpc.Core;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Identity.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class IdentityServerApiService : IdentityServerApi.IdentityServerApiBase
{
    private readonly IMediator _mediator;

    public IdentityServerApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override Task<ListOtpVerificationResponse> ListOtpVerificationServer(
        ListOtpVerificationServerRequest request, ServerCallContext context)
    {
        var command = new ListOtpVerificationServerCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<DisableOtpVerificationResponse> DisableOtpVerificationServer(
        DisableOtpVerificationServerRequest request, ServerCallContext context)
    {
        var command = new DisableOtpVerificationServerCommand
        {
            UserId = request.UserId,
            OtpType = request.OtpType
        };

        return _mediator.Send(command);
    }

    public override Task<GetActiveSessionsResponse> GetActiveSessionsServer(
        GetActiveSessionsServerRequest request, ServerCallContext context)
    {
        var command = new GetActiveSessionsServerCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<RemoveActiveSessionResponse> RemoveActiveSessionServer(
        RemoveActiveSessionServerRequest request, ServerCallContext context)
    {
        var command = new RemoveActiveSessionServerCommand
        {
            UserId = request.UserId,
            DeviceId = request.DeviceId
        };

        return _mediator.Send(command);
    }
}
