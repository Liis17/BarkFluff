using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для работы с пользователями.
    /// </summary>
    internal class WebApiUserManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiUserManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Изменяет биографию пользователя.
        /// </summary>
        public async Task<ErrorReturner> ChangeBio(string bio, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.ChangeBioAsync(new Proto.Users.ChangeBioRequest { Bio = bio });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return new ErrorReturner(false, "Пользователь не подтвержден. Вы не можете изменить биографию до подтверждения аккаунта.");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения биографии");
            }
        }

        public async Task<ErrorReturner> ChangeUsername(string username, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.ChangeUsernameAsync(new Proto.Users.ChangeUsernameRequest { Username = username });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return new ErrorReturner(false, "Пользователь не подтвержден. Вы не можете изменить имя пользователя до подтверждения аккаунта.");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения имени пользователя");
            }
        }

        /// <summary>
        /// Проверяет, существует ли адрес электронной почты в системе.
        /// </summary>
        public async Task<(ErrorReturner error, bool exists)> CheckEmail(string email, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC!.CheckExistEmailAsync(new Proto.Users.CheckExistEmailRequest { Email = email.ToLower() });
                    return (new ErrorReturner(true), getUser.Exist);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтвержден."), false);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка проверки почты"), false);
            }
        }

        /// <summary>
        /// Проверяет, существует ли имя пользователя в системе.
        /// </summary>
        public async Task<(ErrorReturner error, bool exists)> CheckUsername(string username, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC!.CheckExistUsernameAsync(new Proto.Users.CheckExistUsernameRequest { Username = username.ToLower() });
                    return (new ErrorReturner(true), getUser.Exist);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтвержден."), false);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка проверки имени пользователя"), false);
            }
        }

        /// <summary>
        /// Возвращает список активных устройств пользователя.
        /// </summary>
        public async Task<(ErrorReturner error, List<string>? devicesList)> GetDevicesList(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await IdentityAC!.GetActiveSessionsAsync(new Proto.Identity.GetActiveSessionsRequest { });
                    var devicesList = response.Sessions
                        .Select(session => session.DeviceName ?? "Неизвестное устройство")
                        .ToList();
                    return (new ErrorReturner(true), devicesList);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidRefreshTokenException)
            {
                return (new ErrorReturner(false, "Неверный токен обновления."), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения списка устройств"), null);
            }
        }

        /// <summary>
        /// Получает ссылку на аватар пользователя по его ID.
        /// </summary>
        public async Task<(ErrorReturner, string?)> GetUserAvatar(GlobalParam globalParam, long userId = 0)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var getLinkUpload = await GetUserData(globalParam, userId);
                    return (new ErrorReturner(true), getLinkUpload.Data?.ProfilePictureUrl);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
            {
                return (new ErrorReturner(false, "Переданный file-id содержит файл не с типом Изображение профиля пользователя"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтвержден"), null);
            }
        }

        /// <summary>
        /// Получает данные пользователя по его ID.
        /// </summary>
        public async Task<(ErrorReturner Error, UserData? Data)> GetUserData(GlobalParam globalParam, long userId = 0)
        {
            if (globalParam == null)
                return (new ErrorReturner(false, "Параметры приложения не могут быть null"), null);

            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC!.GetUserAsync(new Proto.Users.GetUserRequest { UserId = userId });

                    return (new ErrorReturner(true), new UserData
                    {
                        FirstName = getUser.User.FirstName,
                        LastName = getUser.User.LastName,
                        Username = getUser.User.Username,
                        RegistrationDate = getUser.User.RegistrationDate.ToDateTime(),
                        Id = getUser.User.Id,
                        ProfilePictureUrl = getUser.User.ProfilePicture,
                        Description = getUser.User.Bio,
                        ProfilePicturePreviewUrl = getUser.User.ProfilePicturePreview
                    });
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
            {
                return (new ErrorReturner(false, "Переданный file-id содержит файл не с типом Изображение профиля пользователя."), null);
            }
        }

        /// <summary>
        /// Выполняет авторизацию пользователя с использованием электронной почты или имени пользователя, пароля и кода двухфакторной аутентификации (OTP).
        /// </summary>
        public async Task<(ErrorReturner Error, Proto.Identity.Token? refreshToken, Proto.Identity.Token? accessToken, bool getMeOtpCode)> Authorizations(string _email, string _username, string _password, string _otpCode, GlobalParam global)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await IdentityAC!.AuthAsync(new Proto.Identity.AuthRequest
                    {
                        Email = _email,
                        Username = _username,
                        Password = _password,
                        OtpCode = _otpCode
                    });
                    return (new ErrorReturner(true), response.RefreshToken, response.AccessToken, false);
                }, global);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidLoginOrPasswordException)
            {
                return (new ErrorReturner(false, "Неверный логин или пароль"), null, null, false);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.NotSetUsernameOrEmailException)
            {
                return (new ErrorReturner(false, "Не передан логин или почта"), null, null, false);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameOrEmailIsEmptyException)
            {
                return (new ErrorReturner(false, "Логин или почта пустые"), null, null, false);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UserNotFoundException)
            {
                return (new ErrorReturner(false, "Пользователь не найден"), null, null, false);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.OtpCodeNeedException)
            {
                return (new ErrorReturner(false, "Необходимо ввести код двухфакторной аутентификации (OTP)"), null, null, true);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.OtpNotCreatedException)
            {
                return (new ErrorReturner(false, "Двухфакторная аутентификация не настроена. Пожалуйста, настройте её в настройках аккаунта."), null, null, false);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.NotValidOtpCodeException)
            {
                return (new ErrorReturner(false, "Неверный код двухфакторной аутентификации (OTP)"), null, null, true);
            }
        }

        /// <summary>
        /// Получает баджи пользователя с сервера
        /// </summary>
        /// <param name="globalParam">Глобальные параметры</param>
        /// <param name="userId">ID пользователя (0 для текущего пользователя)</param>
        /// <param name="limit">Лимит количества баджей (null для всех, 3 для профиля)</param>
        /// <returns>Список баджей пользователя, отсортированных по приоритету</returns>
        public async Task<(ErrorReturner error, List<Proto.Users.UserBadge>? badges)> GetUserBadges(
            GlobalParam globalParam,
            long userId = 0,
            int? limit = null)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new Proto.Users.GetUserBadgesRequest
                    {
                        UserId = userId
                    };

                    if (limit.HasValue)
                    {
                        request.Limit = limit.Value;
                    }

                    var response = await UsersAC!.GetUserBadgesAsync(request);
                    return (new ErrorReturner(true), response.Badges.ToList());
                }, globalParam);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, $"Ошибка получения баджей пользователя: {ex.Message}"), null);
            }
        }
    }
}
