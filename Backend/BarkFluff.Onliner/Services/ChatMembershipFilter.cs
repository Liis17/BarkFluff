using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Messages;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Фильтрует список чатов, оставляя только те, где пользователь действительно состоит.
/// Спрашивает Messages через MessagesServerApi.CheckChatMembership.
/// При ошибке gRPC — fail-closed: считаем, что пользователь не состоит ни в одном чате.
/// </summary>
public class ChatMembershipFilter
{
    private readonly MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<ChatMembershipFilter> _logger;

    public ChatMembershipFilter(
        MessagesServerApi.MessagesServerApiClient messagesClient,
        MetricsCollector metrics,
        ILogger<ChatMembershipFilter> logger)
    {
        _messagesClient = messagesClient;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Вернуть подмножество chatIds, в которых состоит пользователь, вместе с федеративным
    /// контекстом этих чатов (этап 4.1). Контекст потребляет typing-мост (этап 4.4);
    /// для локальных чатов он пуст, и поведение фильтрации от него не зависит.
    /// </summary>
    public async Task<ChatMembershipResult> GetMemberChatIdsAsync(
        long userId,
        IEnumerable<string> chatIds,
        CancellationToken cancellationToken = default)
    {
        var requested = chatIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();

        if (requested.Count == 0)
        {
            return ChatMembershipResult.Empty;
        }

        _metrics.Increment("membership_checks");
        try
        {
            var request = new CheckChatMembershipRequest { UserId = userId };
            request.ChatIds.AddRange(requested);

            var response = await _messagesClient.CheckChatMembershipAsync(
                request, cancellationToken: cancellationToken);

            return ChatMembershipResult.FromResponse(response);
        }
        catch (Exception ex)
        {
            _metrics.Increment("membership_check_errors");
            _logger.LogWarning(ex,
                "Failed to check chat membership for user {UserId}, defaulting to none (fail-closed)",
                userId);
            return ChatMembershipResult.Empty;
        }
    }
}

/// <summary>Результат проверки членства: чаты + федеративный контекст ответа Messages (этап 4.1).</summary>
public sealed record ChatMembershipResult(
    HashSet<string> MemberChatIds,
    Guid? RequesterUuid,
    IReadOnlyDictionary<string, IReadOnlyList<FederatedChatPeerInfo>> FederatedChats)
{
    public static readonly ChatMembershipResult Empty = new(
        [],
        null,
        new Dictionary<string, IReadOnlyList<FederatedChatPeerInfo>>());

    public static ChatMembershipResult FromResponse(CheckChatMembershipResponse response)
    {
        Guid? requesterUuid = Guid.TryParse(response.RequesterUuid, out var parsed) ? parsed : null;

        if (response.FederatedChats.Count == 0)
        {
            return new ChatMembershipResult(
                response.MemberChatIds.ToHashSet(),
                requesterUuid,
                Empty.FederatedChats);
        }

        var federated = new Dictionary<string, IReadOnlyList<FederatedChatPeerInfo>>();

        foreach (var chat in response.FederatedChats)
        {
            var peers = new List<FederatedChatPeerInfo>();

            foreach (var peer in chat.Peers)
            {
                if (Guid.TryParse(peer.UserUuid, out var peerUuid) && !string.IsNullOrEmpty(peer.ServerName))
                {
                    peers.Add(new FederatedChatPeerInfo(peerUuid, peer.ServerName));
                }
            }

            federated[chat.ChatId] = peers;
        }

        return new ChatMembershipResult(response.MemberChatIds.ToHashSet(), requesterUuid, federated);
    }
}

public sealed record FederatedChatPeerInfo(Guid UserUuid, string ServerName);
