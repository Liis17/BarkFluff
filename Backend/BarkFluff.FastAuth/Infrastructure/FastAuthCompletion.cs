using BarkFluff.FastAuth.Domain;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace BarkFluff.FastAuth.Infrastructure;

public sealed class FastAuthCompletion(IFastAuthSessionStore sessions, IFastAuthEventBus events,
    IdentityServerApi.IdentityServerApiClient identity, MetricsCollector metrics)
{
    public async Task Advance(FastAuthSessionState session, CancellationToken ct)
    {
        if (session.Status != FastAuthStatus.TelegramPending || session.UserId == null) return;
        var response = await identity.CreateSessionForUserServerAsync(new CreateSessionForUserServerRequest
        {
            UserId = session.UserId.Value, DeviceId = session.Id, AttemptId = session.Id,
            ExpiresAt = Timestamp.FromDateTime(session.ExpiresAt), DeviceName = session.DeviceName,
            OperationSystem = session.OperationSystem, AppName = $"{session.AppName} v.{session.AppVersion}",
            IpAddress = session.IpAddress
        }, cancellationToken: ct);
        if (response.ConfirmationState == AuthChallengeState.Waiting) return;
        if (response.ConfirmationState is AuthChallengeState.Rejected or AuthChallengeState.Cancelled or AuthChallengeState.Expired)
        {
            if (await sessions.TryRejectAsync(session.Id, session.ConfirmationCode!, session.UserId.Value, ct) == FastAuthTransition.Ok)
                await events.PublishAsync(session.Id, new FastAuthResult { Status = FastAuthStatus.Rejected }, ct);
            return;
        }
        if (response.ConfirmationState != AuthChallengeState.Completed ||
            string.IsNullOrEmpty(response.AccessToken?.Value) || string.IsNullOrEmpty(response.RefreshToken?.Value))
            throw new RpcException(new Status(StatusCode.Unavailable, "Identity did not complete FastAuth"));
        var result = new FastAuthSessionResult(FastAuthStatus.Accepted, response.AccessToken.Value,
            response.AccessToken.ExpirationDate.ToDateTime(), response.RefreshToken.Value, response.RefreshToken.ExpirationDate.ToDateTime());
        var transition = await sessions.TryAcceptAsync(session.Id, session.ConfirmationCode!, session.UserId.Value, result, ct);
        if (transition == FastAuthTransition.Ok)
        {
            metrics.Increment("sessions_accepted");
            await events.PublishAsync(session.Id, result.ToProto(), ct);
            return;
        }
        var latest = await sessions.GetAsync(session.Id, ct);
        // Parallel Accept calls share one Identity session. Never revoke the winner's tokens.
        if (latest?.Status == FastAuthStatus.Accepted) return;
        await identity.RemoveActiveSessionServerAsync(new RemoveActiveSessionServerRequest
        { UserId = session.UserId.Value, DeviceId = session.Id }, cancellationToken: ct);
    }
}
