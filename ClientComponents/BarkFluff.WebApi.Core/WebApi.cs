
using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Core.Interceptors;
using Grpc.Net.Client;

using System.Threading.Tasks;

namespace BarkFluff.WebApi.Core
{
    public class WebApi
    {
        public BarkFluff.Proto.Users.UsersApi.UsersApiClient UsersAC;
        public BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient BeaconAC;
        public BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient IdentityAC;

        public GrpcChannel BeaconChannel;
        public GrpcChannel UserChannel;
        public GrpcChannel IdentityChannel;

        public void CreateOnlyBeaconAC(GlobalParam _gParam)
        {
            CreateBeaconAC(_gParam);
        }
        public void CreateAC(GlobalParam _gParam, string _deviceName)
        {
            CreateUsersAC(_gParam);
            CreateBeaconAC(_gParam);
            CreateIdentityAC(_gParam);
            AddInterceptor(_gParam, _deviceName);
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

        private void AddInterceptor(GlobalParam _gParam, string _deviceName)
        {
            var deviceInterceptor = new Shared.Auth.XDeviceClientInterceptor(deviceName: _deviceName);
            var jwtInterceptor = new Shared.Auth.JwtClientInterceptor(string.Empty);
            IdentityChannel = GrpcChannel.ForAddress(_gParam.SocketIdentity);
            UserChannel = GrpcChannel.ForAddress(_gParam.SocketUsers);

            var identityInvoker = IdentityChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor);
            var userInvoker = UserChannel.Intercept(deviceInterceptor).Intercept(jwtInterceptor);

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
    }
}
