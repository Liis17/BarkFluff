
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

        private string EnsureHttpPrefix(string _url)
        {
            return !_url.StartsWith("http://") && !_url.StartsWith("https://")
                   ? "http://" + _url
                   : _url;
        }

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

        public async Task<string> TokenUpdate(GlobalParam globalParam, string refreshToken)
        {
            var response = await IdentityAC.CreateTokenAsync(new BarkFluff.Proto.Identity.CreateTokenRequest { RefreshToken = refreshToken });
            globalParam.AccessToken = response.AccessToken;
            return response.AccessToken.Value;
        }

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
        public async Task<string> GetUserAvatar(long userId = 0)
        {
            try
            {
                var getLinkUpload = await GetUserDatas(userId);
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
        public async Task<UserData> GetUserDatas(long userId = 0)
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
    }
}
