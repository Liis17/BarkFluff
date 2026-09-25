using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.AcceptFastAuth;

public class AcceptFastAuthCommandHandler(
    IFastAuthSessionStore sessions,
    IFastAuthEventBus eventBus,
    IdentityServerApi.IdentityServerApiClient identityClient,
    UserContext userContext,
    MetricsCollector metrics,
    ILogger<AcceptFastAuthCommandHandler> logger)
    : IRequestHandler<AcceptFastAuthCommand, AcceptFastAuthResponse>
{
    public async Task<AcceptFastAuthResponse> Handle(AcceptFastAuthCommand request, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(request.FastAuthId, cancellationToken)
            ?? throw new FastAuthSessionNotFoundException();

        if (session.Status == FastAuthStatus.Expired || DateTime.UtcNow >= session.ExpiresAt)
        {
            throw new FastAuthSessionExpiredException();
        }

        if (session.Status is not (FastAuthStatus.Scanned or FastAuthStatus.TelegramPending or FastAuthStatus.Accepted))
        {
            throw new FastAuthInvalidStateException();
        }

        if (session.UserId != userContext.UserId
            || string.IsNullOrEmpty(session.ConfirmationCode)
            || session.ConfirmationCode != request.ConfirmationCode)
        {
            throw new FastAuthInvalidConfirmationCodeException();
        }

        if (session.Status == FastAuthStatus.Accepted) return new AcceptFastAuthResponse();
        var transition = await sessions.TryWaitForTelegramAsync(session.Id, request.ConfirmationCode,
            userContext.UserId, cancellationToken);
        if (transition != FastAuthTransition.Ok)
            throw new FastAuthInvalidStateException();
        var pending = (await sessions.GetAsync(session.Id, cancellationToken))!;
        await eventBus.PublishAsync(session.Id, new FastAuthResult { Status = FastAuthStatus.TelegramPending }, cancellationToken);
        await new FastAuthCompletion(sessions, eventBus, identityClient, metrics).Advance(pending, cancellationToken);
        logger.LogInformation("FastAuth QR approval recorded for {Id} by {UserId}", session.Id[..8], userContext.UserId);

        return new AcceptFastAuthResponse();
    }
}
