using BarkFluff.GrpcServer.Metrics;

using Grpc.Core;

using MediatR;

using MessagesProto = BarkFluff.Proto.Messages;

namespace BarkFluff.Bots.Features.EditBotMessage;

public class EditBotMessageCommandHandler : IRequestHandler<EditBotMessageCommand, Proto.Shared.Message>
{
    private readonly MessagesProto.MessagesServerApi.MessagesServerApiClient _messagesClient;
    private readonly MetricsCollector _metrics;

    public EditBotMessageCommandHandler(
        MessagesProto.MessagesServerApi.MessagesServerApiClient messagesClient,
        MetricsCollector metrics)
    {
        _messagesClient = messagesClient;
        _metrics = metrics;
    }

    public async Task<Proto.Shared.Message> Handle(EditBotMessageCommand request, CancellationToken cancellationToken)
    {
        _metrics.Increment("bot_api_messages_edited");

        if (request.MessageId <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "message_id обязателен"));

        // Авторство проверяет EditMessageServer: бот правит только свои сообщения
        var serverRequest = new MessagesProto.EditMessageServerRequest
        {
            SenderUserId = request.BotId,
            MessageId = request.MessageId,
            Text = request.Text,
        };
        serverRequest.FilesIds.AddRange(request.FileIds);

        var response = await _messagesClient.EditMessageServerAsync(serverRequest, cancellationToken: cancellationToken);

        return response.Message;
    }
}
