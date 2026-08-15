using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.RejectFastAuth;

public class RejectFastAuthCommandHandler(
    IFastAuthSessionStore sessions,
    IFastAuthEventBus eventBus,
    UserContext userContext,
    MetricsCollector metrics,
    ILogger<RejectFastAuthCommandHandler> logger)
    : IRequestHandler<RejectFastAuthCommand, RejectFastAuthResponse>
{
    public async Task<RejectFastAuthResponse> Handle(RejectFastAuthCommand request, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(request.FastAuthId, cancellationToken)
            ?? throw new FastAuthSessionNotFoundException();

        if (session.Status == FastAuthStatus.Expired || DateTime.UtcNow >= session.ExpiresAt)
        {
            throw new FastAuthSessionExpiredException();
        }

        if (session.Status != FastAuthStatus.Scanned)
        {
            throw new FastAuthInvalidStateException();
        }

        if (session.UserId != userContext.UserId
            || string.IsNullOrEmpty(session.ConfirmationCode)
            || session.ConfirmationCode != request.ConfirmationCode)
        {
            throw new FastAuthInvalidConfirmationCodeException();
        }

        var transition = await sessions.TryRejectAsync(request.FastAuthId, request.ConfirmationCode,
            userContext.UserId, cancellationToken);

        switch (transition)
        {
            case FastAuthTransition.NotFound:
                throw new FastAuthSessionNotFoundException();
            case FastAuthTransition.Expired:
                metrics.Increment("sessions_expired");
                throw new FastAuthSessionExpiredException();
            case FastAuthTransition.InvalidState:
                throw new FastAuthInvalidStateException();
        }

        await eventBus.PublishAsync(session.Id, new FastAuthResult { Status = FastAuthStatus.Rejected },
            cancellationToken);

        metrics.Increment("sessions_rejected");

        logger.LogInformation(
            "FastAuth session {Id} rejected by user {UserId}",
            session.Id, userContext.UserId);

        return new RejectFastAuthResponse();
    }
}
