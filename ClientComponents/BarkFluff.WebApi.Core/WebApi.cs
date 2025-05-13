
using BarkFluff.WebApi.Core.MessengerData;

using Grpc.Core.Interceptors;
using Grpc.Net.Client;

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

        public void CreateBeaconAC(GlobalParam _gParam)
        {
            _gParam.SocketBeacon = EnsureHttpPrefix(_gParam.SocketBeacon);
            BeaconChannel = GrpcChannel.ForAddress(_gParam.SocketBeacon);
            BeaconAC = new BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient(BeaconChannel);
        }

        public void AddInterceptor(GlobalParam _gParam, string _deviceName)
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
    }
}
