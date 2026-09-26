using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Identity.Security;
using BarkFluff.Shared.Exceptions.Identity;

using MediatR;

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
        private readonly MetricsCollector _metrics;
        private readonly ILogger<ConfirmResetPasswordCommandHandler> _logger;
        private readonly AuthenticationStore? _authenticationStore;

        private const int ExpDaysRefreshToken = 9999;


        public ConfirmResetPasswordCommandHandler(ResetPasswordsStorage resetPasswordsStorage, AuthPropertiesStorage authPropertiesStorage,
            PasswordsStorage passwordsStorage, RefreshTokensStorage refreshTokensStorage, IMediator mediator, RequestContext requestContext,
            MetricsCollector metrics, ILogger<ConfirmResetPasswordCommandHandler> logger,
            IIdentityAbuseGuard abuseGuard, AuthenticationStore? authenticationStore = null)
        {
            _resetPasswordsStorage = resetPasswordsStorage;
            _authPropertiesStorage = authPropertiesStorage;
            _passwordsStorage = passwordsStorage;
            this.refreshTokensStorage = refreshTokensStorage;
            _mediator = mediator;
            this.requestContext = requestContext;
            _metrics = metrics;
            _logger = logger;
            _abuseGuard = abuseGuard;
            _authenticationStore = authenticationStore;
        }

        private readonly IIdentityAbuseGuard _abuseGuard;

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

            await _abuseGuard.EnsureCodeAllowedAsync(
                IdentityCodeKind.PasswordReset,
                request.ResetId,
                cancellationToken);

            var initialResetInfo = await _resetPasswordsStorage.GetResetPassword(request.ResetId);

            if (initialResetInfo is null)
            {
                _metrics.Increment("password_reset_confirmation_failed");
                _metrics.Increment("password_reset_confirmation_failed_not_found");
                _logger.LogWarning("Reset ID {ResetId} не найден", request.ResetId);
                throw new ResetIdNotFoundException();
            }

            async Task<ConfirmResetPasswordResponse> ConfirmUnderPolicyLock()
            {
                var resetPasswordInfo = await _resetPasswordsStorage.GetResetPassword(request.ResetId);
                if (resetPasswordInfo is null || resetPasswordInfo.UserId != initialResetInfo.UserId)
                    throw new ResetIdNotFoundException();

                if (resetPasswordInfo.IsApproved)
                {
                    _metrics.Increment("password_reset_confirmation_failed");
                    _metrics.Increment("password_reset_confirmation_failed_already_used");
                    _logger.LogWarning(
                        "Reset ID {ResetId} уже был использован для пользователя {UserId}",
                        request.ResetId,
                        resetPasswordInfo.UserId
                    );
                    throw new ResetIdHasIsApprovedException();
                }

                var policy = await _authPropertiesStorage.GetUserAuthProperties(resetPasswordInfo.UserId);
                if (AuthenticationPolicy.Mode(policy) != AuthLoginMode.Password)
                    throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.FailedPrecondition,
                        "Use web password recovery; the configured second factor is still required"));

                if (resetPasswordInfo.ExpiresAt < DateTime.UtcNow)
                {
                    _metrics.Increment("password_reset_confirmation_failed");
                    _metrics.Increment("password_reset_confirmation_failed_expired");
                    _logger.LogWarning(
                        "Reset ID {ResetId} истёк для пользователя {UserId}. ExpiresAt: {ExpiresAt}",
                        request.ResetId,
                        resetPasswordInfo.UserId,
                        resetPasswordInfo.ExpiresAt
                    );
                    throw new ResetIdExpiredException();
                }

                _logger.LogDebug(
                    "Проверка OTP кода для пользователя {UserId}, тип OTP: {OtpType}",
                    resetPasswordInfo.UserId,
                    resetPasswordInfo.OtpType
                );

                if (string.IsNullOrWhiteSpace(request.OtpCode))
                    throw new OtpCodeNeedException();

                if (resetPasswordInfo.OtpType == OtpType.Authenticator)
                {
                    var otpSecret = await _authPropertiesStorage.GetOtpSecretKey(resetPasswordInfo.UserId);

                    var totp = new Totp(Base32Encoding.ToBytes(otpSecret));

                    var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

                    if (!isValid)
                    {
                        var failure = await _abuseGuard.RegisterCodeFailureAsync(
                            IdentityCodeKind.PasswordReset,
                            request.ResetId,
                            resetPasswordInfo.ExpiresAt,
                            cancellationToken);
                        await _abuseGuard.DelayAfterFailureAsync(failure.Attempts, cancellationToken);

                        _metrics.Increment("password_reset_confirmation_failed");
                        _metrics.Increment("otp_authenticator_failed");
                        _logger.LogWarning(
                            "Неверный Authenticator OTP код для пользователя {UserId}",
                            resetPasswordInfo.UserId
                        );

                        if (failure.Locked)
                        {
                            await _resetPasswordsStorage.InvalidateResetPassword(request.ResetId);
                            throw new IdentityLockoutException();
                        }

                        throw new NotValidOtpCodeException();
                    }

                    _metrics.Increment("otp_authenticator_verified");
                    _logger.LogDebug("Authenticator OTP код успешно проверен для пользователя {UserId}", resetPasswordInfo.UserId);
                }
                else
                {
                    if (!string.Equals(resetPasswordInfo.OtpCode, request.OtpCode, StringComparison.Ordinal))
                    {
                        var failure = await _abuseGuard.RegisterCodeFailureAsync(
                            IdentityCodeKind.PasswordReset,
                            request.ResetId,
                            resetPasswordInfo.ExpiresAt,
                            cancellationToken);
                        await _abuseGuard.DelayAfterFailureAsync(failure.Attempts, cancellationToken);

                        _metrics.Increment("password_reset_confirmation_failed");
                        _metrics.Increment("otp_email_failed");
                        _logger.LogWarning(
                            "Неверный Email OTP код для пользователя {UserId}",
                            resetPasswordInfo.UserId
                        );

                        if (failure.Locked)
                        {
                            await _resetPasswordsStorage.InvalidateResetPassword(request.ResetId);
                            throw new IdentityLockoutException();
                        }

                        throw new NotValidOtpCodeException();
                    }

                    _metrics.Increment("otp_email_verified");
                    _logger.LogDebug("Email OTP код успешно проверен для пользователя {UserId}", resetPasswordInfo.UserId);
                }

                await _abuseGuard.ClearCodeFailuresAsync(
                    IdentityCodeKind.PasswordReset,
                    request.ResetId,
                    cancellationToken);

                _logger.LogDebug("Генерация refresh token для пользователя {UserId}", resetPasswordInfo.UserId);

                var deviceId = Guid.TryParse(requestContext.DeviceId, out var parsedDeviceId)
                    ? parsedDeviceId.ToString()
                    : Guid.NewGuid().ToString();

                var refreshTokenString = RefreshTokenGenerator.GenerateRefreshToken();
                await refreshTokensStorage.CreateNewRefreshToken(refreshTokenString, resetPasswordInfo.UserId, deviceId, ExpDaysRefreshToken);

                var accessTokenResponse = await _mediator.Send(new CreateTokenCommand { RefreshToken = refreshTokenString }, cancellationToken);

                // Отметить запрос сброса как использованный
                _logger.LogDebug("Отметка запроса сброса {ResetId} как использованного", request.ResetId);
                await _resetPasswordsStorage.SetApproved(request.ResetId);

                // Очистить хеш пароля для возможности установки нового без старого
                _logger.LogDebug("Очистка хеша пароля для пользователя {UserId}", resetPasswordInfo.UserId);
                await _passwordsStorage.ClearUserPasswordHash(resetPasswordInfo.UserId);

                _metrics.Increment("password_resets_confirmed");
                _metrics.Increment("sessions_created");

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

            if (_authenticationStore is null)
                return await ConfirmUnderPolicyLock();

            return await _authenticationStore.ForUserPreserving(initialResetInfo.UserId, ConfirmUnderPolicyLock,
                cancellationToken, typeof(OtpCodeNeedException), typeof(IdentityLockoutException));
        }
    }
}
