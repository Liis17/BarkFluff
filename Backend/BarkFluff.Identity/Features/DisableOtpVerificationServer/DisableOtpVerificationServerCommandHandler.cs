using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Proto.Users;

using Grpc.Core;

using MediatR;

namespace BarkFluff.Identity.Features.DisableOtpVerificationServer;

public class DisableOtpVerificationServerCommandHandler : IRequestHandler<DisableOtpVerificationServerCommand, DisableOtpVerificationResponse>
{
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly UsersServerApi.UsersServerApiClient _usersClient;
    private readonly ILogger<DisableOtpVerificationServerCommandHandler> _logger;

    public DisableOtpVerificationServerCommandHandler(AuthPropertiesStorage authPropertiesStorage,
        UsersServerApi.UsersServerApiClient usersClient,
        ILogger<DisableOtpVerificationServerCommandHandler> logger)
    {
        _authPropertiesStorage = authPropertiesStorage;
        _usersClient = usersClient;
        _logger = logger;
    }

    public async Task<DisableOtpVerificationResponse> Handle(DisableOtpVerificationServerCommand request, CancellationToken cancellationToken)
    {
        var restriction = await _usersClient.GetAccountRestrictionAsync(
            new GetAccountRestrictionRequest { UserId = request.UserId }, cancellationToken: cancellationToken);
        if (restriction.Restriction.Status == AccountStatus.Deleted)
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Deleted accounts cannot have credentials changed"));

        _logger.LogInformation(
            "Принудительное отключение 2FA для пользователя {UserId}, тип: {OtpType}",
            request.UserId, request.OtpType);

        if (request.OtpType == OtpTypeId.Authenticator)
        {
            await _authPropertiesStorage.DisableOtp(request.UserId);
        }

        if (request.OtpType == OtpTypeId.Email)
        {
            await _authPropertiesStorage.DisableEmailOtp(request.UserId);
        }

        _logger.LogInformation(
            "2FA успешно отключена для пользователя {UserId}. Тип: {OtpType}",
            request.UserId, request.OtpType);

        return new DisableOtpVerificationResponse();
    }
}
