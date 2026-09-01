using BarkFluff.Proto.SettingsSetup;

using Grpc.Core;
using Grpc.Net.Client;

namespace BarkFluff.Setup.Setup;

public interface ISettingsSetupClient
{
    Task<GetSetupStateResponse> GetStateAsync(CancellationToken cancellationToken = default);

    Task<SaveSetupGroupResponse> SaveGroupAsync(
        string groupId,
        IReadOnlyDictionary<string, string?> values,
        string editedFrom,
        CancellationToken cancellationToken = default);

    Task<CompleteSetupResponse> CompleteAsync(
        string completedFrom,
        CancellationToken cancellationToken = default);
}

public sealed class SettingsSetupClient : ISettingsSetupClient, IDisposable
{
    private readonly SetupOptions _options;
    private readonly GrpcChannel _channel;
    private readonly SettingsSetupApi.SettingsSetupApiClient _client;

    public SettingsSetupClient(SetupOptions options)
    {
        _options = options;
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        _channel = GrpcChannel.ForAddress(options.SettingsUrl, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
        });
        _client = new SettingsSetupApi.SettingsSetupApiClient(_channel);
    }

    public async Task<GetSetupStateResponse> GetStateAsync(CancellationToken cancellationToken = default) =>
        await _client.GetSetupStateAsync(new GetSetupStateRequest(), headers: Headers(), cancellationToken: cancellationToken);

    public async Task<SaveSetupGroupResponse> SaveGroupAsync(
        string groupId,
        IReadOnlyDictionary<string, string?> values,
        string editedFrom,
        CancellationToken cancellationToken = default)
    {
        var request = new SaveSetupGroupRequest
        {
            GroupId = groupId,
            EditedBy = "setup",
            EditedFrom = editedFrom
        };
        request.Values.Add(values.Select(item => new SetupValue { FieldId = item.Key, Value = item.Value ?? string.Empty }));
        return await _client.SaveSetupGroupAsync(request, headers: Headers(), cancellationToken: cancellationToken);
    }

    public async Task<CompleteSetupResponse> CompleteAsync(
        string completedFrom,
        CancellationToken cancellationToken = default) =>
        await _client.CompleteSetupAsync(new CompleteSetupRequest
        {
            CompletedBy = "setup",
            CompletedFrom = completedFrom
        }, headers: Headers(), cancellationToken: cancellationToken);

    public void Dispose() => _channel.Dispose();

    private Metadata Headers() => new()
    {
        { "x-settings-setup-token", _options.Secret }
    };
}
