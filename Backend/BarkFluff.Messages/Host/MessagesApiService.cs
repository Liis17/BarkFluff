using BarkFluff.Messages.Features.CreateGroupChat;
using BarkFluff.Messages.Features.DeleteMessage;
using BarkFluff.Messages.Features.EditMessage;
using BarkFluff.Messages.Features.GetChatInfo;
using BarkFluff.Messages.Features.GetPersonChatId;
using BarkFluff.Messages.Features.KickUser;
using BarkFluff.Messages.Features.ListChatAttachments;
using BarkFluff.Messages.Features.ListChatMembers;
using BarkFluff.Messages.Features.ListChats;
using BarkFluff.Messages.Features.ListMessages;
using BarkFluff.Messages.Features.ListPinnedMessages;
using BarkFluff.Messages.Features.MarkAsRead;
using BarkFluff.Messages.Features.PinMessage;
using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Messages.Features.UnpinAll;
using BarkFluff.Messages.Features.UnpinMessage;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using BarkFluff.Shared.Exceptions.Files;
using BarkFluff.Shared.Exceptions.Messages;
using BarkFluff.Shared.Identity;

using Grpc.Core;

using MediatR;

using Microsoft.AspNetCore.Authorization;

using OutgoingMessage = BarkFluff.Messages.Features.SendMessage.OutgoingMessage;

namespace BarkFluff.Messages.Host;

[Authorize(Policy = nameof(TokenType.User))]
public class MessagesApiService : BarkFluff.Proto.Messages.MessagesApi.MessagesApiBase
{
    private readonly IMediator _mediator;

    public MessagesApiService(IMediator mediator)
    {
        _mediator = mediator;
    }

    public override async Task<ListChatsResponse> ListChats(ListChatsRequest request, ServerCallContext context)
    {

        request.Pagination ??= new PageRequest()
        {
            Size = 10,
            Offset = 0
        };

        var command = new ListChatsCommand()
        {
            Size = request.Pagination.Size,
            Skip = request.Pagination.Offset,
        };

        return await _mediator.Send(command);
    }

    public override async Task<ListMessagesResponse> ListMessages(ListMessagesRequest request, ServerCallContext context)
    {
        var parseGuidResult = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!parseGuidResult)
        {
            throw new ChatIdNotValidException();
        }

        var command = new ListMessagesCommand
        {
            ChatId = chatId,
            Count = request.Count,
            FromMessageId = request.FromMessageId,
            OffsetBefore = request.OffsetBefore,
            OffsetAfter = request.OffsetAfter,
        };

