using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.FederationInternal;
using BarkFluff.Proto.Onliner;

namespace BarkFluff.Onliner.Services;

/// <summary>
/// Отправка typing в федерацию (этап 4.4). Fire-and-forget: локальный путь набора не должен
/// ни ждать федерацию, ни ломаться из-за неё.
/// </summary>
/// <remarks>
/// Ошибки логируются на debug, а не warning: недоступность федерации не должна засорять логи
/// на каждом heartbeat'е — а он приходит каждые 4–5 секунд, пока пользователь печатает.
/// Ретраев нет by design.
/// </remarks>
public class FederatedTypingSender
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    private readonly FederationInternalApi.FederationInternalApiClient? _federationClient;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<FederatedTypingSender> _logger;

    // Клиент резолвится через GetService, а не параметром конструктора: на ноде без федерации
    // он не зарегистрирован вовсе, и обязательный параметр уронил бы построение контейнера.
    public FederatedTypingSender(
        IServiceProvider services,
        MetricsCollector metrics,
        ILogger<FederatedTypingSender> logger)
    {
        _federationClient = services.GetService<FederationInternalApi.FederationInternalApiClient>();
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>Клиент Federation не сконфигурирован — вся федеративная ветка не активируется.</summary>
    public bool IsConfigured => _federationClient is not null;

    public async Task SendAsync(
        string chatId,
        Guid senderUuid,
        TypingAction action,
        IReadOnlyCollection<string> destinationServers,
        CancellationToken cancellationToken = default)
    {
        if (_federationClient is null || destinationServers.Count == 0)
        {
            return;
        }

        var request = new DeliverTypingOutboundRequest
        {
            ChatId = chatId,
            SenderUuid = senderUuid.ToString(),
            Action = (int)action,
        };
        request.DestinationServers.AddRange(destinationServers);

        try
        {
            await _federationClient.DeliverTypingOutboundAsync(
                request,
                deadline: DateTime.UtcNow.Add(Deadline),
                cancellationToken: cancellationToken);

            _metrics.Increment("federated_typing_sent");
        }
        catch (Exception ex)
        {
            _metrics.Increment("federated_typing_errors");
            _logger.LogDebug(ex, "Не удалось отправить federated typing в чате {ChatId}", chatId);
        }
    }
}
