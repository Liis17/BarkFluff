using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.DisableOtpVerificationServer;

public class DisableOtpVerificationServerCommandHandler : IRequestHandler<DisableOtpVerificationServerCommand, DisableOtpVerificationResponse>
{
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly ILogger<DisableOtpVerificationServerCommandHandler> _logger;

    public DisableOtpVerificationServerCommandHandler(AuthPropertiesStorage authPropertiesStorage,
        ILogger<DisableOtpVerificationServerCommandHandler> logger)
    {
        _authPropertiesStorage = authPropertiesStorage;
        _logger = logger;
    }

    public async Task<DisableOtpVerificationResponse> Handle(DisableOtpVerificationServerCommand request, CancellationToken cancellationToken)
    {
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
