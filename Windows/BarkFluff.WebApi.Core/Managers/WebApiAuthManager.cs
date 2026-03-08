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
        /// </summary>
        public async Task<(ErrorReturner error, string? qrBase64, string? justCode)> OtpReceipt(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await IdentityAC!.EnableOtpVerificationAsync(new Proto.Identity.EnableOtpVerificationRequest
                    {
                        OtpType = Proto.Identity.OtpTypeId.Authenticator
                    });

                    return (new ErrorReturner(true), response.OtpQr, response.OtpCode);
                }, globalParam);
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
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подтверждения двухфакторной аутентификации");
            }
        }
    }
}
