using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.RejectFastAuth;

public class RejectFastAuthCommandHandler(
    FastAuthSessionsManager sessions,
    UserContext userContext,
    MetricsCollector metrics,
    ILogger<RejectFastAuthCommandHandler> logger)
    : IRequestHandler<RejectFastAuthCommand, RejectFastAuthResponse>
{
    public Task<RejectFastAuthResponse> Handle(RejectFastAuthCommand request, CancellationToken cancellationToken)
    {
        var session = sessions.TryGet(request.FastAuthId)
            ?? throw new FastAuthSessionNotFoundException();

        if (session.Status == Proto.FastAuth.FastAuthStatus.Expired)
        {
            throw new FastAuthSessionExpiredException();
        }

        if (session.Status != Proto.FastAuth.FastAuthStatus.Scanned)
        {
            throw new FastAuthInvalidStateException();
        }

        if (session.UserId != userContext.UserId
            || string.IsNullOrEmpty(session.ConfirmationCode)
            || session.ConfirmationCode != request.ConfirmationCode)
        {
            throw new FastAuthInvalidConfirmationCodeException();
        }

        if (!session.TryReject(request.ConfirmationCode, userContext.UserId))
        {
            throw new FastAuthInvalidStateException();
        }

        metrics.Increment("sessions_rejected");

        logger.LogInformation(
            "FastAuth session {Id} rejected by user {UserId}",
            session.Id, userContext.UserId);

        return Task.FromResult(new RejectFastAuthResponse());
    }
}
