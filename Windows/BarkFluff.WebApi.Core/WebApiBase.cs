using Grpc.Net.Client;

namespace BarkFluff.WebApi.Core
{
    /// <summary>
    /// Базовый класс для всех менеджеров WebApi, предоставляющий доступ к API клиентам и каналам.
    /// </summary>
    internal abstract class WebApiBase
    {
        protected BarkFluff.Proto.Users.UsersApi.UsersApiClient? UsersAC { get; set; }
        protected BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? BeaconAC { get; set; }
        protected BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? IdentityAC { get; set; }
        protected BarkFluff.Proto.Files.FilesApi.FilesApiClient? FilesAC { get; set; }
        protected BarkFluff.Proto.Messages.MessagesApi.MessagesApiClient? MessagesAC { get; set; }
        protected BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient? NavigatorAC { get; set; }
        protected BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiClient? UpdatesAC { get; set; }
        protected BarkFluff.Proto.Onliner.OnlinerApi.OnlinerApiClient? OnlinerAC { get; set; }

        protected GrpcChannel? BeaconChannel { get; set; }
        protected GrpcChannel? UserChannel { get; set; }
        protected GrpcChannel? IdentityChannel { get; set; }
        protected GrpcChannel? FilesChannel { get; set; }
        protected GrpcChannel? MessagesChannel { get; set; }
        protected GrpcChannel? NavigatorChannel { get; set; }
        protected GrpcChannel? UpdatesChannel { get; set; }
        protected GrpcChannel? OnlinerChannel { get; set; }

        protected WebApiBase(WebApi webApi)
        {
            // Пустой конструктор, клиенты будут устанавливаться через SetClients
        }

        /// <summary>
        /// Устанавливает ссылки на API клиенты и каналы из главного WebApi.
        /// </summary>
        internal void SetClients(
            BarkFluff.Proto.Users.UsersApi.UsersApiClient? usersAC,
            BarkFluff.Proto.Beacon.BeaconApi.BeaconApiClient? beaconAC,
            BarkFluff.Proto.Identity.IdentityApi.IdentityApiClient? identityAC,
            BarkFluff.Proto.Files.FilesApi.FilesApiClient? filesAC,
            BarkFluff.Proto.Messages.MessagesApi.MessagesApiClient? messagesAC,
            BarkFluff.Proto.Navigator.NavigatorApi.NavigatorApiClient? navigatorAC,
            BarkFluff.Proto.Updates.UpdatesApi.UpdatesApiClient? updatesAC,
            BarkFluff.Proto.Onliner.OnlinerApi.OnlinerApiClient? onlinerAC,
            GrpcChannel? beaconChannel,
            GrpcChannel? userChannel,
            GrpcChannel? identityChannel,
            GrpcChannel? filesChannel,
            GrpcChannel? messagesChannel,
            GrpcChannel? navigatorChannel,
            GrpcChannel? updatesChannel,
            GrpcChannel? onlinerChannel)
        {
            UsersAC = usersAC;
            BeaconAC = beaconAC;
            IdentityAC = identityAC;
            FilesAC = filesAC;
            MessagesAC = messagesAC;
            NavigatorAC = navigatorAC;
            UpdatesAC = updatesAC;
            OnlinerAC = onlinerAC;

            BeaconChannel = beaconChannel;
            UserChannel = userChannel;
            IdentityChannel = identityChannel;
            FilesChannel = filesChannel;
            MessagesChannel = messagesChannel;
            NavigatorChannel = navigatorChannel;
            UpdatesChannel = updatesChannel;
            OnlinerChannel = onlinerChannel;
        }
    }
}
