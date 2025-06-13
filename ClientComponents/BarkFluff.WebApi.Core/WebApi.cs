
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Grpc.Core.Interceptors;
using Grpc.Net.Client;

using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BarkFluff.WebApi.Core
{
    public class WebApi
    {
        public BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC;
        public BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC;
        public BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC;
        public BarkFluff.Proto.Files.FilesApi.FilesApiClient? FilesAC;

        public GrpcChannel? BeaconChannel;
        public GrpcChannel? UserChannel;
        public GrpcChannel? IdentityChannel;
        public GrpcChannel? FilesChannel;

        public void CreateOnlyBeaconAC(GlobalParam gParam)
        {
            CreateBeaconAC(gParam);
        }
        public void CreateAC(GlobalParam gParam, string deviceName, string os, string appName, string appVersion, string ip)
        {
            CreateUsersAC(gParam);
            CreateBeaconAC(gParam);
            CreateIdentityAC(gParam);
            CreateFilesAC(gParam);
            AddInterceptor(gParam, deviceName, os, appName, appVersion, ip);
        }
        private void CreateUsersAC(GlobalParam _gParam)
        {
            UsersAC = null!;
            UserChannel = null!;
            _gParam.SocketUsers = EnsureHttpPrefix(_gParam.SocketUsers);
            UserChannel = GrpcChannel.ForAddress(_gParam.SocketUsers);
            UsersAC = new BarkFluff.Proto.Users.UsersApi.UsersApiClient(UserChannel);
        }

        private void CreateBeaconAC(GlobalParam _gParam)
        {
            BeaconAC = null!;
            BeaconChannel = null!;
            _gParam.SocketBeacon = EnsureHttpPrefix(_gParam.SocketBeacon);
            BeaconChannel = GrpcChannel.ForAddress(_gParam.SocketBeacon);
            BeaconAC = new BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient(BeaconChannel);
        }

        private void CreateIdentityAC(GlobalParam _gParam)
        {
            IdentityAC = null!;
            IdentityChannel = null!;
            _gParam.SocketIdentity = EnsureHttpPrefix(_gParam.SocketIdentity);
            IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);
            IdentityAC = new BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient(IdentityChannel);
        }
        private void CreateFilesAC(GlobalParam _gParam)
        {
            FilesAC = null!;
            FilesAC = null!;
            _gParam.SocketFiles = EnsureHttpPrefix(_gParam.SocketFiles);
            FilesChannel = GrpcChannel.ForAddress(_gParam.SocketFiles);
            FilesAC = new BarkFluff.Proto.Files.FilesApi.FilesApiClient(FilesChannel);
        }

        private void AddInterceptor(GlobalParam _gParam, string _deviceName, string os, string appName, string appVersion, string ip)
        {
            IdentityAC = null!;
            UsersAC = null!;

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

        /// <summary>
        /// Получает информацию о сервере и сохраняет её в GlobalParam.
        /// </summary>
        /// <param name="param">Параметры приложения</param>
        /// <returns>Возвращает информацию о сервере</returns>
        public Proto.Beacon.GetServerInfoResponse GetServerInfo(GlobalParam param)
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

            return response; // если нужно то он будет возвращать инфу о сервере
        }

        /// <summary>
        /// Обновляет токен доступа для приложения.
        /// </summary>
        /// <param name="globalParam">Параметры приложения</param>
        /// <returns>Возвращает новый токен доступа</returns>
        public async Task<string> TokenUpdate(GlobalParam globalParam)
        {
            var response = await IdentityAC.CreateTokenAsync(new BarkFluff.Proto.Identity.CreateTokenRequest { RefreshToken = globalParam.RefreshToken.Value });
            globalParam.AccessToken = response.AccessToken;
            return response.AccessToken.Value;
        }

        /// <summary>
        /// Отправляет аватар пользователя на сервер в формате JPEG.
        /// </summary>
        /// <param name="jpegImageBytes">Картинка в виде байтов</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task UploadUserAvatarAsync(byte[] jpegImageBytes)
        {
            try
            {
                var getLinkUpload = await FilesAC.GetUploadUrlAsync(new Proto.Files.GetUploadUrlRequest { FileType = Proto.Files.UploadFileType.UserAvatar });

                using var httpClient = new HttpClient();
                using var formData = new MultipartFormDataContent();

                var fileContent = new ByteArrayContent(jpegImageBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

                formData.Add(fileContent, "file", "avatar.jpg");

                try
                {
                    var response = await httpClient.PostAsync(getLinkUpload.Url, formData);
                    response.EnsureSuccessStatusCode(); 
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ошибка при загрузке файла: {ex.Message}", ex);
                }

                try
                {
                    var setAvatar = await UsersAC.SetProfilePictureAsync(new Proto.Users.SetProfilePictureRequest { FileId = getLinkUpload.FileId });
                }
                catch(BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
                {
                    //добавить обработчики
                }
                catch(BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
                {
                    //добавить обработчики
                }

            }
            catch(BarkFluff.Shared.Exceptions.Files.NotValidFileIdException)
            {
                //добавить обработчики
            }
        }

        /// <summary>
        /// Получает ссылку на аватар пользователя по его ID.
        /// </summary>
        /// <param name="userId">[НЕОБЯЗАТЕЛЬНО] ID пользователя, аватар которого нужно получить</param>
        /// <returns>Возвращает URL аватара или null, если аватар не найден</returns>
        public async Task<string> GetUserAvatar(long userId = 0)
        {
            try
            {
                var getLinkUpload = await GetUserData(userId);
                return getLinkUpload.ProfilePictureUrl;
            }
            catch(BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
            {
                //добавить обработчики
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                //добавить обработчики
            }
            return null;
        }

        /// <summary>
        /// Получает данные пользователя по его ID.
        /// </summary>
        /// <param name="userId">[НЕОБЯЗАТЕЛЬНО] ID пользователя, данные которого нужно получить</param>
        /// <returns>Объект данных пользователя</returns>
        public async Task<UserData> GetUserData(long userId = 0)
        {
            try
            {
                var getUser = await UsersAC.GetUserAsync(new Proto.Users.GetUserRequest { UserId = userId });
                var userData = new UserData
                {
                    FirstName = getUser.User.FirstName,
                    LastName = getUser.User.LastName,
                    Email = "Почта не установлена",
                    Username = getUser.User.Username,
                    RegistrationDate = getUser.User.RegistrationDate,
                    Id = getUser.User.Id,
                    ProfilePictureUrl = getUser.User.ProfilePicture,
                };
                return userData;
            }
            catch (BarkFluff.Shared.Exceptions.Users.ProfilePictureHasNotValidType)
            {
                //добавить обработчики
            }
            catch (BarkFluff.Shared.Exceptions.Users.UserIsDraftException)
            {
                //добавить обработчики
            }
            return null;
            
        }

        /// <summary>
        /// Запрашивает QR-код для настройки двухфакторной аутентификации (OTP) и возвращает его в виде base64 строки.
        /// </summary>
        /// <returns>Кортеж, содержащий QR-код в формате base64 и код для ручного ввода.</returns>
        public async Task<(string qrBase64, string justCode)> OtpReceipt()
        {
            var response = await IdentityAC.EnableOtpVerificationAsync(new Proto.Identity.EnableOtpVerificationRequest { OtpType = Proto.Identity.OtpTypeId.Authenticator });
            var qr = response.OtpQr; // строка base64 для qr кода
            var code = response.OtpCode; //тут код для ручного ввода
            return (qr, code);
        }

        /// <summary>
        /// Подтверждает двухфакторную аутентификацию (OTP) с использованием предоставленного кода.
        /// </summary>
        /// <param name="code">Код который необходимо ввести для подтверждения из Google Authenticator</param>
        public async Task OtpAccept(string code)
        {
            var response = await IdentityAC.ConfirmOtpVerificationAsync(new Proto.Identity.ConfirmOtpVerificationRequest { OtpCode = code });
            
        }
    }
}
