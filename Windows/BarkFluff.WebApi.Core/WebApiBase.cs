using Grpc.Net.Client;

namespace BarkFluff.WebApi.Core
{
    /// <summary>
    /// Базовый класс для всех менеджеров WebApi. Pass-through-доступ к gRPC-клиентам
    /// и каналам, хранящимся в единственном источнике истины — <see cref="WebApi"/>.
    /// За счёт этого пересоздание клиентов в <see cref="Managers.WebApiClientManager"/>
    /// автоматически видно всем менеджерам — синхронизировать вручную не требуется.
    /// </summary>
    internal abstract class WebApiBase
    {
        private readonly WebApi _webApi;

        protected BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC => _webApi.UsersAC;
        protected BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC => _webApi.BeaconAC;
        protected BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC => _webApi.IdentityAC;
        protected BarkFluff.Proto.Files.FilesApi.FilesApiClient? FilesAC => _webApi.FilesAC;
        protected BarkFluff.Proto.Messages.MessagesApi.MessagesApiClient? MessagesAC => _webApi.MessagesAC;
        protected BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient? NavigatorAC => _webApi.NavigatorAC;
        protected BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiClient? UpdatesAC => _webApi.UpdatesAC;
        protected BarkFluff.Proto.Onliner.OnlinerApi.OnlinerApiClient? OnlinerAC => _webApi.OnlinerAC;
        protected BarkFluff.Proto.FastAuth.FastAuthApi.FastAuthApiClient? FastAuthAC => _webApi.FastAuthAC;

        protected GrpcChannel? BeaconChannel => _webApi.BeaconChannel;
        protected GrpcChannel? UserChannel => _webApi.UserChannel;
        protected GrpcChannel? IdentityChannel => _webApi.IdentityChannel;
        protected GrpcChannel? FilesChannel => _webApi.FilesChannel;
        protected GrpcChannel? MessagesChannel => _webApi.MessagesChannel;
        protected GrpcChannel? NavigatorChannel => _webApi.NavigatorChannel;
        protected GrpcChannel? UpdatesChannel => _webApi.UpdatesChannel;
        protected GrpcChannel? OnlinerChannel => _webApi.OnlinerChannel;
        protected GrpcChannel? FastAuthChannel => _webApi.FastAuthChannel;

        protected WebApiBase(WebApi webApi)
        {
            _webApi = webApi;
        }
    }
}
