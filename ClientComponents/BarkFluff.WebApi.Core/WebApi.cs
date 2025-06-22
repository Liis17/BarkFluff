using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace BarkFluff.WebApi.Core
{
#pragma warning disable CS8619
    public class WebApi
    {
        #region ApiClients
        private BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC;
        private BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC;
        private BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC;
        private BarkFluff.Proto.Files.FilesApi.FilesApiClient? FilesAC;
        #endregion

        #region gRPC Channels
        private GrpcChannel? BeaconChannel;
        private GrpcChannel? UserChannel;
        private GrpcChannel? IdentityChannel;
        private GrpcChannel? FilesChannel;
        #endregion

        #region На всякий случай, возможно переиспользование
        private struct InitializationParams
        {
            public string DeviceName { get; set; }
            public string Os { get; set; }
            public string AppName { get; set; }
            public string AppVersion { get; set; }
            public string Ip { get; set; }
        }
        private InitializationParams? _initParams;
        #endregion

        #region Создание клиентов (AC - Api Client)
        /// <summary>
        /// Вызывает создание только gRPC клиента для работы с Beacon API на сервере.
        /// </summary>
        /// <param name="gParam">Параметры приложения</param>
        public void CreateOnlyBeaconAC(GlobalParam gParam)
        {
            CreateBeaconAC(gParam);
        }

        /// <summary>
        /// Вызывает создание gRPC клиентов для работы с API сервера.
        /// </summary>
        /// <param name="gParam">Параметры приложения</param>
        /// <param name="deviceName">Имя устройства на котором запущен клиент</param>
        /// <param name="os">Название операционной системы на котором запущен клиент</param>
        /// <param name="appName">Имя клиента (приложения)</param>
        /// <param name="appVersion">Версия приложения</param>
        /// <param name="ip">IP адрес устройства клиента</param>
        public void CreateAC(GlobalParam gParam, string deviceName, string os, string appName, string appVersion, string ip)
        {
            _initParams = new InitializationParams
            {
                DeviceName = deviceName,
                Os = os,
                AppName = appName,
                AppVersion = appVersion,
                Ip = ip
            };

            CreateUsersAC(gParam);
            CreateBeaconAC(gParam);
            CreateIdentityAC(gParam);
            CreateFilesAC(gParam);
            AddInterceptor(gParam, deviceName, os, appName, appVersion, ip);
        }

        /// <summary>
        /// Создает gRPC клиент для работы с пользователями на сервере.
        /// </summary>
        /// <param name="_gParam">Параметры приложения</param>
        private void CreateUsersAC(GlobalParam _gParam)
        {
            UsersAC = null!;
            UserChannel = null!;
            _gParam.SocketUsers = EnsureHttpPrefix(_gParam.SocketUsers);
            UserChannel = GrpcChannel.ForAddress(_gParam.SocketUsers);
            UsersAC = new BarkFluff.Proto.Users.UsersApi.UsersApiClient(UserChannel);
        }

        /// <summary>
        /// Создает gRPC клиент для работы с Beacon API на сервере.
        /// </summary>
        /// <param name="_gParam">Параметры приложения</param>
        private void CreateBeaconAC(GlobalParam _gParam)
        {
            BeaconAC = null!;
            BeaconChannel = null!;
            _gParam.SocketBeacon = EnsureHttpPrefix(_gParam.SocketBeacon);
            BeaconChannel = GrpcChannel.ForAddress(_gParam.SocketBeacon);
            BeaconAC = new BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient(BeaconChannel);
        }

        /// <summary>
        /// Создает gRPC клиент для работы с идентификацией на сервере.
        /// </summary>
        /// <param name="_gParam">Параметры приложения</param>
        private void CreateIdentityAC(GlobalParam _gParam)
        {
            IdentityAC = null!;
            IdentityChannel = null!;
            _gParam.SocketIdentity = EnsureHttpPrefix(_gParam.SocketIdentity);
            IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);
            IdentityAC = new BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient(IdentityChannel);
        }

        /// <summary>
        /// Создает gRPC клиент для работы с файлами на сервере.
        /// </summary>
        /// <param name="_gParam">Параметры приложения</param>
        private void CreateFilesAC(GlobalParam _gParam)
        {
            FilesAC = null!;
            FilesChannel = null!;
            _gParam.SocketFiles = EnsureHttpPrefix(_gParam.SocketFiles);
            FilesChannel = GrpcChannel.ForAddress(_gParam.SocketFiles);
            FilesAC = new BarkFluff.Proto.Files.FilesApi.FilesApiClient(FilesChannel);
        }

        /// <summary>
        ///  Добавляет перехватчики для аутентификации и авторизации в gRPC каналы.
        /// </summary>
        /// <param name="_gParam">Параметры приложения</param>
        /// <param name="_deviceName">Имя устройства на котором запущен клиент</param>
        /// <param name="os">Название операционной системы на котором запущен клиент</param>
        /// <param name="appName">Имя клиента (приложения)</param>
        /// <param name="appVersion">Версия приложения</param>
        /// <param name="ip">IP адрес устройства клиента</param>
        private void AddInterceptor(GlobalParam _gParam, string _deviceName, string os, string appName, string appVersion, string ip)
        {
            IdentityAC = null!;
            UsersAC = null!;
            FilesAC = null!;

            var token = string.Empty;
            if (_gParam.AccessToken != null)
            {
                token = _gParam.AccessToken.Value;
            }

            var deviceInterceptor = new Shared.Auth.XDeviceClientInterceptor(deviceName: _deviceName);
            var osInterceptor = new Shared.Auth.XOsClientInterceptor(os);
            var jwtInterceptor = new Shared.Auth.JwtClientInterceptor(token);
            var appInterceptor = new Shared.Auth.XAppClientInterceptor(appName, appVersion);
            var errorInterceptor = new Shared.Exceptions.Interceptors.ExceptionClientInterceptor();
            var ipInterceptor = new Shared.Auth.XIpClientInterceptor(ip);

            var identityInvoker = IdentityChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
            var userInvoker = UserChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
            var filesInvoker = FilesChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);

            IdentityAC = new BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient(identityInvoker);
            UsersAC = new BarkFluff.Proto.Users.UsersApi.UsersApiClient(userInvoker);
            FilesAC = new BarkFluff.Proto.Files.FilesApi.FilesApiClient(filesInvoker);
        }
        #endregion

        #region Работа с токенами и безопасными вызовами API

        /// <summary>
        /// Вызов API с обработкой возможных ошибок, связанных с токеном.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="globalParam">Параметры приложения</param>
        /// <param name="operation">Функция для выполнения API вызова</param>
        /// <param name="allowRetry">Разрешить повторный вызов при ошибке токена</param>
        /// <returns>Результат выполнения операции</returns>
        /// <exception cref="InvalidOperationException">Выбрасывается, если не удалось обновить токен</exception>
        private async Task<T> ExecuteWithTokenRefresh<T>(GlobalParam globalParam, Func<Task<T>> operation, bool allowRetry = true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (IsTokenRelatedError(ex) && allowRetry)
            {
                if (globalParam?.RefreshToken == null)
                {
                    throw new InvalidOperationException("Refresh token is not available for token renewal", ex);
                }

                if (_initParams == null)
                {
                    throw new InvalidOperationException("Initialization parameters are not available for client reinitialization", ex);
                }

                try
                {
                    await TokenUpdate(globalParam);

                    // Переинициализируем клиентов с новым токеном
                    AddInterceptor(globalParam, _initParams.Value.DeviceName, _initParams.Value.Os,
                                 _initParams.Value.AppName, _initParams.Value.AppVersion, _initParams.Value.Ip);

                    // Повторяем операцию (только один раз, чтобы избежать бесконечной рекурсии)
                    return await ExecuteWithTokenRefresh(globalParam, operation, allowRetry: false);
                }
                catch (Exception refreshEx)
                {
                    throw new InvalidOperationException("Failed to refresh token and retry operation", refreshEx);
                }
            }
        }

        /// <summary>
        /// Проверяет, является ли ошибка связанной с токеном доступа.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        private bool IsTokenRelatedError(Exception ex)
        {
            if (ex is RpcException rpcEx)
            {
                return rpcEx.StatusCode == StatusCode.Unauthenticated ||
                       rpcEx.StatusCode == StatusCode.PermissionDenied ||
                       (rpcEx.Status.Detail?.Contains("token", StringComparison.OrdinalIgnoreCase) == true) ||
                       (rpcEx.Status.Detail?.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) == true) ||
                       (rpcEx.Status.Detail?.Contains("expired", StringComparison.OrdinalIgnoreCase) == true);
            }

            // Можно добавить другие типы исключений при необходимости
            return false;
        }

        public async Task<TResponse> SafeCallAsync<TResponse>(Func<Task<TResponse>> apiCall, GlobalParam globalParam)
        {
            return await ExecuteWithTokenRefresh(globalParam, apiCall);
        }
        #endregion




        #region Обслуживание
        /// <summary>
        /// Добавляет http:// или https:// к URL, если он не начинается с них.
        /// </summary>
        /// <param name="_url">Ссылка которая должна будет иметь http:// на выходе</param>
        /// <returns>Возвращает строку с адресом которая будет начинаться на http://</returns>
        private string EnsureHttpPrefix(string _url)
        {
            return !_url.StartsWith("http://") && !_url.StartsWith("https://")
                   ? "http://" + _url
                   : _url;
        }
        #endregion



        /// <summary>
        /// Получает информацию о сервере
        /// </summary>
        /// <param name="param">Параметры приложения</param>
        /// <returns>Возвращает информацию о сервере</returns>
        public async Task<Proto.Beacon.GetServerInfoResponse> GetServerInfo(GlobalParam param)
        {
            return await SafeCallAsync(async () =>
            {
                var response = BeaconAC.GetServerInfo(new BarkFluff.Proto.Beacon.GetServerInfoRequest());
                param.ServerName = response.Name;
                param.ServerDescription = response.Description;
                param.SocketIdentity = EnsureHttpPrefix(response.Identity.Endpoint.Host + ":" + response.Identity.Endpoint.Port);
                param.SocketUsers = EnsureHttpPrefix(response.Users.Endpoint.Host + ":" + response.Users.Endpoint.Port);
                param.SocketFiles = EnsureHttpPrefix(response.Files.Endpoint.Host + ":" + response.Files.Endpoint.Port);
                param.Colors = new ClientColors()
                {
                    LiteHex = response.Color.LiteHex,
                    MainHex = response.Color.MainHex,
                    HardHex = response.Color.HardHex,
                };
                return response;
            }, param);
        }

        /// <summary>
        /// Обновляет токен доступа для приложения.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <returns>Возвращает новый токен доступа</returns>
        public async Task<string> TokenUpdate(GlobalParam globalParam)
        {
            try
            {
                var response = await IdentityAC.CreateTokenAsync(new BarkFluff.Proto.Identity.CreateTokenRequest { RefreshToken = globalParam.RefreshToken.Value });
                globalParam.AccessToken = response.AccessToken;
                return response.AccessToken.Value;
            }
            catch
            {

            }
            return "";
        }

        #region Работа с пользователями и аватарками
        /// <summary>
        /// Отправляет аватар пользователя на сервер в формате JPEG.
        /// </summary>
        /// <param name="jpegImageBytes">Картинка в виде байтов</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task UploadUserAvatarAsync(GlobalParam globalParam, byte[] jpegImageBytes)
        {
            try
            {
                await SafeCallAsync<bool>(async () =>
                {
                    var getLinkUpload = await FilesAC.GetUploadUrlAsync(new Proto.Files.GetUploadUrlRequest
                    {
                        FileType = Proto.Files.UploadFileType.UserAvatar
                    });

                    using var httpClient = new HttpClient();
                    using var formData = new MultipartFormDataContent();

                    var fileContent = new ByteArrayContent(jpegImageBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                    formData.Add(fileContent, "file", "avatar.jpg");

                    var response = await httpClient.PostAsync(getLinkUpload.Url, formData);
                    response.EnsureSuccessStatusCode();

                    try
                    {
                        var setAvatar = await UsersAC.SetProfilePictureAsync(new Proto.Users.SetProfilePictureRequest
                        {
                            FileId = getLinkUpload.FileId
                        });
                    }
                    catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
                    {
                        // обработка
                    }
                    catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
                    {
                        // обработка
                    }

                    return true;
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                // обработка
            }
        }

        /// <summary>
        /// Получает ссылку на аватар пользователя по его ID.
        /// </summary>
        /// <param name="userId">[НЕОБЯЗАТЕЛЬНО] ID пользователя, аватар которого нужно получить</param>
        /// <returns>Возвращает URL аватара или null, если аватар не найден</returns>
        public async Task<string> GetUserAvatar(GlobalParam globalParam, long userId = 0)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var getLinkUpload = await GetUserData(globalParam, userId);
                    return getLinkUpload.ProfilePictureUrl;
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                // обработка
            }

            return null;
        }
        #endregion

        /// <summary>
        /// Получает данные пользователя по его ID.
        /// </summary>
        /// <param name="userId">[НЕОБЯЗАТЕЛЬНО] ID пользователя, данные которого нужно получить</param>
        /// <returns>Объект данных пользователя</returns>
        public async Task<UserData> GetUserData(GlobalParam globalParam, long userId = 0)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC.GetUserAsync(new Proto.Users.GetUserRequest { UserId = userId });

                    return new UserData
                    {
                        FirstName = getUser.User.FirstName,
                        LastName = getUser.User.LastName,
                        Email = "Почта не установлена",
                        Username = getUser.User.Username,
                        RegistrationDate = getUser.User.RegistrationDate,
                        Id = getUser.User.Id,
                        ProfilePictureUrl = getUser.User.ProfilePicture,
                    };
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                // обработка
            }

            return null;
        }

        #region Настройка двухфакторной аутентификации

        /// <summary>
        /// Запрашивает QR-код для настройки двухфакторной аутентификации (OTP) и возвращает его в виде base64 строки.
        /// </summary>
        /// <returns>Кортеж, содержащий QR-код в формате base64 и код для ручного ввода.</returns>
        public async Task<(string qrBase64, string justCode)> OtpReceipt(GlobalParam globalParam)
        {
            return await SafeCallAsync(async () =>
            {
                var response = await IdentityAC.EnableOtpVerificationAsync(new Proto.Identity.EnableOtpVerificationRequest
                {
                    OtpType = Proto.Identity.OtpTypeId.Authenticator
                });

                return (response.OtpQr, response.OtpCode);
            }, globalParam);
        }

        /// <summary>
        /// Подтверждает двухфакторную аутентификацию (OTP) с использованием предоставленного кода.
        /// </summary>
        /// <param name="code">Код который необходимо ввести для подтверждения из Google Authenticator</param>
        public async Task OtpAccept(GlobalParam globalParam, string code)
        {
            await SafeCallAsync(async () =>
            {
                await IdentityAC.ConfirmOtpVerificationAsync(new Proto.Identity.ConfirmOtpVerificationRequest
                {
                    OtpCode = code
                });

                return true; // или `null`, если ты используешь `SafeCallAsync<object>`
            }, globalParam);
        }
        #endregion

        /// <summary>
        /// Вызывает создание аккаунта с предоставленными данными.
        /// </summary>
        /// <param name="firstName">Имя</param>
        /// <param name="lastName">Фамилия</param>
        /// <param name="email">Почта</param>
        /// <param name="login">Username</param>
        /// <param name="global">Глобальный параметр конфигурации</param>
        /// <returns>Кортеж, состоящий из статуса создания аккаунта и идентификатора кода</returns>
        public async Task<(bool, string)> CreateAccount(string firstName, string lastName, string email, string login, GlobalParam global)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var createAccount = await IdentityAC.CreateAccountAsync(new Proto.Identity.CreateAccountRequest
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Username = login
                    });
                    return (true, createAccount.CodeId);
                }, global);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameExistException)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Identity.EmailExistException)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameOrEmailIsEmptyException)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Identity.NotSetUsernameOrEmailException)
            {
                // обработка
            }
            return (false, null);
        }

        /// <summary>
        /// Подтверждает аккаунт по коду и значению кода подтверждения.
        /// </summary>
        /// <param name="code">Код подтверждения который получен при создании аккаунта</param>
        /// <param name="verifyCode">Значение кода подтверждения из почты/аутентификатора</param>
        /// <param name="global">Глобальный параметр конфигурации.</param>
        /// <returns>Кортеж, содержащий статус подтверждения и токен обновления.</returns>
        public async Task<(bool, BarkFluff.Proto.Identity.Token RefreshToken)> ConfirmAccount(string code, string verifyCode, GlobalParam global)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var confirmAccount = await IdentityAC.ConfirmAccountAsync(new Proto.Identity.ConfirmAccountRequest
                    {
                        CodeId = code,
                        CodeValue = verifyCode
                    });
                    return (true, confirmAccount.RefreshToken);
                }, global);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeExpiredException)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeIncorrectException)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Identity.ConfirmationCodeNotFoundException)
            {
                // обработка
            }
            return (false, null);
        }

        /// <summary>
        /// Изменяет биографию пользователя.
        /// </summary>
        /// <param name="bio">Новая биография пользователя</param>
        /// <param name="globalParam">Параметр глобальной конфигурации</param>
        /// <returns>Возвращает true, если операция успешна</returns>
        public async Task<bool> ChangeBio(string bio, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC.ChangeBioAsync(new Proto.Users.ChangeBioRequest { Bio = bio });
                    return true;
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                // обработка
            }
            return false;
        }

        public async Task<bool> ChangeUsername(string username, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC.ChangeUsernameAsync(new Proto.Users.ChangeUsernameRequest { Username = username });
                    return true;
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                // обработка
            }
            return false;
        }

        /// <summary>
        /// Проверяет, существует ли адрес электронной почты в системе.
        /// </summary>
        /// <param name="email">Адрес электронной почты для проверки.</param>
        /// <param name="globalParam">Параметр глобальной конфигурации.</param>
        /// <returns>Возвращает true, если почта существует, иначе false.</returns>
        public async Task<bool> CheckEmail(string email, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC.CheckExistEmailAsync(new Proto.Users.CheckExistEmailRequest { Email = email.ToLower() });
                    return getUser.Exist;
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                // обработка
            }
            return false;
        }

        /// <summary>
        /// Проверяет, существует ли имя пользователя в системе.
        /// </summary>
        /// <param name="username">Имя пользователя для проверки.</param>
        /// <param name="globalParam">Параметр глобальной конфигурации.</param>
        /// <returns>Возвращает true, если имя пользователя существует, иначе false.</returns>
        public async Task<bool> CheckUsername(string username, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var getUser = await UsersAC.CheckExistUsernameAsync(new Proto.Users.CheckExistUsernameRequest { Username = username.ToLower() });
                    return getUser.Exist;
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                // обработка
            }
            return false;
        }

        public async Task<bool> SetPassword(string newPassword, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var setPassword = await IdentityAC.SetPasswordAsync(new Proto.Identity.SetPasswordRequest { Password = newPassword });
                    return true;
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidLoginOrPasswordException)
            {
                // обработка
            }
            return false;
        }
        public async Task<(bool, string resetId)> ResetPassword(string emailOrUsername, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var resetPassword = await IdentityAC.ResetPasswordAsync(new Proto.Identity.ResetPasswordRequest { Email = emailOrUsername });
                    return (true, resetPassword.ResetId);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.NotSetUsernameOrEmailException)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UsernameOrEmailIsEmptyException)
            {
                // обработка
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidLoginOrPasswordException)
            {
                // обработка
            }

            return (false, null);

        }
    }
}
