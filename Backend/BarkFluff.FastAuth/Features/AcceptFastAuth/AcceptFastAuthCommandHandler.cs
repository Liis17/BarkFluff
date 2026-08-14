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

        var acceptedResult = new FastAuthSessionResult(
            FastAuthStatus.Accepted,
            sessionResponse.AccessToken.Value,
            sessionResponse.AccessToken.ExpirationDate.ToDateTime(),
            sessionResponse.RefreshToken.Value,
            sessionResponse.RefreshToken.ExpirationDate.ToDateTime());

        var transition = await sessions.TryAcceptAsync(request.FastAuthId, request.ConfirmationCode,
            userContext.UserId, acceptedResult, cancellationToken);

        if (transition != FastAuthTransition.Ok)
        {
            // Проиграли гонку (параллельный Accept/Reject/истечение) — откатываем выпущенную сессию.
            await identityClient.RemoveActiveSessionServerAsync(
                new Proto.Identity.RemoveActiveSessionServerRequest
                {
                    UserId = userContext.UserId,
                    DeviceId = newDeviceId
                }, cancellationToken: cancellationToken);

            throw transition switch
            {
                FastAuthTransition.NotFound => new FastAuthSessionNotFoundException(),
                FastAuthTransition.Expired => new FastAuthSessionExpiredException(),
                _ => new FastAuthInvalidStateException()
            };
        }

        await eventBus.PublishAsync(session.Id, acceptedResult.ToProto(), cancellationToken);

        metrics.Increment("sessions_accepted");

        logger.LogInformation(
            "FastAuth session {Id} accepted by user {UserId}, new device {DeviceId} provisioned",
            session.Id[..8], userContext.UserId, newDeviceId);

        return new AcceptFastAuthResponse();
    }
}
