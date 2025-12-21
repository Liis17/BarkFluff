using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;
using Microsoft.Extensions.Logging;
using OtpNet;

using OtpType = BarkFluff.Identity.Domain.OtpType;

namespace BarkFluff.Identity.Features.ConfirmResetPassword
{
    using CreateToken;
    using Google.Protobuf.WellKnownTypes;
    using GrpcServer.Tracker;
    using Proto.Identity;
    using Services;

    public class ConfirmResetPasswordCommandHandler : IRequestHandler<ConfirmResetPasswordCommand, ConfirmResetPasswordResponse>
    {
        private readonly ResetPasswordsStorage _resetPasswordsStorage;
        private readonly AuthPropertiesStorage _authPropertiesStorage;
        private readonly PasswordsStorage _passwordsStorage;
        private readonly RefreshTokensStorage refreshTokensStorage;
        private readonly IMediator _mediator;
        private readonly RequestContext requestContext;
        private readonly ILogger<ConfirmResetPasswordCommandHandler> _logger;

        private const int ExpDaysRefreshToken = 9999;


        public ConfirmResetPasswordCommandHandler(ResetPasswordsStorage resetPasswordsStorage, AuthPropertiesStorage authPropertiesStorage,
            PasswordsStorage passwordsStorage, RefreshTokensStorage refreshTokensStorage, IMediator mediator, RequestContext requestContext,
            ILogger<ConfirmResetPasswordCommandHandler> logger)
        {
            _resetPasswordsStorage = resetPasswordsStorage;
            _authPropertiesStorage = authPropertiesStorage;
            _passwordsStorage = passwordsStorage;
            this.refreshTokensStorage = refreshTokensStorage;
            _mediator = mediator;
            this.requestContext = requestContext;
            _logger = logger;
        }

        public async Task<ConfirmResetPasswordResponse> Handle(ConfirmResetPasswordCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Начало подтверждения сброса пароля. ResetId: {ResetId}",
                request.ResetId
            );
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
            
            var resetPasswordInfo = await _resetPasswordsStorage.GetResetPassword(request.ResetId);

            if (resetPasswordInfo is null)
            {
                _logger.LogWarning("Reset ID {ResetId} не найден", request.ResetId);
                throw new ResetIdNotFoundException();
            }

            if (resetPasswordInfo.IsApproved)
            {
                _logger.LogWarning(
                    "Reset ID {ResetId} уже был использован для пользователя {UserId}",
                    request.ResetId,
                    resetPasswordInfo.UserId
                );
                throw new ResetIdHasIsApprovedException();
            }

            _logger.LogDebug(
                "Проверка OTP кода для пользователя {UserId}, тип OTP: {OtpType}",
                resetPasswordInfo.UserId,
                resetPasswordInfo.OtpType
            );

            if (resetPasswordInfo.OtpType == OtpType.Authenticator)
            {
                var otpSecret = await _authPropertiesStorage.GetOtpSecretKey(resetPasswordInfo.UserId);

                var totp = new Totp(Base32Encoding.ToBytes(otpSecret));

                var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

                if (!isValid)
                {
                    _logger.LogWarning(
                        "Неверный Authenticator OTP код для пользователя {UserId}",
                        resetPasswordInfo.UserId
                    );
                    throw new NotValidOtpCodeException();
                }

                _logger.LogDebug("Authenticator OTP код успешно проверен для пользователя {UserId}", resetPasswordInfo.UserId);
            }
            else
            {
                if (!string.Equals(request.OtpCode, request.OtpCode))
                {
                    _logger.LogWarning(
                        "Неверный Email OTP код для пользователя {UserId}",
                        resetPasswordInfo.UserId
                    );
                    throw new NotValidOtpCodeException();
                }

                _logger.LogDebug("Email OTP код успешно проверен для пользователя {UserId}", resetPasswordInfo.UserId);
            }

            _logger.LogDebug("Генерация refresh token для пользователя {UserId}", resetPasswordInfo.UserId);

            var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
            await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, resetPasswordInfo.UserId, requestContext.DeviceName, ExpDaysRefreshToken);

            var accessTokenResponse = await _mediator.Send(new CreateTokenCommand { RefreshToken = refreshTokenString }, cancellationToken);

            _logger.LogInformation(
                "Сброс пароля успешно подтвержден для пользователя {UserId}, устройство: {DeviceName}",
                resetPasswordInfo.UserId,
                requestContext.DeviceName
            );

            return new ConfirmResetPasswordResponse()
            {
                AccessToken = accessTokenResponse.AccessToken,
                RefreshToken = new Token
                {
                    ExpirationDate = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(ExpDaysRefreshToken)),
                    Value = refreshTokenString
                }
            };
        }
    }
}