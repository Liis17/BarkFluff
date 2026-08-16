using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.Client.Core.Models;

public sealed record NodeServiceConfiguration(
    NodeProfile Profile,
    string IdentityEndpoint,
    string UsersEndpoint,
    string FilesEndpoint,
    string MessagesEndpoint,
    string UpdatesEndpoint,
    string OnlinerEndpoint,
    string FastAuthEndpoint,
    string CallsEndpoint,
    string LivekitUrl,
    string ServerDnsName,
    bool FederationEnabled,
    string ColorLight,
    string ColorMain,
    string ColorDark,
    // Необязательное поле: у сохранённых ранее конфигураций его нет, и файлы тогда
    // качаются по ссылкам Files как раньше.
    string FilesMediaEndpoint = "")
{
    public static NodeServiceConfiguration From(NodeConnection connection)
    {
        var parameters = connection.ConnectionParameters;
        return new NodeServiceConfiguration(
            connection.Profile,
            parameters.SocketIdentity,
            parameters.SocketUsers,
            parameters.SocketFiles,
            parameters.SocketMessages,
            parameters.SocketUpdates,
            parameters.SocketOnliner,
            parameters.SocketFastAuth,
            parameters.SocketCalls,
            parameters.LivekitUrl,
            parameters.ServerDnsName,
            parameters.FederationEnabled,
            parameters.Colors.LiteHex,
            parameters.Colors.MainHex,
            parameters.Colors.HardHex,
            parameters.SocketFilesMedia);
    }

    public NodeConnection ToConnection() => new(
        Profile,
        new GlobalParam
        {
            SocketBeacon = Profile.BeaconAddress,
            ServerName = Profile.Name,
            ServerDescription = Profile.Description,
            SocketIdentity = IdentityEndpoint,
            SocketUsers = UsersEndpoint,
            SocketFiles = FilesEndpoint,
            SocketFilesMedia = FilesMediaEndpoint,
            SocketMessages = MessagesEndpoint,
            SocketUpdates = UpdatesEndpoint,
            SocketOnliner = OnlinerEndpoint,
            SocketFastAuth = FastAuthEndpoint,
            SocketCalls = CallsEndpoint,
            LivekitUrl = LivekitUrl,
            ServerDnsName = ServerDnsName,
            FederationEnabled = FederationEnabled,
            Colors = new ClientColors
            {
                LiteHex = ColorLight,
                MainHex = ColorMain,
                HardHex = ColorDark
            }
        });
}
