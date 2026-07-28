using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Менеджер для создания и управления gRPC клиентами и каналами.
    /// </summary>
    internal class WebApiClientManager
    {
        private const string DefaultNavigatorUrl = "https://navigator.barkfluff.com:443";

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
                _webApi.BeaconChannel?.Dispose();
                _webApi.BeaconChannel = GrpcChannel.ForAddress(gParam.SocketBeacon);
                _webApi.BeaconAC = new Proto.Beacon.BeaconApi.BeaconApiClient(_webApi.BeaconChannel);
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
                _webApi.NavigatorChannel?.Dispose();
                _webApi.NavigatorChannel = GrpcChannel.ForAddress(WebApi.EnsureHttpPrefix(navigatorUrl));
                _webApi.NavigatorAC = new Proto.Navigator.NavigatorApi.NavigatorApiClient(_webApi.NavigatorChannel);
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
            _webApi.OnlinerAC = null!;
            _webApi.CallsAC = null;
            _webApi.FastAuthUserAC = null;

            try
            {
                var token = string.Empty;
                if (_gParam.AccessToken != null)
                {
                    token = _gParam.AccessToken.Value;
                }
                var deviceInterceptor = new Shared.Auth.XDeviceClientInterceptor(deviceName: _deviceName);
                var deviceIdInterceptor = new Shared.Auth.XDeviceIdInterceptor(_gParam.DeviceId);
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
                _gParam.SocketOnliner = WebApi.EnsureHttpPrefix(_gParam.SocketOnliner);

                _webApi.MessagesChannel?.Dispose();
                _webApi.FilesChannel?.Dispose();
                _webApi.IdentityChannel?.Dispose();
                _webApi.BeaconChannel?.Dispose();
                _webApi.UserChannel?.Dispose();
                _webApi.UpdatesChannel?.Dispose();
                _webApi.OnlinerChannel?.Dispose();

                _webApi.MessagesChannel = GrpcChannel.ForAddress(_gParam.SocketMessages);
                _webApi.FilesChannel = GrpcChannel.ForAddress(_gParam.SocketFiles);
                _webApi.IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);
                _webApi.BeaconChannel = GrpcChannel.ForAddress(_gParam.SocketBeacon);
                _webApi.UserChannel = GrpcChannel.ForAddress(_gParam.SocketUsers);
                _webApi.UpdatesChannel = GrpcChannel.ForAddress(_gParam.SocketUpdates);
                _webApi.OnlinerChannel = GrpcChannel.ForAddress(_gParam.SocketOnliner);

                var interceptors = new Interceptor[]
                {
                    deviceInterceptor, deviceIdInterceptor, jwtInterceptor,
                    osInterceptor, appInterceptor, errorInterceptor, ipInterceptor
                };

                var identityInvoker = BuildInvoker(_webApi.IdentityChannel, interceptors);
                var userInvoker = BuildInvoker(_webApi.UserChannel, interceptors);
                var filesInvoker = BuildInvoker(_webApi.FilesChannel, interceptors);
                var messageInvoker = BuildInvoker(_webApi.MessagesChannel, interceptors);
                var updatesInvoker = BuildInvoker(_webApi.UpdatesChannel, interceptors);
                var onlinerInvoker = BuildInvoker(_webApi.OnlinerChannel, interceptors);

                _webApi.IdentityAC = new BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient(identityInvoker);
                _webApi.UsersAC = new BarkFluff.Proto.Users.UsersApi.UsersApiClient(userInvoker);
                _webApi.FilesAC = new BarkFluff.Proto.Files.FilesApi.FilesApiClient(filesInvoker);
                _webApi.MessagesAC = new Proto.Messages.MessagesApi.MessagesApiClient(messageInvoker);
                _webApi.UpdatesAC = new Proto.Updates.UpdatesApi.UpdatesApiClient(updatesInvoker);
                _webApi.OnlinerAC = new BarkFluff.Proto.Onliner.OnlinerApi.OnlinerApiClient(onlinerInvoker);

                // Звонки есть не на каждом сервере, а сохранённый ранее GlobalParam мог быть
                // записан до появления Calls — поэтому канал создаём только при известном адресе.
                _webApi.CallsChannel?.Dispose();
                _webApi.CallsChannel = null;
                if (!string.IsNullOrWhiteSpace(_gParam.SocketCalls))
                {
                    _gParam.SocketCalls = WebApi.EnsureHttpPrefix(_gParam.SocketCalls);
                    _webApi.CallsChannel = GrpcChannel.ForAddress(_gParam.SocketCalls);
                    _webApi.CallsAC = new Proto.Calls.CallsApi.CallsApiClient(BuildInvoker(_webApi.CallsChannel, interceptors));
                }

                // Авторизованный FastAuth — для подтверждения входа нового устройства с этого клиента
                // (Scan/Accept/Reject требуют User-токен, анонимный канал из CreateFastAuthClient не подходит).
                _webApi.FastAuthUserChannel?.Dispose();
                _webApi.FastAuthUserChannel = null;
                if (!string.IsNullOrWhiteSpace(_gParam.SocketFastAuth))
                {
                    _gParam.SocketFastAuth = WebApi.EnsureHttpPrefix(_gParam.SocketFastAuth);
                    _webApi.FastAuthUserChannel = GrpcChannel.ForAddress(_gParam.SocketFastAuth);
                    _webApi.FastAuthUserAC = new Proto.FastAuth.FastAuthApi.FastAuthApiClient(BuildInvoker(_webApi.FastAuthUserChannel, interceptors));
                }

                return new ErrorReturner(true);
            }
            catch (Exception ex)
            {
                return new ErrorReturner(false, "Ошибка подключения к серверу");
            }
        }

        public ErrorReturner CreateFastAuthClient(MessengerData.GlobalParam gParam, string deviceName, string os, string appName, string appVersion, string ip)
        {
            if (string.IsNullOrWhiteSpace(gParam?.SocketFastAuth))
                return new ErrorReturner(false, "Адрес FastAuth сервера не указан");

            try
            {
                var address = WebApi.EnsureHttpPrefix(gParam.SocketFastAuth);
                _webApi.FastAuthChannel?.Dispose();
                _webApi.FastAuthChannel = Grpc.Net.Client.GrpcChannel.ForAddress(address);

                var deviceInterceptor = new Shared.Auth.XDeviceClientInterceptor(deviceName: deviceName);
                var deviceIdInterceptor = new Shared.Auth.XDeviceIdInterceptor(gParam.DeviceId);
                var osInterceptor = new Shared.Auth.XOsClientInterceptor(os);
                var appInterceptor = new Shared.Auth.XAppClientInterceptor(appName, appVersion);
                var ipInterceptor = new Shared.Auth.XIpClientInterceptor(ip);
                var errorInterceptor = new Shared.Exceptions.Interceptors.ExceptionClientInterceptor();

                var invoker = BuildInvoker(_webApi.FastAuthChannel, new Interceptor[]
                {
                    deviceInterceptor, deviceIdInterceptor, osInterceptor,
                    appInterceptor, ipInterceptor, errorInterceptor
                });

                _webApi.FastAuthAC = new Proto.FastAuth.FastAuthApi.FastAuthApiClient(invoker);
                return new ErrorReturner(true);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка создания FastAuth клиента");
            }
        }

        public void DisposeFastAuthClient()
        {
            _webApi.FastAuthAC = null;
            _webApi.FastAuthChannel?.Dispose();
            _webApi.FastAuthChannel = null;
        }

        private static CallInvoker BuildInvoker(GrpcChannel channel, Interceptor[] interceptors)
        {
            CallInvoker invoker = channel.CreateCallInvoker();
            foreach (var i in interceptors)
                invoker = invoker.Intercept(i);
            return invoker;
        }
    }
}
