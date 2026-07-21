using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Queue.Federation;

using MassTransit;

namespace BarkFluff.Messages.Consumers;

// Origin-нода получила от Federation privacy-отказ ChatCreated (этап 2.5, docs/rearch/05,
// «Создание чата») — помечает свою копию чата Rejected. Дальнейшие попытки отправить в этот чат
// получают понятную ошибку FederatedDmRejected (см. SendMessageCommandHandler).
public class FederatedChatRejectedConsumer(
    ChatsStorage chatsStorage,
    MetricsCollector metrics,
    ILogger<FederatedChatRejectedConsumer> logger)
    : IConsumer<FederatedChatRejectedEvent>
{
    public async Task Consume(ConsumeContext<FederatedChatRejectedEvent> context)
    {
        var msg = context.Message;
        metrics.Increment("federated_chat_rejected_consumed");

        var marked = await chatsStorage.MarkFederatedChatRejectedAsync(msg.ChatId);
        if (marked)
        {
            logger.LogInformation("Чат {ChatId} помечен Rejected: {Reason}", msg.ChatId, msg.Reason);
        }
        else
        {
            logger.LogDebug("FederatedChatRejectedEvent для {ChatId} — чат не найден или уже не Active", msg.ChatId);
        }
    }
}
