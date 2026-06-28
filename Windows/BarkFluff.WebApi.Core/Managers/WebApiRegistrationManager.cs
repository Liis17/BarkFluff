using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для регистрации пользователей.
    /// </summary>
    internal class WebApiRegistrationManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiRegistrationManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Вызывает создание аккаунта с предоставленными данными.
        /// </summary>
        public async Task<(ErrorReturner error, string? userid)> CreateAccount(string firstName, string lastName, string email, string login, GlobalParam global)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var createAccount = await IdentityAC!.CreateAccountAsync(new Proto.Identity.CreateAccountRequest
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Username = login
                    });
                    return (new ErrorReturner(true), createAccount.CodeId);
                }, global);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameInvalidFormatException)
            {
                return (new ErrorReturner(false, "Имя пользователя имеет недопустимый формат: разрешены латинские буквы, цифры и подчёркивание, длина от 3 до 32 символов"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameExistException)
            {
                return (new ErrorReturner(false, "Имя пользователя уже существует"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.EmailExistException)
            {
                return (new ErrorReturner(false, "Почта уже существует"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameOrEmailIsEmptyException)
            {
                return (new ErrorReturner(false, "Имя пользователя или почта не могут быть пустыми"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.NotSetUsernameOrEmailException)
            {
                return (new ErrorReturner(false, "Имя пользователя или почта не установлены"), null);
            }
        }

        /// <summary>
        /// Подтверждает аккаунт по коду и значению кода подтверждения.
        /// </summary>
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? RefreshToken)> ConfirmAccount(string code, string verifyCode, GlobalParam global)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var confirmAccount = await IdentityAC!.ConfirmAccountAsync(new Proto.Identity.ConfirmAccountRequest
                    {
                        CodeId = code,
                        CodeValue = verifyCode
                    });
                    return (new ErrorReturner(true), confirmAccount.RefreshToken);
                }, global);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeExpiredException)
            {
                return (new ErrorReturner(false, "Код подтверждения больше недействителен"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeIncorrectException)
            {
                return (new ErrorReturner(false, "Неверный код подтверждения"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeNotFoundException)
            {
                return (new ErrorReturner(false, "Код подтверждения не найден"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка подтверждения аккаунта"), null);
            }
        }
    }
}
