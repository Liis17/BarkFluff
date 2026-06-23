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
    FastAuthSessionsManager sessions,
    UserContext userContext,
    MetricsCollector metrics,
    ILogger<ScanFastAuthCommandHandler> logger)
    : IRequestHandler<ScanFastAuthCommand, ScanFastAuthResponse>
{
    public Task<ScanFastAuthResponse> Handle(ScanFastAuthCommand request, CancellationToken cancellationToken)
    {
        var session = sessions.TryGet(request.FastAuthId)
            ?? throw new FastAuthSessionNotFoundException();

        var outcome = session.TryScan(userContext.UserId);

        switch (outcome)
        {
            case ScanOutcome.Expired:
                throw new FastAuthSessionExpiredException();
            case ScanOutcome.AlreadyHandled:
                throw new FastAuthInvalidStateException();
        }

        metrics.Increment("sessions_scanned");

        logger.LogInformation(
            "FastAuth session {Id} scanned by user {UserId}",
            session.Id[..8], userContext.UserId);

        return Task.FromResult(new ScanFastAuthResponse
        {
            DeviceName = session.DeviceName,
            OperationSystem = session.OperationSystem,
            AppName = session.AppName,
            AppVersion = session.AppVersion,
            IpAddress = session.IpAddress,
            ConfirmationCode = session.ConfirmationCode!,
            ExpiresAt = Timestamp.FromDateTime(session.ExpiresAt)
        });
    }
}
