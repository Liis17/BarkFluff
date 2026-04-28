using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.GrpcServer.Tracker;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Shared.Exceptions.Identity;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.FastAuth.Features.GenerateFastAuthToken;

public class GenerateFastAuthTokenCommandHandler(
    FastAuthSessionsManager sessions,
    QrCodeGenerator qrGenerator,
    RequestContext requestContext,
    MetricsCollector metrics,
    ILogger<GenerateFastAuthTokenCommandHandler> logger)
    : IRequestHandler<GenerateFastAuthTokenCommand, GenerateFastAuthTokenResponse>
{
    public Task<GenerateFastAuthTokenResponse> Handle(GenerateFastAuthTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(requestContext.DeviceName))
        {
            throw new XDeviceNameIsRequiredException();
        }

        if (string.IsNullOrEmpty(requestContext.OperationSystem))
        {
            throw new XOsNameIsRequiredException();
        }

        if (string.IsNullOrEmpty(requestContext.AppName) || string.IsNullOrEmpty(requestContext.AppVersion))
        {
            throw new XAppInfoIsRequiedException();
        }

        var session = sessions.Create(
            deviceName: requestContext.DeviceName!,
            operationSystem: requestContext.OperationSystem!,
            appName: requestContext.AppName!,
            appVersion: requestContext.AppVersion!,
            ipAddress: requestContext.IpAddress ?? string.Empty);

        metrics.Increment("sessions_generated");

        logger.LogInformation(
            "FastAuth session {Id} created for device {DeviceName} ({Os}, {AppName} v.{AppVersion}), expires at {ExpiresAt:O}",
            session.Id, session.DeviceName, session.OperationSystem,
            session.AppName, session.AppVersion, session.ExpiresAt);

        var format = request.Format == TokenFormat.Unknown ? TokenFormat.Qr : request.Format;
        var tokenValue = format switch
        {
            TokenFormat.Qr => qrGenerator.GeneratePngBase64(session.Id),
            _ => session.Id
        };

        return Task.FromResult(new GenerateFastAuthTokenResponse
        {
            FastAuthId = session.Id,
            ExpiresAt = Timestamp.FromDateTime(session.ExpiresAt),
            Token = new FastAuthToken
            {
                Format = format,
                Value = tokenValue
            }
        });
    }
}
