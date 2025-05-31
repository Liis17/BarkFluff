
using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Core.Interceptors;
using Grpc.Net.Client;

using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;

namespace BarkFluff.WebApi.Core
{
    public class WebApi
    {
        public BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC;
        public BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC;
        public BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC;

        public GrpcChannel? BeaconChannel;
        public GrpcChannel? UserChannel;
        public GrpcChannel? IdentityChannel;

        public void CreateOnlyBeaconAC(GlobalParam gParam)
        {
            CreateBeaconAC(gParam);
        }
        public void CreateAC(GlobalParam gParam, string deviceName, string os, string appName, string appVersion, string ip)
        {
            CreateUsersAC(gParam);
            CreateBeaconAC(gParam);
            CreateIdentityAC(gParam);
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

            IdentityAC = new BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient(identityInvoker);
            UsersAC = new BarkFluff.Proto.Users.UsersApi.UsersApiClient(userInvoker);

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

        public async Task CreateAccount()
        {

        }
    }
}
