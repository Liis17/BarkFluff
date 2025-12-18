using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;
using Microsoft.Extensions.Logging;
using OtpNet;
using OtpNotCreatedException = BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException;
using OtpType = BarkFluff.Identity.Domain.OtpType;

namespace BarkFluff.Identity.Features.ConfirmOtpVerification;

public class ConfirmOtpVerificationCommandHandler : IRequestHandler<ConfirmOtpVerificationCommand, ConfirmOtpVerificationResponse>
{
    private readonly UserContext _userContext;
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    private readonly ILogger<ConfirmOtpVerificationCommandHandler> _logger;

    public ConfirmOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage,
        ILogger<ConfirmOtpVerificationCommandHandler> logger)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
        _logger = logger;
    }

    public async Task<ConfirmOtpVerificationResponse> Handle(ConfirmOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Начало подтверждения OTP для пользователя {UserId}",
            _userContext.UserId
        );

        try
        {
            var otpConfigs = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);

            if (otpConfigs.SelectedOtpType == OtpType.Authenticator)
            {
                _logger.LogDebug("Проверка Authenticator OTP кода для пользователя {UserId}", _userContext.UserId);

                var otpSecret = await _authPropertiesStorage.GetOtpSecretKey(_userContext.UserId);

                var totp = new Totp(Base32Encoding.ToBytes(otpSecret));

                var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

                if (!isValid)
                {
                    _logger.LogWarning(
                        "Неверный Authenticator OTP код для пользователя {UserId}",
                        _userContext.UserId
                    );
                    throw new NotValidOtpCodeException();
                }

                _logger.LogDebug("Активация Authenticator OTP для пользователя {UserId}", _userContext.UserId);

                await _authPropertiesStorage.EnableOtp(_userContext.UserId);

                _logger.LogInformation(
                    "Authenticator OTP успешно активирован для пользователя {UserId}",
                    _userContext.UserId
                );
            }

            if (otpConfigs.SelectedOtpType == OtpType.Email)
            {
                _logger.LogDebug("Проверка Email OTP кода для пользователя {UserId}", _userContext.UserId);

                if (!string.Equals(otpConfigs.LastEmailAuthCode, request.OtpCode,
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogWarning(
                        "Неверный Email OTP код для пользователя {UserId}",
                        _userContext.UserId
                    );
                    throw new NotValidOtpCodeException();
                }

                _logger.LogDebug("Активация Email OTP для пользователя {UserId}", _userContext.UserId);

                await _authPropertiesStorage.EnableEmailOtp(_userContext.UserId);

                _logger.LogInformation(
                    "Email OTP успешно активирован для пользователя {UserId}",
                    _userContext.UserId
                );
            }

        }
        catch (OtpNotCreatedException ex)
        {
            _logger.LogError(
                ex,
                "OTP не был создан для пользователя {UserId}",
                _userContext.UserId
            );
            throw new BarkFluff.Shared.Exceptions.Identity.OtpNotCreatedException();
        }

        return new ConfirmOtpVerificationResponse();
    }
}