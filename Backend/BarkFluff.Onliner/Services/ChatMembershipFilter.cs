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
    /// Вернуть подмножество chatIds, в которых состоит пользователь.
    /// </summary>
    public async Task<HashSet<string>> GetMemberChatIdsAsync(
        long userId,
        IEnumerable<string> chatIds,
        CancellationToken cancellationToken = default)
    {
        var requested = chatIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();

        if (requested.Count == 0)
        {
            return [];
        }

        _metrics.Increment("membership_checks");
        try
        {
            var request = new CheckChatMembershipRequest { UserId = userId };
            request.ChatIds.AddRange(requested);

            var response = await _messagesClient.CheckChatMembershipAsync(
                request, cancellationToken: cancellationToken);

            return response.MemberChatIds.ToHashSet();
        }
        catch (Exception ex)
        {
            _metrics.Increment("membership_check_errors");
            _logger.LogWarning(ex,
                "Failed to check chat membership for user {UserId}, defaulting to none (fail-closed)",
                userId);
            return [];
        }
    }
}
