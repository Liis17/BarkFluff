using BarkFluff.FastAuth.Features.AcceptFastAuth;
using BarkFluff.FastAuth.Features.GenerateFastAuthToken;
using BarkFluff.FastAuth.Features.RejectFastAuth;
using BarkFluff.FastAuth.Features.ScanFastAuth;
using BarkFluff.FastAuth.Features.SubscribeFastAuthResult;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.FastAuth.Host;

public class FastAuthApiService(
    IMediator mediator,
    SubscribeFastAuthResultQueryHandler subscribeHandler)
    : BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiBase
{
    [AllowAnonymous]
    public override Task<GenerateFastAuthTokenResponse> GenerateFastAuthToken(
        GenerateFastAuthTokenRequest request, ServerCallContext context)
    {
        return mediator.Send(new GenerateFastAuthTokenCommand
        {
            Format = request.Format
        });
    }

    [AllowAnonymous]
    public override Task SubscribeFastAuthResult(
        SubscribeFastAuthResultRequest request,
        IServerStreamWriter<FastAuthResult> responseStream,
        ServerCallContext context)
    {
        return subscribeHandler.Handle(new SubscribeFastAuthResultQuery
        {
            FastAuthId = request.FastAuthId,
            ResponseStream = responseStream,
            CancellationToken = context.CancellationToken
        });
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<ScanFastAuthResponse> ScanFastAuth(
        ScanFastAuthRequest request, ServerCallContext context)
    {
        return mediator.Send(new ScanFastAuthCommand
        {
            FastAuthId = request.FastAuthId
        });
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<AcceptFastAuthResponse> AcceptFastAuth(
        AcceptFastAuthRequest request, ServerCallContext context)
    {
        return mediator.Send(new AcceptFastAuthCommand
        {
            FastAuthId = request.FastAuthId,
            ConfirmationCode = request.ConfirmationCode
        });
    }

    [Authorize(Policy = nameof(TokenType.User))]
    public override Task<RejectFastAuthResponse> RejectFastAuth(
        RejectFastAuthRequest request, ServerCallContext context)
    {
        return mediator.Send(new RejectFastAuthCommand
        {
            FastAuthId = request.FastAuthId,
            ConfirmationCode = request.ConfirmationCode
        });
    }
}
