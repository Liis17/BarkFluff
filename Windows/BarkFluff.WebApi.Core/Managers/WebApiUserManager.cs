using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Google.Protobuf.WellKnownTypes;

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
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameInvalidFormatException)
            {
                return new ErrorReturner(false, "Имя пользователя имеет недопустимый формат: разрешены латинские буквы, цифры и подчёркивание, длина от 3 до 32 символов");
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
                    var getUser = await UsersAC!.CheckExistEmailAsync(new Proto.Users.CheckExistEmailRequest { Email = email.ToLowerInvariant() });
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
                    var getUser = await UsersAC!.CheckExistUsernameAsync(new Proto.Users.CheckExistUsernameRequest { Username = username.ToLowerInvariant() });
                    return (new ErrorReturner(true), getUser.Exist);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтвержден."), false);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, "Ошибка проверки имени пользователя"), false);
            }
        }

        /// <summary>
        /// Возвращает список активных сессий пользователя с информацией об устройствах.
        /// </summary>
        public async Task<(ErrorReturner error, List<Proto.Identity.GetActiveSessionsResponse.Types.Session>? sessions)> GetDevicesList(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await IdentityAC!.GetActiveSessionsAsync(new Proto.Identity.GetActiveSessionsRequest { });
                    var sessions = response.Sessions.ToList();
                    return (new ErrorReturner(true), sessions);
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
        /// Возвращает текущее устройство.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Users.Device? device)> GetCurrentDevice(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.GetCurrentDeviceAsync(new Proto.Users.GetCurrentDeviceRequest { });
                    return (new ErrorReturner(true), response.Device);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidRefreshTokenException)
            {
                return (new ErrorReturner(false, "Неверный токен обновления."), null);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, "Ошибка получения текущего устройства"), null);
            }
        }

        /// <summary>
        /// Переименовывает устройство по его идентификатору.
        /// </summary>
        public async Task<ErrorReturner> RenameDevice(string deviceId, string customName, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.RenameDeviceAsync(new Proto.Users.RenameDeviceRequest
                    {
                        DeviceId = deviceId,
                        CustomName = customName
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка переименования устройства");
            }
        }

        /// <summary>
        /// Отключить или включить уведомления конкретного чата.
        /// mutedUntil = null при muted=true означает «навсегда».
        /// </summary>
        public async Task<ErrorReturner> SetChatMuted(string chatId, bool muted, GlobalParam globalParam, DateTime? mutedUntil = null)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var request = new Proto.Users.SetChatMutedRequest
                    {
                        ChatId = chatId,
                        Muted = muted
                    };
                    if (mutedUntil.HasValue)
                        request.MutedUntil = Timestamp.FromDateTime(mutedUntil.Value.ToUniversalTime());

                    await UsersAC!.SetChatMutedAsync(request);
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения уведомлений чата");
            }
        }

        /// <summary>
        /// Список чатов с активным mute у текущего пользователя.
        /// </summary>
        public async Task<(ErrorReturner error, List<Proto.Users.MutedChat>? chats)> GetMutedChats(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.GetMutedChatsAsync(new Proto.Users.GetMutedChatsRequest());
                    return (new ErrorReturner(true), response.Chats.ToList());
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения списка отключённых чатов"), null);
            }
        }

        /// <summary>
        /// Сохранить Firebase-токен текущего устройства для push-уведомлений.
        /// </summary>
        public async Task<ErrorReturner> SetFirebaseToken(string firebaseToken, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.SetFirebaseTokenAsync(new Proto.Users.SetFirebaseTokenRequest
                    {
                        FirebaseToken = firebaseToken
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка сохранения токена уведомлений");
            }
        }

        /// <summary>
        /// Найти пользователя другой ноды по федеративному идентификатору «@username:servername».
        /// Возвращает found=false, если такого пользователя нет или федерация не настроена.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Users.ResolveFederatedUserResponse? user)> ResolveFederatedUser(string fid, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.ResolveFederatedUserAsync(new Proto.Users.ResolveFederatedUserRequest { Fid = fid });
                    return ((ErrorReturner, Proto.Users.ResolveFederatedUserResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Federation.FederationNotConfiguredException)
            {
                return (new ErrorReturner(false, "Федерация не настроена на этом сервере"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Federation.InvalidServernameException)
            {
                return (new ErrorReturner(false, "Неверный формат федеративного идентификатора"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Federation.FederationServerBlockedException)
            {
                return (new ErrorReturner(false, "Сервер собеседника заблокирован"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка поиска пользователя другого сервера"), null);
            }
        }

        /// <summary>
        /// Завершает текущую сессию на сервере: refresh-токен этого устройства отзывается.
        /// Локальные токены и авто-обновление гасит вызывающая сторона.
        /// </summary>
        public async Task<ErrorReturner> Logout(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await IdentityAC!.LogoutAsync(new Proto.Identity.LogoutRequest());
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.SessionNotFoundException)
            {
                // Сессии уже нет — с точки зрения клиента выход состоялся.
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка выхода из аккаунта");
            }
        }

        /// <summary>
        /// Удаляет активную сессию по идентификатору устройства.
        /// </summary>
        public async Task<ErrorReturner> RemoveActiveSession(string deviceId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await IdentityAC!.RemoveActiveSessionAsync(new Proto.Identity.RemoveActiveSessionRequest
                    {
                        DeviceId = deviceId
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.SessionNotFoundException)
            {
                return new ErrorReturner(false, "Сессия не найдена.");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка удаления сессии");
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

        public async Task<ErrorReturner> ChangeName(string firstName, string lastName, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.ChangeNameAsync(new Proto.Users.ChangeNameRequest
                    {
                        FirstName = firstName,
                        LastName = lastName
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return new ErrorReturner(false, "Пользователь не подтвержден. Вы не можете изменить имя до подтверждения аккаунта.");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения имени");
            }
        }

        public async Task<(ErrorReturner error, List<Proto.Users.Device>? devices)> GetDevices(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.GetDevicesAsync(new Proto.Users.GetDevicesRequest { });
                    return (new ErrorReturner(true), response.Devices.ToList());
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения списка устройств"), null);
            }
        }

        public async Task<(ErrorReturner error, Proto.Users.PrivacySettings? settings)> GetPrivacySettings(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.GetPrivacySettingsAsync(new Proto.Users.GetPrivacySettingsRequest { });
                    return (new ErrorReturner(true), response.Settings);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения настроек приватности"), null);
            }
        }

        public async Task<ErrorReturner> UpdatePrivacySettings(Proto.Users.PrivacySettings settings, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.UpdatePrivacySettingsAsync(new Proto.Users.UpdatePrivacySettingsRequest
                    {
                        Settings = settings
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка обновления настроек приватности");
            }
        }

        public async Task<ErrorReturner> SetNotificationsEnabled(bool enabled, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.SetNotificationsEnabledAsync(new Proto.Users.SetNotificationsEnabledRequest
                    {
                        Enabled = enabled
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка изменения настроек уведомлений");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Персонализация
        // ──────────────────────────────────────────────────────────────

        public async Task<(ErrorReturner error, Proto.Users.UserPersonalizationData? data)> GetPersonalization(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.GetPersonalizationAsync(new Proto.Users.GetPersonalizationRequest());
                    return (new ErrorReturner(true), response.Personalization);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения персонализации"), null);
            }
        }

        public async Task<ErrorReturner> UpdatePersonalization(Proto.Users.UserPersonalizationData data, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.UpdatePersonalizationAsync(new Proto.Users.UpdatePersonalizationRequest
                    {
                        Personalization = data
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка обновления персонализации");
            }
        }

        /// <summary>
        /// Получить FileId постера профиля. Пустая строка если постер не задан.
        /// </summary>
        public async Task<(ErrorReturner error, string fileId)> GetProfilePoster(GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.GetProfilePosterAsync(new Proto.Users.GetProfilePosterRequest());
                    return (new ErrorReturner(true), response.ProfilePosterFileId ?? string.Empty);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения постера профиля"), string.Empty);
            }
        }

        /// <summary>
        /// Установить (или удалить — пустая строка) постер профиля.
        /// </summary>
        public async Task<ErrorReturner> SetProfilePoster(string fileId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await UsersAC!.SetProfilePosterAsync(new Proto.Users.SetProfilePosterRequest
                    {
                        ProfilePosterFileId = fileId ?? string.Empty
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка обновления постера профиля");
            }
        }
    }
}
