using BarkFluff.GrpcServer.Metrics;

using Grpc.Core;

using MediatR;

using MessagesProto = BarkFluff.Proto.Messages;

namespace BarkFluff.Bots.Features.SendBotMessage;

public class SendBotMessageCommandHandler : IRequestHandler<SendBotMessageCommand, Proto.Shared.Message>
{
    private readonly MessagesProto.MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly MetricsCollector _metrics;

    public SendBotMessageCommandHandler(
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        MetricsCollector metrics)
    {
        _messagesClient = messagesClient;
        _metrics = metrics;
    }

    public async Task<Proto.Shared.Message> Handle(SendBotMessageCommand request, CancellationToken cancellationToken)
    {
        _metrics.Increment("bot_api_messages_sent");

        // Авторизацию отправки выполняет SendMessageServer: членство бота в чате (chat_id)
        // и запрет инициации личного чата (user_id) — бот отвечает только в существующие чаты.
        var serverRequest = new MessagesProto.SendMessageServerRequest
        {
            SenderUserId = request.BotId,
            Message = new MessagesProto.OutgoingMessage { Text = request.Text },
        };
        serverRequest.Message.FilesIds.AddRange(request.FileIds);

        if (!string.IsNullOrWhiteSpace(request.ChatId))
            serverRequest.ChatId = request.ChatId;
        else if (request.UserId is > 0)
            serverRequest.UserId = request.UserId.Value;
        else
            throw new RpcException(new Status(StatusCode.InvalidArgument, "chat_id или user_id обязателен"));

        var response = await _messagesClient.SendMessageServerAsync(serverRequest, cancellationToken: cancellationToken);

        return response.Message;
    }
}
