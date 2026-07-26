using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Bots;

using Grpc.Core;

using MediatR;

using MessagesProto = BarkFluff.Proto.Messages;

namespace BarkFluff.Bots.Features.DeleteBotMessage;

public class DeleteBotMessageCommandHandler : IRequestHandler<DeleteBotMessageCommand, DeleteMessageResponse>
{
    private readonly MessagesProto.MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly MetricsCollector _metrics;

    public DeleteBotMessageCommandHandler(
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        MetricsCollector metrics)
    {
        _messagesClient = messagesClient;
        _metrics = metrics;
    }

    public async Task<DeleteMessageResponse> Handle(DeleteBotMessageCommand request, CancellationToken cancellationToken)
    {
        _metrics.Increment("bot_api_messages_deleted");

        if (request.MessageId <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "message_id обязателен"));

        // Авторство проверяет DeleteMessageServer: бот удаляет только свои сообщения
        await _messagesClient.DeleteMessageServerAsync(new MessagesProto.DeleteMessageServerRequest
        {
            SenderUserId = request.BotId,
            MessageId = request.MessageId,
        }, cancellationToken: cancellationToken);

        return new DeleteMessageResponse();
    }
}
