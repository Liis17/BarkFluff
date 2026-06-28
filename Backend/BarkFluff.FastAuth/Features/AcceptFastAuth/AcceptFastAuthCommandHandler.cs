using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.FastAuth;

using MediatR;

namespace BarkFluff.FastAuth.Features.AcceptFastAuth;

public class AcceptFastAuthCommandHandler(
    FastAuthSessionsManager sessions,
    IdentityServerApi.IdentityServerApiClient identityClient,
    UserContext userContext,
    MetricsCollector metrics,
    ILogger<AcceptFastAuthCommandHandler> logger)
    : IRequestHandler<AcceptFastAuthCommand, AcceptFastAuthResponse>
{
    public async Task<AcceptFastAuthResponse> Handle(AcceptFastAuthCommand request, CancellationToken cancellationToken)
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

        var newDeviceId = Guid.NewGuid().ToString();

        var sessionResponse = await identityClient.CreateSessionForUserServerAsync(new CreateSessionForUserServerRequest
        {
            UserId = userContext.UserId,
            DeviceId = newDeviceId,
            DeviceName = session.DeviceName,
            OperationSystem = session.OperationSystem,
            AppName = $"{session.AppName} v.{session.AppVersion}",
            IpAddress = session.IpAddress
        }, cancellationToken: cancellationToken);

        var acceptedResult = new FastAuthResult
        {
            Status = Proto.FastAuth.FastAuthStatus.Accepted,
            AccessToken = sessionResponse.AccessToken.Value,
            AccessTokenExpiresAt = sessionResponse.AccessToken.ExpirationDate,
            RefreshToken = sessionResponse.RefreshToken.Value,
            RefreshTokenExpiresAt = sessionResponse.RefreshToken.ExpirationDate
        };

        if (!session.TryAccept(request.ConfirmationCode, userContext.UserId, acceptedResult))
        {
            await identityClient.RemoveActiveSessionServerAsync(
                new Proto.Identity.RemoveActiveSessionServerRequest
                {
                    UserId = userContext.UserId,
                    DeviceId = newDeviceId
                }, cancellationToken: cancellationToken);
            throw new FastAuthInvalidStateException();
        }

        metrics.Increment("sessions_accepted");

        logger.LogInformation(
            "FastAuth session {Id} accepted by user {UserId}, new device {DeviceId} provisioned",
            session.Id[..8], userContext.UserId, newDeviceId);

        return new AcceptFastAuthResponse();
    }
}
