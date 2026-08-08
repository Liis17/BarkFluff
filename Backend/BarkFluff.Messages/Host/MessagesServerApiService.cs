using BarkFluff.Messages.Features.ApplyFederatedDelete;
using BarkFluff.Messages.Features.ApplyFederatedEdit;
using BarkFluff.Messages.Features.ApplyFederatedRead;
using BarkFluff.Messages.Features.CheckChatMembership;
using BarkFluff.Messages.Features.CheckFederatedPresenceAccess;
using BarkFluff.Messages.Features.CheckFedFileUserAccess;
using BarkFluff.Messages.Features.CheckFileFederationAccess;
using BarkFluff.Messages.Features.DeleteMessage;
using BarkFluff.Messages.Features.EditMessage;
using BarkFluff.Messages.Features.ExportData;
using BarkFluff.Messages.Features.GetChatInfoServer;
using BarkFluff.Messages.Features.GetChatMemberIds;
using BarkFluff.Messages.Features.ImportFederatedChat;
using BarkFluff.Messages.Features.ImportFederatedMessage;
using BarkFluff.Messages.Features.PostCallSystemMessage;
using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Files;
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

    // ---- Федерация: ImportFederatedChat / ImportFederatedMessage (2.3), ApplyFederatedEdit/Delete/Read
    // (2.4) ----. Зовёт только Federation своей ноды (TokenType.Service). Применяют валидации и хелперы
    // Features.Federation (docs/rearch/05-chat-replication.md). ExportChatEvents — этап 2.6, до него Unimplemented.

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

    public override async Task<ApplyFederatedEditResponse> ApplyFederatedEdit(
        ApplyFederatedEditRequest request,
        ServerCallContext context)
    {
        return await _mediator.Send(new ApplyFederatedEditCommand(request), context.CancellationToken);
    }

    public override async Task<ApplyFederatedDeleteResponse> ApplyFederatedDelete(
        ApplyFederatedDeleteRequest request,
        ServerCallContext context)
    {
        return await _mediator.Send(new ApplyFederatedDeleteCommand(request), context.CancellationToken);
    }

    public override async Task<ApplyFederatedReadResponse> ApplyFederatedRead(
        ApplyFederatedReadRequest request,
        ServerCallContext context)
    {
        return await _mediator.Send(new ApplyFederatedReadCommand(request), context.CancellationToken);
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
        Guid? userUuid = null;

        if (!string.IsNullOrEmpty(request.UserUuid))
        {
            if (!Guid.TryParse(request.UserUuid, out var parsed))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "user_uuid не является UUID"));
            }

            userUuid = parsed;
        }

        if (userUuid is null && request.UserId == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "требуется user_id либо user_uuid"));
        }

        var query = new CheckChatMembershipQuery
        {
            UserId = userUuid is null ? request.UserId : null,
            UserUuid = userUuid,
            ChatIds = request.ChatIds
        };

        return _mediator.Send(query, context.CancellationToken);
    }

    public override Task<CheckFileFederationAccessResponse> CheckFileFederationAccess(
        CheckFileFederationAccessRequest request,
        ServerCallContext context)
    {
        var query = new CheckFileFederationAccessQuery
        {
            FileId = request.FileId,
            RequestingServer = request.RequestingServer
        };

        return _mediator.Send(query, context.CancellationToken);
    }

    public override Task<CheckFedFileUserAccessResponse> CheckFedFileUserAccess(
        CheckFedFileUserAccessRequest request,
        ServerCallContext context)
    {
        var query = new CheckFedFileUserAccessQuery
        {
            UserId = request.UserId,
            OriginServer = request.OriginServer,
            FileId = request.FileId
        };

        return _mediator.Send(query, context.CancellationToken);
    }

    public override Task<CheckFederatedPresenceAccessResponse> CheckFederatedPresenceAccess(
        CheckFederatedPresenceAccessRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.RequestingServer))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "requesting_server обязателен"));
        }

        if (request.UserUuids.Count > CheckFederatedPresenceAccessQuery.MaxUserUuids)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"user_uuids: не более {CheckFederatedPresenceAccessQuery.MaxUserUuids} за вызов"));
        }

        var query = new CheckFederatedPresenceAccessQuery
        {
            RequestingServer = request.RequestingServer,
            UserUuids = request.UserUuids
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

    public override Task<GetChatInfoServerResponse> GetChatInfoServer(
        GetChatInfoServerRequest request,
        ServerCallContext context)
    {
        var query = new GetChatInfoServerQuery
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

    public override Task<EditMessageResponse> EditMessageServer(
        EditMessageServerRequest request,
        ServerCallContext context)
    {
        if (request.SenderUserId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "sender_user_id обязателен"));
        }

        List<Guid>? fileIds = null;

        if (request.FilesIds is { Count: > 0 })
        {
            fileIds = new List<Guid>(request.FilesIds.Count);
            foreach (var rawId in request.FilesIds)
            {
                if (!Guid.TryParse(rawId, out var fileId))
                {
                    throw new NotValidFileIdException();
                }

                fileIds.Add(fileId);
            }
        }

        var command = new EditMessageCommand
        {
            SenderId = request.SenderUserId,
            MessageId = request.MessageId,
            Text = request.Text,
            FileIds = fileIds
        };

        return _mediator.Send(command, context.CancellationToken);
    }

    public override Task<DeleteMessageResponse> DeleteMessageServer(
        DeleteMessageServerRequest request,
        ServerCallContext context)
    {
        if (request.SenderUserId <= 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "sender_user_id обязателен"));
        }

        var command = new DeleteMessageCommand
        {
            SenderId = request.SenderUserId,
            MessageId = request.MessageId
        };

        return _mediator.Send(command, context.CancellationToken);
    }
}
