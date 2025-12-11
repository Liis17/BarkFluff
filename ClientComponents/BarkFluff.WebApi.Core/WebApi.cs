using BarkFluff.Proto.Updates;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace BarkFluff.WebApi.Core
{
    public class WebApi : IDisposable
    {
        #region Constants
        private const int DefaultPageSize = 50;
        private const string DefaultNavigatorUrl = "navigator.barkfluff.com:64645";
        private static readonly TimeSpan DefaultHttpTimeout = TimeSpan.FromMinutes(5);
        #endregion

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = DefaultHttpTimeout };
        private bool _disposed = false;
        #region ApiClients
        private BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC;
        private BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC;
        private BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC;
        private BarkFluff.Proto.Files.FilesApi.FilesApiClient? FilesAC;
        private BarkFluff.Proto.Messages.MessagesApi.MessagesApiClient? MessagesAC;
        private BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient? NavigatorAC;
        private BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiClient? UpdatesAC;
        #endregion

        #region gRPC Channels
        private GrpcChannel? BeaconChannel;
        private GrpcChannel? UserChannel;
        private GrpcChannel? IdentityChannel;
        private GrpcChannel? FilesChannel;
        private GrpcChannel? MessagesChannel;
        private GrpcChannel? NavigatorChannel;
        private GrpcChannel? UpdatesChannel;
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

        public bool ACisnull => UsersAC == null || BeaconAC == null || IdentityAC == null || FilesAC == null || MessagesAC == null || UpdatesAC == null;
        public bool BeaconIsnull => BeaconAC == null;

        #region IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                BeaconChannel?.Dispose();
                UserChannel?.Dispose();
                IdentityChannel?.Dispose();
                FilesChannel?.Dispose();
                MessagesChannel?.Dispose();
                NavigatorChannel?.Dispose();
                UpdatesChannel?.Dispose();
            }
            _disposed = true;
        }
        #endregion

        #region Создание клиентов (AC - Api Client)
        /// <summary>
        /// Вызывает создание только gRPC клиента для работы с Beacon API на сервере.
        /// </summary>
        /// <param name="gParam">Параметры приложения</param>
        public ErrorReturner CreateOnlyBeaconAC(GlobalParam gParam)
        {
            if (gParam == null)
                return new ErrorReturner(false, "Параметры приложения не могут быть null");
            if (string.IsNullOrWhiteSpace(gParam.SocketBeacon))
                return new ErrorReturner(false, "Адрес Beacon сервера не указан");

            try
            {
                gParam.SocketBeacon = EnsureHttpPrefix(gParam.SocketBeacon);
                BeaconChannel = GrpcChannel.ForAddress(gParam.SocketBeacon);
                BeaconAC = new Proto.Beacon.BeaconApi.BeaconApiClient(BeaconChannel);
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подключения к серверу");
            }

        }

        public ErrorReturner CreateNavigatorAC(string navigatorUrl = DefaultNavigatorUrl)
        {
            if (string.IsNullOrWhiteSpace(navigatorUrl))
                return new ErrorReturner(false, "URL навигатора не может быть пустым");

            try
            {
                NavigatorChannel = GrpcChannel.ForAddress(EnsureHttpPrefix(navigatorUrl));
                NavigatorAC = new Proto.Navigator.NavigatorApi.NavigatorApiClient(NavigatorChannel);
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подключения к серверу");
            }
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
        public ErrorReturner CreateAC(GlobalParam gParam, string deviceName, string os, string appName, string appVersion, string ip)
        {
            if (gParam == null)
                return new ErrorReturner(false, "Параметры приложения не могут быть null");

            try
            {
                _initParams = new InitializationParams
                {
                    DeviceName = deviceName,
                    Os = os,
                    AppName = appName,
                    AppVersion = appVersion,
                    Ip = ip
                };
                AddInterceptor(gParam, deviceName, os, appName, appVersion, ip);
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подключения к серверу");
            }

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
        private ErrorReturner AddInterceptor(GlobalParam _gParam, string _deviceName, string os, string appName, string appVersion, string ip)
        {
            IdentityAC = null!;
            UsersAC = null!;
            FilesAC = null!;
            MessagesAC = null!;
            UpdatesAC = null!;

            try
            {
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

                _gParam.SocketMessages = EnsureHttpPrefix(_gParam.SocketMessages);
                _gParam.SocketFiles = EnsureHttpPrefix(_gParam.SocketFiles);
                _gParam.SocketIdentity = EnsureHttpPrefix(_gParam.SocketIdentity);
                _gParam.SocketBeacon = EnsureHttpPrefix(_gParam.SocketBeacon);
                _gParam.SocketUsers = EnsureHttpPrefix(_gParam.SocketUsers);
                _gParam.SocketUpdates = EnsureHttpPrefix(_gParam.SocketUpdates);

                MessagesChannel = GrpcChannel.ForAddress(_gParam.SocketMessages);
                FilesChannel = GrpcChannel.ForAddress(_gParam.SocketFiles);
                IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);
                BeaconChannel = GrpcChannel.ForAddress(_gParam.SocketBeacon);
                UserChannel = GrpcChannel.ForAddress(_gParam.SocketUsers);
                UpdatesChannel = GrpcChannel.ForAddress(_gParam.SocketUpdates);

                var identityInvoker = IdentityChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var userInvoker = UserChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var filesInvoker = FilesChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var messageInvoker = MessagesChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var updatesInvoker = UpdatesChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);

                IdentityAC = new BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient(identityInvoker);
                UsersAC = new BarkFluff.Proto.Users.UsersApi.UsersApiClient(userInvoker);
                FilesAC = new BarkFluff.Proto.Files.FilesApi.FilesApiClient(filesInvoker);
                MessagesAC = new Proto.Messages.MessagesApi.MessagesApiClient(messageInvoker);
                UpdatesAC = new Proto.Updates.UpdatesApi.UpdatesApiClient(updatesInvoker);
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подключения к серверу");
            }

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
        public static string EnsureHttpPrefix(string _url)
        {
            return !_url.StartsWith("http://") && !_url.StartsWith("https://")
                   ? "http://" + _url
                   : _url;
        }

        /// <summary>
        /// Проверяет, что список файловых URL не пуст.
        /// </summary>
        private static bool HasValidFileUrls(Google.Protobuf.Collections.RepeatedField<Proto.Files.GetTempDownloadUrlResponse.Types.DownloadFileData>? fileUrls)
        {
            return fileUrls != null && fileUrls.Count > 0;
        }
        #endregion

        /// <summary>
        /// Получает информацию о сервере
        /// </summary>
        /// <param name="param">Параметры приложения</param>
        /// <returns>Возвращает информацию о сервере</returns>
        public async Task<(ErrorReturner error, Proto.Beacon.GetServerInfoResponse?)> GetServerInfo(GlobalParam param)
        {
            if (BeaconAC == null)
            {
                CreateOnlyBeaconAC(param);
            }
            return await SafeCallAsync(async () =>
            {
                try
                {
                    var response = await BeaconAC!.GetServerInfoAsync(new BarkFluff.Proto.Beacon.GetServerInfoRequest());
                    return (new ErrorReturner(true), response);
                }
                catch (Exception)
                {
                    return (new ErrorReturner(false, "Ошибка получения информации о сервере"), null);
                }

            }, param);
        }

        /// <summary>
        /// Получает список серверов, доступных для подключения.
        /// </summary>
        /// <param name="global">Параметры приложения</param>
        /// <returns></returns>
        public async Task<(ErrorReturner, List<ServerDataElement> ServerElements)> GetServerList(GlobalParam global)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var response = await NavigatorAC!.ListServersAsync(new Proto.Navigator.ListServersRequest { });
                    var serverList = response.Servers
                        .Select(item => new ServerDataElement
                        {
                            Ip = $"{item.BeaconUri.Host}:{item.BeaconUri.Port}",
                            Title = item.Name,
                            UserCount = item.AccountsCount.ToString(),
                            Description = item.Description,
                            PublicName = item.ServerPublicName
                        })
                        .ToList();

                    return (new ErrorReturner(true), serverList);
                }, global);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения списка серверов"), new List<ServerDataElement>());
            }

        }

        /// <summary>
        /// Обновляет токен доступа для приложения.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <returns>Возвращает новый токен доступа</returns>
        public async Task<(ErrorReturner, string)> TokenUpdate(GlobalParam globalParam)
        {
            try
            {
                var response = await IdentityAC!.CreateTokenAsync(new BarkFluff.Proto.Identity.CreateTokenRequest { RefreshToken = globalParam.RefreshToken.Value });
                globalParam.AccessToken = response.AccessToken;
                return (new ErrorReturner(true), response.AccessToken.Value);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка обновления токена"), "");
            }

        }

        #region Работа с пользователями и аватарками

        /// <summary>
        /// Отправляет аватар пользователя на сервер в формате JPEG.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <param name="jpegImageBytes">Картинка в виде байтов</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<ErrorReturner> UploadUserAvatarAsync(GlobalParam globalParam, byte[] jpegImageBytes)
        {
            try
            {
                await SafeCallAsync<ErrorReturner>(async () =>
                {
                    var getLinkUpload = await FilesAC!.GetUploadUrlAsync(new Proto.Files.GetUploadUrlRequest
                    {
                        FileType = Proto.Files.UploadFileType.UserAvatar
                    });

                    using var formData = new MultipartFormDataContent();

                    var fileContent = new ByteArrayContent(jpegImageBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                    formData.Add(fileContent, "file", "avatar.jpg");

                    var response = await _httpClient.PostAsync(getLinkUpload.Url, formData);
                    response.EnsureSuccessStatusCode();

                    try
                    {
                        await UsersAC!.SetProfilePictureAsync(new Proto.Users.SetProfilePictureRequest
                        {
                            FileId = getLinkUpload.FileId
                        });
                    }
                    catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
                    {
                        return new ErrorReturner(false, "Переданный file-id содержит файл не с типом Изображение профиля пользователя");
                    }
                    catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
                    {
                        return new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid");
                    }

                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                return new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid");
            }
            return new ErrorReturner(true);
        }

        /// <summary>
        /// Uploads a file to the server and returns the file ID.
        /// </summary>
        /// <param name="globalParam">Application parameters</param>
        /// <param name="filePath">Path to the file to upload</param>
        /// <param name="fileType">Type of file being uploaded</param>
        /// <returns>File ID if successful, or error</returns>
        public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(GlobalParam globalParam, string filePath, Proto.Files.UploadFileType fileType)
        {
            return await UploadFileAsync(globalParam, filePath, fileType, null);
        }

        /// <summary>
        /// Uploads a file to the server with progress reporting and automatic image optimization.
        /// </summary>
        /// <param name="globalParam">Application parameters</param>
        /// <param name="filePath">Path to the file to upload</param>
        /// <param name="fileType">Type of file being uploaded</param>
        /// <param name="progress">Progress callback (0.0 to 1.0)</param>
        /// <returns>File ID if successful, or error</returns>
        public async Task<(ErrorReturner error, string? fileId)> UploadFileAsync(
            GlobalParam globalParam, 
            string filePath, 
            Proto.Files.UploadFileType fileType, 
            IProgress<double>? progress)
        {
            string? processedFilePath = null;
            try
            {
                return await SafeCallAsync(async () =>
                {
                    // Report initial progress
                    progress?.Report(0.1);

                    // Process image if needed (convert to JPEG with compression)
                    string fileToUpload = filePath;
                    if (fileType == Proto.Files.UploadFileType.MessageAttachmentImage)
                    {
                        processedFilePath = await ImageProcessor.ProcessImageForUploadAsync(filePath);
                        fileToUpload = processedFilePath;
                        progress?.Report(0.2);
                    }

                    var getLinkUpload = await FilesAC!.GetUploadUrlAsync(new Proto.Files.GetUploadUrlRequest
                    {
                        FileType = fileType
                    });
                    progress?.Report(0.3);

                    using var formData = new MultipartFormDataContent();

                    // Note: Reading entire file into memory. For large files, consider streaming approach.
                    var fileBytes = await File.ReadAllBytesAsync(fileToUpload);
                    var fileContent = new ByteArrayContent(fileBytes);
                    progress?.Report(0.5);
                    
                    // Determine content type based on file extension
                    var extension = Path.GetExtension(fileToUpload).ToLowerInvariant();
                    var contentType = extension switch
                    {
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        ".mp4" => "video/mp4",
                        ".webm" => "video/webm",
                        ".avi" => "video/x-msvideo",
                        ".mov" => "video/quicktime",
                        ".mkv" => "video/x-matroska",
                        _ => "application/octet-stream"
                    };

                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

                    formData.Add(fileContent, "file", Path.GetFileName(filePath));
                    progress?.Report(0.7);

                    var response = await _httpClient.PostAsync(getLinkUpload.Url, formData);
                    response.EnsureSuccessStatusCode();
                    progress?.Report(1.0);

                    return (new ErrorReturner(true), getLinkUpload.FileId);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                return (new ErrorReturner(false, "Неверный формат идентификатора файла. Он должен быть guid"), null);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, $"Ошибка загрузки файла: {ex.Message}"), null);
            }
            finally
            {
                // Clean up processed file if it was created
                if (processedFilePath != null && processedFilePath != filePath && File.Exists(processedFilePath))
                {
                    try 
                    { 
                        File.Delete(processedFilePath); 
                    } 
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"WebApi: Failed to delete temp file: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Получает ссылку на аватар пользователя по его ID.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <param name="userId">[НЕОБЯЗАТЕЛЬНО] ID пользователя, аватар которого нужно получить</param>
        /// <returns>Возвращает URL аватара или null, если аватар не найден</returns>
        public async Task<(ErrorReturner, string?)> GetUserAvatar(GlobalParam globalParam, long userId = 0)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
        #endregion

        /// <summary>
        /// Получает данные пользователя по его ID.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <param name="userId">[НЕОБЯЗАТЕЛЬНО] ID пользователя, данные которого нужно получить</param>
        /// <returns>Объект данных пользователя</returns>
        public async Task<(ErrorReturner Error, UserData? Data)> GetUserData(GlobalParam globalParam, long userId = 0)
        {
            if (globalParam == null)
                return (new ErrorReturner(false, "Параметры приложения не могут быть null"), null);

            try
            {
                return await SafeCallAsync(async () =>
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
        /// <param name="_email"></param>
        /// <param name="_username"></param>
        /// <param name="_password"></param>
        /// <param name="_otpCode"></param>
        /// <param name="global"></param>
        /// <returns>Токены refreshToken и accessToken</returns>
        public async Task<(ErrorReturner Error, Proto.Identity.Token? refreshToken, Proto.Identity.Token? accessToken, bool getMeOtpCode)> Authorizations(string _email, string _username, string _password, string _otpCode, GlobalParam global)
        {
            try
            {
                return await SafeCallAsync(async () =>
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


        #region Настройка двухфакторной аутентификации

        /// <summary>
        /// Запрашивает QR-код для настройки двухфакторной аутентификации (OTP) и возвращает его в виде base64 строки.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <returns>Кортеж, содержащий QR-код в формате base64 и код для ручного ввода.</returns>
        public async Task<(ErrorReturner error, string? qrBase64, string? justCode)> OtpReceipt(GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
        /// <param name="globalParam">Параметры приложения</param>
        /// <param name="code">Код который необходимо ввести для подтверждения из Google Authenticator</param>
        public async Task<ErrorReturner> OtpAccept(GlobalParam globalParam, string code)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
        public async Task<(ErrorReturner error, string? userid)> CreateAccount(string firstName, string lastName, string email, string login, GlobalParam global)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
        /// <param name="code">Код подтверждения который получен при создании аккаунта</param>
        /// <param name="verifyCode">Значение кода подтверждения из почты/аутентификатора</param>
        /// <param name="global">Глобальный параметр конфигурации.</param>
        /// <returns>Кортеж, содержащий статус подтверждения и токен обновления.</returns>
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? RefreshToken)> ConfirmAccount(string code, string verifyCode, GlobalParam global)
        {
            try
            {
                return await SafeCallAsync(async () =>
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

        /// <summary>
        /// Изменяет биографию пользователя.
        /// </summary>
        /// <param name="bio">Новая биография пользователя</param>
        /// <param name="globalParam">Параметр глобальной конфигурации</param>
        /// <returns>Возвращает true, если операция успешна</returns>
        public async Task<ErrorReturner> ChangeBio(string bio, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
                return await SafeCallAsync(async () =>
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
        /// <param name="email">Адрес электронной почты для проверки.</param>
        /// <param name="globalParam">Параметр глобальной конфигурации.</param>
        /// <returns>Возвращает true, если почта существует, иначе false.</returns>
        public async Task<(ErrorReturner error, bool exists)> CheckEmail(string email, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
        /// <param name="username">Имя пользователя для проверки.</param>
        /// <param name="globalParam">Параметр глобальной конфигурации.</param>
        /// <returns>Возвращает true, если имя пользователя существует, иначе false.</returns>
        public async Task<(ErrorReturner error, bool exists)> CheckUsername(string username, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
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

        #region Сброс пароля и установка нового пароля
        /// <summary>
        /// Устанавливает новый пароль для пользователя.
        /// </summary>
        /// <param name="newPassword"></param>
        /// <param name="globalParam"></param>
        /// <returns>Возвращает true, если установка пароля успешна, иначе false.</returns>
        public async Task<ErrorReturner> SetPassword(string newPassword, GlobalParam globalParam)
        {
            if (globalParam == null)
                return new ErrorReturner(false, "Параметры приложения не могут быть null");

            try
            {
                return await SafeCallAsync(async () =>
                {
                    await IdentityAC!.SetPasswordAsync(new Proto.Identity.SetPasswordRequest { Password = newPassword });
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
        /// <param name="email"></param>
        /// <param name="username"></param>
        /// <param name="globalParam"></param>
        /// <returns>Возвращает resetId для дальнейшего сброса</returns>
        public async Task<(ErrorReturner error, string? resetId)> ResetPassword(string email, string username, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка сброса пароля"), null);
            }

        }

        /// <summary>
        /// Выполняет подтверждение кода сброса пароля по resetId и возвращает новый токен обновления.
        /// </summary>
        /// <param name="resetId"></param>
        /// <param name="otpCode"></param>
        /// <param name="globalParam"></param>
        /// <returns></returns>
        public async Task<(ErrorReturner error, BarkFluff.Proto.Identity.Token? refreshToken)> ConfirmResetCode(string resetId, string otpCode, GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
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
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка подтверждения кода сброса пароля"), null);
            }
        }
        #endregion

        /// <summary>
        /// Возвращает список активных устройств пользователя.
        /// </summary>
        /// <param name="globalParam"></param>
        /// <returns>Возвращает List<string> устройств</returns>
        public async Task<(ErrorReturner error, List<string>? devicesList)> GetDevicesList(GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
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

        #region Работа с сообщениями

        public async Task<(ErrorReturner error, List<Proto.Messages.Chat>? chats)> GetChats(GlobalParam globalParam)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListChatsAsync(new Proto.Messages.ListChatsRequest
                    {
                        Pagination = new Proto.Shared.PageRequest { Size = DefaultPageSize },
                    });

                    var chatsList = response.Chats.ToList();

                    return (new ErrorReturner(true), chatsList);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                return (new ErrorReturner(false, "Пользователь не подтвержден."), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения чатов"), null);
            }
        }

        public async Task<(ErrorReturner error, MessageModel? message)> SendMessage(GlobalParam globalParam, (bool isUserId, string recipient) options, ForwardingLetter letter)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    Proto.Messages.SendMessageResponse response;
                    string chatId;
                    if (!options.isUserId)
                    {
                        chatId = options.recipient;
                        response = await MessagesAC!.SendMessageAsync(new Proto.Messages.SendMessageRequest
                        {
                            ChatId = options.recipient,
                            Message = new Proto.Messages.OutgoingMessage { Text = letter.Text, FilesIds = { letter.FilesId } },
                        });
                    }
                    else
                    {
                        chatId = string.Empty;
                        response = await MessagesAC!.SendMessageAsync(new Proto.Messages.SendMessageRequest
                        {
                            UserId = long.Parse(options.recipient),
                            Message = new Proto.Messages.OutgoingMessage { Text = letter.Text, FilesIds = { letter.FilesId } },
                        });
                    }

                    var sentMessage = new MessageModel
                    {
                        MessageId = response.Message.Id,
                        ChatId = chatId,
                        Text = response.Message.Content.Text,
                        Attachments = response.Message.Content.Attachments.Select(a => new AttachmentsModel
                        {
                            Id = a.Id,
                            Type = a.Type,
                            PreviewUrl = a.PreviewUrl,
                            FileId = a.FileId,
                            Size = a.AttachmentSize,
                        }).ToList(),
                        SenderId = response.Message.SenderId,
                        SentAt = response.Message.SentAt,
                        Type = response.Message.Type,
                        ReadBy = response.Message.ReadBy.ToList(),
                    };

                    return (new ErrorReturner(true), sentMessage);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата."), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка отправки сообщения"), null);
            }
        }

        public async Task<(bool, string?)> CreateGroupChat(GlobalParam globalParam, string chatName, List<long> userIds)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    // TODO: Реализовать создание группового чата (Backend task)
                    await Task.CompletedTask;
                    return (true, string.Empty);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (false, "Неверный идентификатор чата");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.UserNotMemberChatException)
            {
                return (false, "Пользователь не является участником чата");
            }
            catch (Exception)
            {
                return (false, "Ошибка создания Gruppового чата");
            }
        }

        public async Task<(bool, string?)> CreateChat(GlobalParam globalParam, string userId)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    await MessagesAC!.CreateGroupChatAsync(new Proto.Messages.CreateGroupChatRequest
                    {
                        // TODO: Добавить UserIds в API запрос (Backend task)
                    });

                    return (true, string.Empty);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (false, "Неверный идентификатор чата");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.UserNotMemberChatException)
            {
                return (false, "Пользователь не является участником чата");
            }
            catch (Exception)
            {
                return (false, "Ошибка создания чата");
            }
        }

        /// <summary>
        /// Метод для получения сообщений из чата.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="fromMessageId">Идентификатор сообщения, с которого начинать</param>
        /// <returns></returns>
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessages(GlobalParam globalParam, string chatId, long fromMessageId)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListMessagesAsync(new Proto.Messages.ListMessagesRequest { ChatId = chatId, Count = DefaultPageSize, FromMessageId = fromMessageId });
                    if (response.Messages.Count == 0)
                    {
                        return (new ErrorReturner(false, "Нет сообщений в этом чате", 1), null);
                    }
                    return (new ErrorReturner(true), response.Messages.Select(m => new MessageModel
                    {
                        MessageId = m.Id,
                        ChatId = chatId,
                        Text = m.Content.Text,
                        Attachments = m.Content.Attachments.Select(a => new AttachmentsModel
                        {
                            Id = a.Id,
                            Type = a.Type,
                            PreviewUrl = a.PreviewUrl,
                            FileId = a.FileId,
                            PreviewFileId = string.Empty, // TODO: Добавить PreviewFileId в API ответ (Backend task)
                            Size = a.AttachmentSize,
                        }).ToList(),
                        SenderId = m.SenderId,
                        SentAt = m.SentAt,
                        Type = m.Type,
                        ReadBy = m.ReadBy.ToList(),
                    }).ToList());
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения сообщений"), null);
            }
        }

        /// <summary>
        /// Метод для получения сообщений из чата с двусторонней пагинацией.
        /// Возвращает сообщения до и после указанного сообщения.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <param name="chatId">Идентификатор чата</param>
        /// <param name="fromMessageId">Идентификатор сообщения, относительно которого загружать</param>
        /// <param name="offsetBefore">Количество сообщений до указанного (в прошлое)</param>
        /// <param name="offsetAfter">Количество сообщений после указанного (в будущее)</param>
        /// <returns></returns>
        public async Task<(ErrorReturner error, List<MessageModel>? messages)> GetMessagesWithOffset(GlobalParam globalParam, string chatId, long fromMessageId, int offsetBefore, int offsetAfter)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListMessagesAsync(new Proto.Messages.ListMessagesRequest
                    {
                        ChatId = chatId,
                        FromMessageId = fromMessageId,
                        OffsetBefore = offsetBefore,
                        OffsetAfter = offsetAfter
                    });
                    if (response.Messages.Count == 0)
                    {
                        return (new ErrorReturner(false, "Нет сообщений в этом чате", 1), null);
                    }
                    return (new ErrorReturner(true), response.Messages.Select(m => new MessageModel
                    {
                        MessageId = m.Id,
                        ChatId = chatId,
                        Text = m.Content.Text,
                        Attachments = m.Content.Attachments.Select(a => new AttachmentsModel
                        {
                            Id = a.Id,
                            Type = a.Type,
                            PreviewUrl = a.PreviewUrl,
                            FileId = a.FileId,
                            PreviewFileId = string.Empty,
                            Size = a.AttachmentSize,
                        }).ToList(),
                        SenderId = m.SenderId,
                        SentAt = m.SentAt,
                        Type = m.Type,
                        ReadBy = m.ReadBy.ToList(),
                    }).ToList());
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return (new ErrorReturner(false, "Неверный идентификатор чата"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения сообщений"), null);
            }
        }

        public async Task<ErrorReturner> MarkMessageAsRead(GlobalParam globalParam, List<long> messageId)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    await MessagesAC!.MarkAsReadAsync(new Proto.Messages.MarkAsReadRequest { MessageIds = { messageId } });
                    return (new ErrorReturner(true));
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatIdNotValidException)
            {
                return new ErrorReturner(false, "Неверный идентификатор чата.");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.MessageNotFoundException)
            {
                return new ErrorReturner(false, "Сообщение не найдено.");
            }
            catch (Exception ex)
            {
                return new ErrorReturner(false, "Ошибка отметки сообщения как прочитанного");
            }
        }

        #endregion

        #region Поиск

        public async Task<(ErrorReturner error, List<UserData>? userList)> SearchUser(GlobalParam globalParam, string userNameSearched)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var response = await UsersAC!.SearchUsersAsync(new Proto.Users.SearchUsersRequest
                    {
                        Pagination = new Proto.Shared.PageRequest
                        {
                            Offset = 0,
                            Size = DefaultPageSize
                        },
                        Query = userNameSearched
                    });

                    var userDataList = response.Users
                        .Select(item => new UserData
                        {
                            FirstName = item.FirstName,
                            LastName = item.LastName,
                            Email = "Почта скрыта",
                            Username = item.Username,
                            RegistrationDate = item.RegistrationDate.ToDateTime(),
                            Id = item.Id,
                            Badges = item.Badges.ToString(),
                            ProfilePictureUrl = item.ProfilePicture,
                            ProfilePicturePreviewUrl = item.ProfilePicturePreview,
                        })
                        .ToList();

                    return (new ErrorReturner(true), userDataList);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.InvalidRefreshTokenException)
            {
                return (new ErrorReturner(false, "Неверный токен обновления."), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка поиска пользователей"), null);
            }
        }

        #endregion

        #region Файлы

        public async Task<(ErrorReturner error, string? url)> GetFile(GlobalParam globalParam, string fileId)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.GetTempDownloadUrlAsync(new Proto.Files.GetTempDownloadUrlRequest { FileIds = { fileId } });
                    if (!HasValidFileUrls(response.FileUrls))
                        return (new ErrorReturner(false, "Файл не найден"), null);
                    return (new ErrorReturner(true), response.FileUrls[0].Url);
                }, globalParam);
            }
            catch (Exception ex)
            {
                return (new ErrorReturner(false, "Ошибка получения файла"), null);
            }
        }

        public async Task<(ErrorReturner error, List<string>? urls)> GetFiles(GlobalParam globalParam, List<string> fileId)
        {
            try
            {
                return await SafeCallAsync(async () =>
                {
                    var response = await FilesAC!.GetTempDownloadUrlAsync(new Proto.Files.GetTempDownloadUrlRequest { FileIds = { fileId } });
                    if (!HasValidFileUrls(response.FileUrls))
                        return (new ErrorReturner(false, "Файлы не найдены"), null);
                    var urls = response.FileUrls.Select(f => f.Url).ToList();
                    return (new ErrorReturner(true), urls);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения файлов"), null);
            }
        }

        #endregion

        #region реалтайм обновления

        public async Task<(ErrorReturner error, IAsyncEnumerable<NewMessageEvent>? stream)> JustUpdate(GlobalParam globalParam)
        {
            return await SafeCallAsync(async () =>
            {
                try
                {
                    // Подготовка заголовков с токеном (предполагается, что токен в globalParam)
                    var headers = new Metadata();
                    if (!string.IsNullOrEmpty(globalParam.AccessToken?.Value))
                    {
                        headers.Add("Authorization", $"Bearer {globalParam.AccessToken.Value}");
                    }

                    // Вызов метода подписки с заголовками
                    var response = UpdatesAC!.SubscribeNewMessages(new SubscribeNewMessagesRequest { }, headers);

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<NewMessageEvent> GetMessageStream()
                    {
                        while (true)
                        {
                            bool hasNext;
                            NewMessageEvent messageEvent;

                            try
                            {
                                hasNext = await response.ResponseStream.MoveNext(CancellationToken.None);
                                if (!hasNext)
                                {
                                    yield break; // Стрим завершён
                                }
                                messageEvent = response.ResponseStream.Current;
                            }
                            catch (RpcException ex)
                            {
                                var a = ex;
                                yield break;
                            }
                            catch (Exception)
                            {
                                yield break;
                            }

                            yield return messageEvent;
                        }
                    }

                    return (new ErrorReturner(true, ""), GetMessageStream());
                }
                catch (RpcException)
                {
                    return (new ErrorReturner(false, "Ошибка аутентификации"), null);
                }
                catch (Exception)
                {
                    return (new ErrorReturner(false, "Ошибка подключения к обновлениям"), null);
                }
            }, globalParam);
        }

        public async Task<(ErrorReturner error, IAsyncEnumerable<MessageReadEvent>? stream)> SubscribeToReadReceipts(
            GlobalParam globalParam)
        {
            return await SafeCallAsync(async () =>
            {
                try
                {
                    // Подготовка заголовков с токеном
                    var headers = new Metadata();
                    if (!string.IsNullOrEmpty(globalParam.AccessToken?.Value))
                    {
                        headers.Add("Authorization", $"Bearer {globalParam.AccessToken.Value}");
                    }

                    var request = new SubscribeMessagesReadRequest();

                    // Вызов метода подписки с заголовками
                    var response = UpdatesAC!.SubscribeMessagesRead(request, headers);

                    // Создаём IAsyncEnumerable для стрима
                    async IAsyncEnumerable<MessageReadEvent> GetReadReceiptStream()
                    {
                        while (true)
                        {
                            bool hasNext;
                            MessageReadEvent update;

                            try
                            {
                                hasNext = await response.ResponseStream.MoveNext(CancellationToken.None);
                                if (!hasNext)
                                {
                                    yield break; // Стрим завершён
                                }
                                update = response.ResponseStream.Current;
                            }
                            catch (RpcException)
                            {
                                yield break;
                            }
                            catch (Exception)
                            {
                                yield break;
                            }

                            yield return update;
                        }
                    }

                    return (new ErrorReturner(true, ""), GetReadReceiptStream());
                }
                catch (RpcException)
                {
                    return (new ErrorReturner(false, "Ошибка аутентификации"), null);
                }
                catch (Exception)
                {
                    return (new ErrorReturner(false, "Ошибка подключения к обновлениям"), null);
                }
            }, globalParam);
        }

        #endregion
    }
}
