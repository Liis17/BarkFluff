using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для сброса пароля и установки нового пароля.
    /// </summary>
    internal class WebApiPasswordManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiPasswordManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Устанавливает новый пароль для пользователя. Старый пароль обязателен, только если пароль уже
        /// установлен: после сброса через код хеш очищен, и <paramref name="oldPassword"/> остаётся пустым.
        /// </summary>
        public async Task<ErrorReturner> SetPassword(string newPassword, GlobalParam globalParam, string oldPassword = "")
        {
            if (globalParam == null)
                return new ErrorReturner(false, "Параметры приложения не могут быть null");

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await IdentityAC!.SetPasswordAsync(new Proto.Identity.SetPasswordRequest { Password = newPassword, OldPassword = oldPassword });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidLoginOrPasswordException)
            {
                return new ErrorReturner(false, "Неверный логин или пароль");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Неизвестная ошибка при установке пароля.");
            }
        }

        /// <summary>
        /// Вызывает сброс пароля для пользователя по электронной почте или имени пользователя.
        /// </summary>
        public async Task<(ErrorReturner error, string? resetId)> ResetPassword(string email, string username, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    if (!string.IsNullOrEmpty(email))
                    {
                        var resetPassword = await IdentityAC!.ResetPasswordAsync(new Proto.Identity.ResetPasswordRequest
                        {
                            OtpType = Proto.Identity.OtpTypeId.Email,
                            Email = email,
                        });
                        return (new ErrorReturner(true), resetPassword.ResetId);
                    }
                    else if (!string.IsNullOrEmpty(username))
                    {
                        var resetPassword = await IdentityAC!.ResetPasswordAsync(new Proto.Identity.ResetPasswordRequest
                        {
                            OtpType = Proto.Identity.OtpTypeId.Email,
                            Username = username,
                        });
                        return (new ErrorReturner(true), resetPassword.ResetId);
                    }

                    return (new ErrorReturner(false), null);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.NotSetUsernameOrEmailException)
            {
                return (new ErrorReturner(false, "Не указаны имя пользователя или электронная почта."), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameOrEmailIsEmptyException)
            {
                return (new ErrorReturner(false, "Имя пользователя или электронная почта не могут быть пустыми."), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidLoginOrPasswordException)
            {
                return (new ErrorReturner(false, "Неверный логин или пароль."), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UserNotFoundException)
            {
                return (new ErrorReturner(false, "Пользователь не найден."), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityRateLimitExceededException)
            {
                return (new ErrorReturner(false, "Слишком много запросов. Повторите попытку позже", errorResourceKey: "Error_PasswordResetRateLimited"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityLockoutException)
            {
                return (new ErrorReturner(false, "Сброс пароля временно заблокирован. Повторите попытку позже", errorResourceKey: "Error_PasswordResetLocked"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityProtectionUnavailableException)
            {
                return (new ErrorReturner(false, "Защита сброса пароля временно недоступна. Повторите попытку позже", errorResourceKey: "Error_PasswordResetProtectionUnavailable"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка сброса пароля"), null);
            }
        }

        /// <summary>
        /// Выполняет подтверждение кода сброса пароля по resetId и возвращает новый токен обновления.
        /// </summary>
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? refreshToken)> ConfirmResetCode(string resetId, string otpCode, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await IdentityAC!.ConfirmResetPasswordAsync(new Proto.Identity.ConfirmResetPasswordRequest { ResetId = resetId, OtpCode = otpCode });
                    return (new ErrorReturner(true), response.RefreshToken);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeExpiredException)
            {
                return (new ErrorReturner(false, "Код подтверждения истек."), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeIncorrectException)
            {
                return (new ErrorReturner(false, "Неверный код подтверждения."), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityRateLimitExceededException)
            {
                return (new ErrorReturner(false, "Слишком много запросов. Повторите попытку позже", errorResourceKey: "Error_PasswordResetRateLimited"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityLockoutException)
            {
                return (new ErrorReturner(false, "Код сброса временно заблокирован. Повторите попытку позже", errorResourceKey: "Error_PasswordResetLocked"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.IdentityProtectionUnavailableException)
            {
                return (new ErrorReturner(false, "Защита сброса пароля временно недоступна. Повторите попытку позже", errorResourceKey: "Error_PasswordResetProtectionUnavailable"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка подтверждения кода сброса пароля"), null);
            }
        }
    }
}
