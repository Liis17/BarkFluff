using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BarkFluff.Identity.Features.ListOtpVerification;

public class ListOtpVerificationCommandHandler : IRequestHandler<ListOtpVerificationCommand, ListOtpVerificationResponse>
{
    private readonly UserContext _userContext;
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly ILogger<ListOtpVerificationCommandHandler> _logger;

    public ListOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage,
        ILogger<ListOtpVerificationCommandHandler> logger)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _logger = logger;
    }

    public async Task<ListOtpVerificationResponse> Handle(ListOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Получение статуса 2FA для пользователя {UserId}", _userContext.UserId);

        var otpAuth = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);

        if (otpAuth is null)
        {
            _logger.LogWarning("Настройки 2FA не найдены для пользователя {UserId}", _userContext.UserId);
            throw new OtpNotCreatedException();
        }

        _logger.LogInformation(
            "Статус 2FA для пользователя {UserId}: Authenticator={AuthEnabled}, Email={EmailEnabled}",
            _userContext.UserId,
            otpAuth.OtpEnabled,
            otpAuth.EmailOtpEnabled
        );

        return new ListOtpVerificationResponse()
        {
            EmailEnabled = otpAuth.EmailOtpEnabled,
            AuthenticatorEnabled = otpAuth.OtpEnabled,
        };
    }
}