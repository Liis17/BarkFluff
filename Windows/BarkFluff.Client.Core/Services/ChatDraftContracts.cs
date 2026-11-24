using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.WebApi.Core.MessengerData;
using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.Core.Services;

public sealed record ChatDraft(string Text, long ReplyId, ChatType Type,
    string Revision = "", bool Pending = false, bool Deleted = false, bool LocalOnly = false)
{
    public bool HasContent => !Deleted && (!string.IsNullOrWhiteSpace(Text) || ReplyId != 0);
}

public interface IChatDraftStore
{
    Dictionary<string, ChatDraft> Load(string node, long userId);
    void Save(string node, long userId, string chatId, ChatDraft draft);
    void RemoveNonRegularDrafts(string node, long userId);
}

public interface IChatDraftRemote
{
    Task<GetChatDraftResponse?> GetAsync((string Node, long UserId) scope, string chatId, CancellationToken token);
    Task<UpsertChatDraftResponse?> UpsertAsync((string Node, long UserId) scope, string chatId, string text, long replyId, CancellationToken token);
    Task<DeleteChatDraftResponse?> DeleteAsync((string Node, long UserId) scope, string chatId, string revision, CancellationToken token);
}

public sealed class ChatDraftRemote(WebApiClient api, IClientSession session, IAuthenticationService? authentication = null) : IChatDraftRemote
{
    private GlobalParam? GetParameters((string Node, long UserId) scope) =>
        authentication?.IsRestoredSessionOffline != true && session.CurrentConnection is { } connection && connection.Profile.BeaconAddress == scope.Node &&
        connection.ConnectionParameters.UserId == scope.UserId
            ? connection.ConnectionParameters
            : null;

    public Task<GetChatDraftResponse?> GetAsync((string Node, long UserId) scope, string chatId, CancellationToken token) =>
        GetParameters(scope) is { } parameters
            ? api.GetChatDraft(parameters, chatId, token)
            : Task.FromResult<GetChatDraftResponse?>(null);

    public Task<UpsertChatDraftResponse?> UpsertAsync((string Node, long UserId) scope, string chatId, string text, long replyId, CancellationToken token) =>
        GetParameters(scope) is { } parameters
            ? api.UpsertChatDraft(parameters, chatId, text, replyId, token)
            : Task.FromResult<UpsertChatDraftResponse?>(null);

    public Task<DeleteChatDraftResponse?> DeleteAsync((string Node, long UserId) scope, string chatId, string revision, CancellationToken token) =>
        GetParameters(scope) is { } parameters
            ? api.DeleteChatDraft(parameters, chatId, revision, token)
            : Task.FromResult<DeleteChatDraftResponse?>(null);
}
