using BarkFluff.WebApi.Core.MessengerData;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для создания и управления gRPC клиентами и каналами.
    /// </summary>
    internal class WebApiClientManager
    {
        private const string DefaultNavigatorUrl = "navigator.barkfluff.com:64646";

        private readonly WebApi _webApi;

        internal struct InitializationParams
        {
            public string DeviceName { get; set; }
            public string Os { get; set; }
            public string AppName { get; set; }
            public string AppVersion { get; set; }
            public string Ip { get; set; }
        }

        internal InitializationParams? _initParams;

        public WebApiClientManager(WebApi webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Вызывает создание только gRPC клиента для работы с Beacon API на сервере.
        /// </summary>
        public ErrorReturner CreateOnlyBeaconAC(GlobalParam gParam)
        {
            if (gParam == null)
                return new ErrorReturner(false, "Параметры приложения не могут быть null");
            if (string.IsNullOrWhiteSpace(gParam.SocketBeacon))
                return new ErrorReturner(false, "Адрес Beacon сервера не указан");

            try
            {
                gParam.SocketBeacon = WebApi.EnsureHttpPrefix(gParam.SocketBeacon);
                _webApi.BeaconChannel = GrpcChannel.ForAddress(gParam.SocketBeacon);
                _webApi.BeaconAC = new Proto.Beacon.BeaconApi.BeaconApiClient(_webApi.BeaconChannel);
                UpdateManagerClients();
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
                _webApi.NavigatorChannel = GrpcChannel.ForAddress(WebApi.EnsureHttpPrefix(navigatorUrl));
                _webApi.NavigatorAC = new Proto.Navigator.NavigatorApi.NavigatorApiClient(_webApi.NavigatorChannel);
                UpdateManagerClients();
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
        internal ErrorReturner AddInterceptor(GlobalParam _gParam, string _deviceName, string os, string appName, string appVersion, string ip)
        {
            _webApi.IdentityAC = null!;
            _webApi.UsersAC = null!;
            _webApi.FilesAC = null!;
            _webApi.MessagesAC = null!;
            _webApi.UpdatesAC = null!;

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

                _gParam.SocketMessages = WebApi.EnsureHttpPrefix(_gParam.SocketMessages);
                _gParam.SocketFiles = WebApi.EnsureHttpPrefix(_gParam.SocketFiles);
                _gParam.SocketIdentity = WebApi.EnsureHttpPrefix(_gParam.SocketIdentity);
                _gParam.SocketBeacon = WebApi.EnsureHttpPrefix(_gParam.SocketBeacon);
                _gParam.SocketUsers = WebApi.EnsureHttpPrefix(_gParam.SocketUsers);
                _gParam.SocketUpdates = WebApi.EnsureHttpPrefix(_gParam.SocketUpdates);

                _webApi.MessagesChannel = GrpcChannel.ForAddress(_gParam.SocketMessages);
                _webApi.FilesChannel = GrpcChannel.ForAddress(_gParam.SocketFiles);
                _webApi.IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);
                _webApi.BeaconChannel = GrpcChannel.ForAddress(_gParam.SocketBeacon);
                _webApi.UserChannel = GrpcChannel.ForAddress(_gParam.SocketUsers);
                _webApi.UpdatesChannel = GrpcChannel.ForAddress(_gParam.SocketUpdates);

                var identityInvoker = _webApi.IdentityChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var userInvoker = _webApi.UserChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var filesInvoker = _webApi.FilesChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var messageInvoker = _webApi.MessagesChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);
                var updatesInvoker = _webApi.UpdatesChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor).Intercept(osInterceptor).Intercept(appInterceptor).Intercept(errorInterceptor).Intercept(ipInterceptor);

                _webApi.IdentityAC = new BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient(identityInvoker);
                _webApi.UsersAC = new BarkFluff.Proto.Users.UsersApi.UsersApiClient(userInvoker);
                _webApi.FilesAC = new BarkFluff.Proto.Files.FilesApi.FilesApiClient(filesInvoker);
                _webApi.MessagesAC = new Proto.Messages.MessagesApi.MessagesApiClient(messageInvoker);
                _webApi.UpdatesAC = new Proto.Updates.UpdatesApi.UpdatesApiClient(updatesInvoker);

                UpdateManagerClients();
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подключения к серверу");
            }
        }

        /// <summary>
        /// Обновляет ссылки на клиенты во всех менеджерах.
        /// </summary>
        private void UpdateManagerClients()
        {
            _webApi.ServerManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.TokenManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.UserManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.AuthManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.RegistrationManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.PasswordManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.MessageManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.SearchManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.FileManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);

            _webApi.UpdateManager.SetClients(
                _webApi.UsersAC, _webApi.BeaconAC, _webApi.IdentityAC, _webApi.FilesAC, _webApi.MessagesAC, _webApi.NavigatorAC, _webApi.UpdatesAC,
                _webApi.BeaconChannel, _webApi.UserChannel, _webApi.IdentityChannel, _webApi.FilesChannel, _webApi.MessagesChannel, _webApi.NavigatorChannel, _webApi.UpdatesChannel);
        }
    }
}
