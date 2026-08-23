using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Features.CreateBotTokenServer;
using BarkFluff.Identity.Features.CreateSessionForUserServer;
using BarkFluff.Identity.Features.DisableOtpVerificationServer;
using BarkFluff.Identity.Features.ForceSetPasswordServer;
using BarkFluff.Identity.Features.GetActiveSessionsServer;
using BarkFluff.Identity.Features.GetBotTokenServer;
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
    private readonly MetricsCollector _metrics;

    public IdentityServerApiService(IMediator mediator, MetricsCollector metrics)
    {
        _mediator = mediator;
        _metrics = metrics;
    }

    public override Task<ListOtpVerificationResponse> ListOtpVerificationServer(
        ListOtpVerificationServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_otp_lookups");
        var command = new ListOtpVerificationServerCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<DisableOtpVerificationResponse> DisableOtpVerificationServer(
        DisableOtpVerificationServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_otp_disable_attempts");
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
        _metrics.Increment("server_session_lookups");
        var command = new GetActiveSessionsServerCommand
        {
            UserId = request.UserId
        };

        return _mediator.Send(command);
    }

    public override Task<RemoveActiveSessionResponse> RemoveActiveSessionServer(
        RemoveActiveSessionServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_session_removal_attempts");
        var command = new RemoveActiveSessionServerCommand
        {
            UserId = request.UserId,
            DeviceId = request.DeviceId
        };

        return _mediator.Send(command);
    }

    public override Task<CreateSessionForUserServerResponse> CreateSessionForUserServer(
        CreateSessionForUserServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_session_creation_attempts");
        var command = new CreateSessionForUserServerCommand
        {
            UserId = request.UserId,
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName,
            OperationSystem = request.OperationSystem,
            AppName = request.AppName,
            IpAddress = request.IpAddress
        };

        return _mediator.Send(command);
    }

    public override Task<ForceSetPasswordServerResponse> ForceSetPasswordServer(
        ForceSetPasswordServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_force_password_changes");
        return _mediator.Send(new ForceSetPasswordServerCommand
        {
            UserId = request.UserId,
            NewPassword = request.NewPassword
        });
    }

    public override Task<CreateBotTokenServerResponse> CreateBotTokenServer(
        CreateBotTokenServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_bot_token_creations");
        return _mediator.Send(new CreateBotTokenServerCommand
        {
            BotUserId = request.BotUserId
        });
    }

    public override Task<GetBotTokenServerResponse> GetBotTokenServer(
        GetBotTokenServerRequest request, ServerCallContext context)
    {
        _metrics.Increment("server_bot_token_reads");
        return _mediator.Send(new GetBotTokenServerCommand
        {
            BotUserId = request.BotUserId,
            TokenId = request.TokenId
        });
    }
}
