using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.FastAuth;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.FastAuth.Features.ScanFastAuth;

public class ScanFastAuthCommandHandler(
    IFastAuthSessionStore sessions,
    IFastAuthEventBus eventBus,
    UserContext userContext,
    MetricsCollector metrics,
    ILogger<ScanFastAuthCommandHandler> logger)
    : IRequestHandler<ScanFastAuthCommand, ScanFastAuthResponse>
{
    public async Task<ScanFastAuthResponse> Handle(ScanFastAuthCommand request, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(request.FastAuthId, cancellationToken)
            ?? throw new FastAuthSessionNotFoundException();

        var confirmationCode = Guid.NewGuid().ToString();
        var transition = await sessions.TryScanAsync(request.FastAuthId, userContext.UserId,
            confirmationCode, cancellationToken);

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

        metrics.Increment("sessions_scanned");

        await eventBus.PublishAsync(session.Id, new FastAuthResult { Status = FastAuthStatus.Scanned },
            cancellationToken);

        logger.LogInformation(
            "FastAuth session {Id} scanned by user {UserId}",
            session.Id[..8], userContext.UserId);

        return new ScanFastAuthResponse
        {
            DeviceName = session.DeviceName,
            OperationSystem = session.OperationSystem,
            AppName = session.AppName,
            AppVersion = session.AppVersion,
            IpAddress = session.IpAddress,
            ConfirmationCode = confirmationCode,
            ExpiresAt = Timestamp.FromDateTime(session.ExpiresAt)
        };
    }
}
