using BarkFluff.Messages.Features.CheckChatMembership;
using BarkFluff.Messages.Features.ExportData;
using BarkFluff.Messages.Features.GetChatMemberIds;
using BarkFluff.Messages.Features.ImportFederatedChat;
using BarkFluff.Messages.Features.ImportFederatedMessage;
using BarkFluff.Messages.Features.PostCallSystemMessage;
using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

using OutgoingMessage = BarkFluff.Messages.Features.SendMessage.OutgoingMessage;

namespace BarkFluff.Messages.Host;

[Authorize(Policy = nameof(TokenType.Service))]
public class MessagesServerApiService : MessagesServerApi.MessagesServerApiBase
{
    private readonly IMediator _mediator;

    public MessagesServerApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    // ---- Федерация (этап 2.3): ImportFederatedChat / ImportFederatedMessage ----
    // Зовёт только Federation своей ноды (TokenType.Service). Применяют валидации и хелперы
    // Features.Federation (docs/rearch/05-chat-replication.md).
    // ApplyFederatedEdit/Delete/Read + ExportChatEvents — этапы 2.4/2.6, до них Unimplemented.

    public override async Task<ImportFederatedChatResponse> ImportFederatedChat(
        ImportFederatedChatRequest request,
        ServerCallContext context)
    {
        return await _mediator.Send(new ImportFederatedChatCommand(request), context.CancellationToken);
    }

    public override async Task<ImportFederatedMessageResponse> ImportFederatedMessage(
        ImportFederatedMessageRequest request,
        ServerCallContext context)
    {
        return await _mediator.Send(new ImportFederatedMessageCommand(request), context.CancellationToken);
    }

    public override Task<GetUserAllMessagesResponse> GetUserAllMessages(
        GetUserAllMessagesRequest request,
        ServerCallContext context)
    {
        var query = new GetUserAllMessagesQuery
        {
            UserId = request.UserId
        };

        return _mediator.Send(query);
    }

    public override Task<CheckChatMembershipResponse> CheckChatMembership(
        CheckChatMembershipRequest request,
        ServerCallContext context)
    {
        var query = new CheckChatMembershipQuery
        {
            UserId = request.UserId,
            ChatIds = request.ChatIds
        };

        return _mediator.Send(query, context.CancellationToken);
    }

    public override Task<GetChatMemberIdsResponse> GetChatMemberIds(
        GetChatMemberIdsRequest request,
        ServerCallContext context)
    {
        var query = new GetChatMemberIdsQuery
        {
            ChatId = request.ChatId
        };

        return _mediator.Send(query, context.CancellationToken);
    }

    public override Task<PostCallSystemMessageResponse> PostCallSystemMessage(
        PostCallSystemMessageRequest request,
        ServerCallContext context)
    {
        Guid? chatId = null;
        long? caller = null;
        long? callee = null;

        switch (request.TargetCase)
        {
            case PostCallSystemMessageRequest.TargetOneofCase.ChatId when Guid.TryParse(request.ChatId, out var cid):
                chatId = cid;
                break;
            case PostCallSystemMessageRequest.TargetOneofCase.Person:
                caller = request.Person.CallerUserId;
                callee = request.Person.CalleeUserId;
                break;
            default:
                return Task.FromResult(new PostCallSystemMessageResponse { Posted = false });
        }

        var command = new PostCallSystemMessageCommand
        {
            ChatId = chatId,
            CallerUserId = caller,
            CalleeUserId = callee,
            SenderUserId = request.SenderUserId,
            Result = request.Result,
            DurationSeconds = request.DurationSeconds,
        };

        return _mediator.Send(command, context.CancellationToken);
    }

    public override Task<SendMessageResponse> SendMessageServer(
        SendMessageServerRequest request,
        ServerCallContext context)
    {
        // Доверяем сервисному токену вызывающего (Bots); авторизацию отправки (членство бота
        // в чате, запрет инициации чата) вызывающий выполняет ДО обращения сюда.
        if (request.SenderUserId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "sender_user_id обязателен"));
        }

        if (request.Message is null)
        {
            throw new MessageNotContainContextException();
        }

        var command = new SendMessageCommand
        {
            SenderId = request.SenderUserId,
            AllowChatCreation = request.AllowChatCreation,
            Message = new OutgoingMessage
            {
                FileIds = request.Message.FilesIds?.Select(Guid.Parse).ToList(),
                Text = request.Message.Text,
                ForwardedMessageId = request.Message.ForwardedMessageId == 0 ? null : request.Message.ForwardedMessageId
            },
        };

        switch (request.SourceIdCase)
        {
            case SendMessageServerRequest.SourceIdOneofCase.ChatId when Guid.TryParse(request.ChatId, out var chatId):
                command.ChatId = chatId;
                break;
            case SendMessageServerRequest.SourceIdOneofCase.ChatId:
                throw new ChatIdNotValidException();
            case SendMessageServerRequest.SourceIdOneofCase.UserId:
                command.UserId = request.UserId;
                break;
        }

        return _mediator.Send(command, context.CancellationToken);
    }
}
