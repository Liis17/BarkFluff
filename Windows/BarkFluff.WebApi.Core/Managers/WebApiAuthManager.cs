using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для настройки двухфакторной аутентификации.
    /// </summary>
    internal class WebApiAuthManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiAuthManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Запрашивает QR-код для настройки двухфакторной аутентификации (OTP) и возвращает его в виде base64 строки.
        /// Для <see cref="Proto.Identity.OtpTypeId.Email"/> сервер шлёт код письмом, поэтому QR и ручной код приходят пустыми.
        /// </summary>
        public async Task<(ErrorReturner error, string? qrBase64, string? justCode)> OtpReceipt(GlobalParam globalParam, Proto.Identity.OtpTypeId otpType = Proto.Identity.OtpTypeId.Authenticator)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await IdentityAC!.EnableOtpVerificationAsync(new Proto.Identity.EnableOtpVerificationRequest
                    {
                        OtpType = otpType
                    });

                    return (new ErrorReturner(true), response.OtpQr, response.OtpCode);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityRateLimitExceededException)
            {
                return (new ErrorReturner(false, "Слишком много запросов. Повторите попытку позже", errorResourceKey: "Error_TwoFactorRateLimited"), null, null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityLockoutException)
            {
                return (new ErrorReturner(false, "Настройка двухфакторной аутентификации временно заблокирована. Повторите попытку позже", errorResourceKey: "Error_TwoFactorLocked"), null, null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityProtectionUnavailableException)
            {
                return (new ErrorReturner(false, "Защита двухфакторной аутентификации временно недоступна. Повторите попытку позже", errorResourceKey: "Error_TwoFactorProtectionUnavailable"), null, null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка настройки двухфакторной аутентификации"), null, null);
            }
        }

        /// <summary>
        /// Подтверждает двухфакторную аутентификацию (OTP) с использованием предоставленного кода.
        /// </summary>
        public async Task<ErrorReturner> OtpAccept(GlobalParam globalParam, string code)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await IdentityAC!.ConfirmOtpVerificationAsync(new Proto.Identity.ConfirmOtpVerificationRequest
                    {
                        OtpCode = code
                    });

                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityRateLimitExceededException)
            {
                return new ErrorReturner(false, "Слишком много запросов. Повторите попытку позже", errorResourceKey: "Error_TwoFactorRateLimited");
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityLockoutException)
            {
                return new ErrorReturner(false, "Подтверждение двухфакторной аутентификации временно заблокировано. Повторите попытку позже", errorResourceKey: "Error_TwoFactorLocked");
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityProtectionUnavailableException)
            {
                return new ErrorReturner(false, "Защита двухфакторной аутентификации временно недоступна. Повторите попытку позже", errorResourceKey: "Error_TwoFactorProtectionUnavailable");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подтверждения двухфакторной аутентификации");
            }
        }

        /// <summary>
        /// Отключает метод двухфакторной аутентификации. Код требуется только для
        /// <see cref="Proto.Identity.OtpTypeId.Authenticator"/>; при отключении email передаётся пустая строка.
        /// </summary>
        public async Task<ErrorReturner> OtpDisable(GlobalParam globalParam, Proto.Identity.OtpTypeId otpType = Proto.Identity.OtpTypeId.Authenticator, string otpCode = "")
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await IdentityAC!.DisableOtpVerificationAsync(new Proto.Identity.DisableOtpVerificationRequest
                    {
                        OtpType = otpType,
                        OtpCode = otpCode
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityRateLimitExceededException)
            {
                return new ErrorReturner(false, "Слишком много запросов. Повторите попытку позже", errorResourceKey: "Error_TwoFactorRateLimited");
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityLockoutException)
            {
                return new ErrorReturner(false, "Отключение двухфакторной аутентификации временно заблокировано. Повторите попытку позже", errorResourceKey: "Error_TwoFactorLocked");
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityProtectionUnavailableException)
            {
                return new ErrorReturner(false, "Защита двухфакторной аутентификации временно недоступна. Повторите попытку позже", errorResourceKey: "Error_TwoFactorProtectionUnavailable");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка отключения двухфакторной аутентификации");
            }
        }

        public async Task<(ErrorReturner error, bool authenticatorEnabled, bool emailEnabled)> OtpStatus(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await IdentityAC!.ListOtpVerificationAsync(new Proto.Identity.ListOtpVerificationRequest { });
                    return (new ErrorReturner(true), response.AuthenticatorEnabled, response.EmailEnabled);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения статуса двухфакторной аутентификации"), false, false);
            }
        }
    }
}