        return await _mediator.Send(command);
    }

    public override async Task<ListChatMembersResponse> ListChatMembers(ListChatMembersRequest request, ServerCallContext context)
    {
        var parseGuidResult = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!parseGuidResult)
        {
            throw new ChatIdNotValidException();
        }

        var command = new ListChatMembersCommand()
        {
            ChatId = chatId,
            Count = request.Pagination.Size,
            Skip = request.Pagination.Offset,
        };

        return await _mediator.Send(command);
    }

    public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
    {
        if (request.Message is null)
        {
            throw new MessageNotContainContextException();
        }

        var command = new SendMessageCommand()
        {
            Message = new OutgoingMessage
            {
                FileIds = request.Message.FilesIds?.Select(x => Guid.Parse(x)).ToList(),
                Text = request.Message.Text,
                ForwardedMessageId = request.Message.ForwardedMessageId == 0 ? null : request.Message.ForwardedMessageId
            },
        };

        switch (request.SourceIdCase)
        {
            case SendMessageRequest.SourceIdOneofCase.ChatId when Guid.TryParse(request.ChatId, out Guid chatId):
                command.ChatId = chatId;
                break;
            case SendMessageRequest.SourceIdOneofCase.ChatId:
                throw new ChatIdNotValidException();
            case SendMessageRequest.SourceIdOneofCase.UserId:
                command.UserId = request.UserId;
                break;
        }

        return await _mediator.Send(command);
    }

    public override async Task<CreateGroupChatResponse> CreateGroupChat(CreateGroupChatRequest request, ServerCallContext context)
    {
        Guid? pictureFileId = null;

        if (!string.IsNullOrEmpty(request.PictureFileId))
        {
            var hasValidGuid = Guid.TryParse(request.PictureFileId, out Guid pictureFileIdTmp);

            if (!hasValidGuid)
            {
                throw new NotValidFileIdException();
            }

            pictureFileId = pictureFileIdTmp;
        }

        var command = new CreateGroupChatCommand()
        {
            Title = request.Title,
            UserIds = request.UserIds.ToList(),
            PictureFileId = pictureFileId
        };

        return await _mediator.Send(command);
    }

    public override async Task<KickUserResponse> KickUser(KickUserRequest request, ServerCallContext context)
    {
        var hasValidGuid = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!hasValidGuid)
        {
            throw new ChatIdNotValidException();
        }

        var command = new KickUserCommand()
        {
            UserId = request.UserId,
            ChatId = chatId
        };

        await _mediator.Send(command);

        return new KickUserResponse();
    }

    public override async Task<MarkAsReadResponse> MarkAsRead(MarkAsReadRequest request, ServerCallContext context)
    {
        var command = new MarkAsReadCommand
        {
            MessageIds = request.MessageIds.ToList()
        };

        await _mediator.Send(command);

        return new MarkAsReadResponse();
    }

    public override async Task<GetPersonChatIdResponse> GetPersonChatId(GetPersonChatIdRequest request, ServerCallContext context)
    {
        var command = new GetPersonChatIdCommand
        {
            UserId = request.UserId
        };

        return await _mediator.Send(command);
    }

    public override async Task<GetChatInfoResponse> GetChatInfo(GetChatInfoRequest request, ServerCallContext context)
    {
        var parseGuidResult = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!parseGuidResult)
        {
            throw new ChatIdNotValidException();
        }

        var command = new GetChatInfoCommand
        {
            ChatId = chatId
        };

        return await _mediator.Send(command);
    }

    public override async Task<EditMessageResponse> EditMessage(EditMessageRequest request, ServerCallContext context)
    {
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
            MessageId = request.MessageId,
            Text = request.Text,
            FileIds = fileIds
        };

        return await _mediator.Send(command);
    }

    public override async Task<DeleteMessageResponse> DeleteMessage(DeleteMessageRequest request, ServerCallContext context)
    {
        var command = new DeleteMessageCommand
        {
            MessageId = request.MessageId
        };

        return await _mediator.Send(command);
    }

    public override async Task<PinMessageResponse> PinMessage(PinMessageRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        var command = new PinMessageCommand
        {
            ChatId = chatId,
            MessageId = request.MessageId
        };

        return await _mediator.Send(command);
    }

    public override async Task<UnpinMessageResponse> UnpinMessage(UnpinMessageRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        var command = new UnpinMessageCommand
        {
            ChatId = chatId,
            MessageId = request.MessageId
        };

        return await _mediator.Send(command);
    }

    public override async Task<ListPinnedMessagesResponse> ListPinnedMessages(ListPinnedMessagesRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        request.Pagination ??= new PageRequest
        {
            Size = 50,
            Offset = 0
        };

        var query = new ListPinnedMessagesQuery
        {
            ChatId = chatId,
            Skip = request.Pagination.Offset,
            Count = request.Pagination.Size
        };

        return await _mediator.Send(query);
    }

    public override async Task<UnpinAllResponse> UnpinAll(UnpinAllRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        var command = new UnpinAllCommand
        {
            ChatId = chatId
        };

        return await _mediator.Send(command);
    }

    public override async Task<ListChatAttachmentsResponse> ListChatAttachments(ListChatAttachmentsRequest request, ServerCallContext context)
    {
        var parseGuidResult = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!parseGuidResult)
        {
            throw new ChatIdNotValidException();
        }

        request.Pagination ??= new PageRequest()
        {
            Size = 20,
            Offset = 0
        };

        var command = new ListChatAttachmentsCommand
        {
            ChatId = chatId,
            Skip = request.Pagination.Offset,
            Size = request.Pagination.Size,
            AttachmentType = request.AttachmentType == 0 ? null : (Domain.MessageAttachmentType?)(int)request.AttachmentType,
            SortDescending = request.SortDescending
        };

        return await _mediator.Send(command);
    }
}