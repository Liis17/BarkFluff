using BarkFluff.Messages.Features.AcceptPrivateChat;
using BarkFluff.Messages.Features.AcceptSecretChatInvite;
using BarkFluff.Messages.Features.AckSecretMessage;
using BarkFluff.Messages.Features.AddUser;
using BarkFluff.Messages.Features.CreateGroupChat;
using BarkFluff.Messages.Features.CreatePrivateChat;
using BarkFluff.Messages.Features.DeleteChatDraft;
using BarkFluff.Messages.Features.DeleteMessage;
using BarkFluff.Messages.Features.DeletePrivateMessage;
using BarkFluff.Messages.Features.EditMessage;
using BarkFluff.Messages.Features.EditPrivateMessage;
using BarkFluff.Messages.Features.GetChatInfo;
using BarkFluff.Messages.Features.GetChatDraft;
using BarkFluff.Messages.Features.GetPersonChatId;
using BarkFluff.Messages.Features.KickUser;
using BarkFluff.Messages.Features.ListChatAttachments;
using BarkFluff.Messages.Features.ListChatMembers;
using BarkFluff.Messages.Features.ListChats;
using BarkFluff.Messages.Features.ListMessages;
using BarkFluff.Messages.Features.ListPinnedMessages;
using BarkFluff.Messages.Features.ListPrivateMessages;
using BarkFluff.Messages.Features.MarkAsRead;
using BarkFluff.Messages.Features.MarkPrivateMessagesAsRead;
using BarkFluff.Messages.Features.PinMessage;
using BarkFluff.Messages.Features.RejectPrivateChat;
using BarkFluff.Messages.Features.RejectSecretChatInvite;
using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Messages.Features.SendPrivateMessage;
using BarkFluff.Messages.Features.SendSecretChatInvite;
using BarkFluff.Messages.Features.SendSecretMessage;
using BarkFluff.Messages.Features.UnpinAll;
using BarkFluff.Messages.Features.UnpinMessage;
using BarkFluff.Messages.Features.UpdateGroupChat;
using BarkFluff.Messages.Features.UpsertChatDraft;
using BarkFluff.Messages.Mapping;
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
            Message = request.Message.ToCommandMessage(),
        };

        if (!string.IsNullOrWhiteSpace(request.ClientOperationId))
        {
            if (!Guid.TryParse(request.ClientOperationId, out var clientOperationId))
                throw new ClientOperationIdNotValidException();
            command.ClientOperationId = clientOperationId;
        }

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
            case SendMessageRequest.SourceIdOneofCase.UserUuid when Guid.TryParse(request.UserUuid, out Guid userUuid):
                command.UserUuid = userUuid;
                break;
            case SendMessageRequest.SourceIdOneofCase.UserUuid:
                throw new ChatIdNotValidException();
        }

        return await _mediator.Send(command, context.CancellationToken);
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

    public override async Task<AddUserResponse> AddUser(AddUserRequest request, ServerCallContext context)
    {
        var hasValidGuid = Guid.TryParse(request.ChatId, out Guid chatId);

        if (!hasValidGuid)
        {
            throw new ChatIdNotValidException();
        }

        var command = new AddUserCommand()
        {
            UserId = request.UserId,
            ChatId = chatId
        };

        await _mediator.Send(command);

        return new AddUserResponse();
    }

    public override async Task<UpdateGroupChatResponse> UpdateGroupChat(UpdateGroupChatRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out Guid chatId))
        {
            throw new ChatIdNotValidException();
        }

        Guid? pictureFileId = null;

        if (!string.IsNullOrEmpty(request.PictureFileId))
        {
            if (!Guid.TryParse(request.PictureFileId, out Guid pictureFileIdTmp))
            {
                throw new NotValidFileIdException();
            }

            pictureFileId = pictureFileIdTmp;
        }

        var command = new UpdateGroupChatCommand()
        {
            ChatId = chatId,
            Title = string.IsNullOrEmpty(request.Title) ? null : request.Title,
            PictureFileId = pictureFileId
        };

        return await _mediator.Send(command);
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
        // request.UserId — plain proto3 int64 (не oneof/optional), при незаполненном поле приходит 0,
        // а не null. Без этой проверки request.UserId == 0 ? null : ... federated-ветка по UserUuid
        // в GetPersonChatIdCommandHandler (гейт "UserId is null") недостижима.
        var command = new GetPersonChatIdCommand
        {
            UserId = request.UserId == 0 ? null : request.UserId,
        };

        if (Guid.TryParse(request.UserUuid, out var userUuid))
            command.UserUuid = userUuid;

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

    public override async Task<GetChatDraftResponse> GetChatDraft(GetChatDraftRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        return await _mediator.Send(new GetChatDraftQuery { ChatId = chatId });
    }

    public override async Task<UpsertChatDraftResponse> UpsertChatDraft(UpsertChatDraftRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        return await _mediator.Send(new UpsertChatDraftCommand
        {
            ChatId = chatId,
            Text = request.Text,
            ReplyToMessageId = request.ReplyToMessageId == 0 ? null : request.ReplyToMessageId
        });
    }

    public override async Task<DeleteChatDraftResponse> DeleteChatDraft(DeleteChatDraftRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }
        if (!Guid.TryParse(request.ExpectedRevision, out var revision))
        {
            return new DeleteChatDraftResponse();
        }

        var deleted = await _mediator.Send(new DeleteChatDraftCommand
        {
            ChatId = chatId,
            Revision = revision
        });

        return new DeleteChatDraftResponse { Deleted = deleted };
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
            SortDescending = request.SortDescending,
            FileNameQuery = request.FileNameQuery
        };

        return await _mediator.Send(command);
    }

    // -- Приватные чаты ------------------------------------------------------

    public override async Task<CreatePrivateChatResponse> CreatePrivateChat(CreatePrivateChatRequest request, ServerCallContext context)
    {
        var command = new CreatePrivateChatCommand
        {
            PeerUserId = request.PeerUserId,
            KdfSalt = request.KdfSalt.ToByteArray(),
            PassphraseVerifier = request.PassphraseVerifier.ToByteArray()
        };

        return await _mediator.Send(command);
    }

    public override async Task<AcceptPrivateChatResponse> AcceptPrivateChat(AcceptPrivateChatRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        return await _mediator.Send(new AcceptPrivateChatCommand { ChatId = chatId });
    }

    public override async Task<RejectPrivateChatResponse> RejectPrivateChat(RejectPrivateChatRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        return await _mediator.Send(new RejectPrivateChatCommand { ChatId = chatId });
    }

    public override async Task<SendPrivateMessageResponse> SendPrivateMessage(SendPrivateMessageRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        var command = new SendPrivateMessageCommand
        {
            ChatId = chatId,
            Ciphertext = request.Ciphertext.ToByteArray(),
            Nonce = request.Nonce.ToByteArray(),
            AssociatedData = request.AssociatedData.ToByteArray()
        };

        return await _mediator.Send(command);
    }

    public override async Task<ListPrivateMessagesResponse> ListPrivateMessages(ListPrivateMessagesRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        var query = new ListPrivateMessagesQuery
        {
            ChatId = chatId,
            FromMessageId = request.FromMessageId,
            OffsetBefore = request.OffsetBefore,
            OffsetAfter = request.OffsetAfter
        };

        return await _mediator.Send(query);
    }

    public override async Task<EditPrivateMessageResponse> EditPrivateMessage(EditPrivateMessageRequest request, ServerCallContext context)
    {
        var command = new EditPrivateMessageCommand
        {
            MessageId = request.MessageId,
            Ciphertext = request.Ciphertext.ToByteArray(),
            Nonce = request.Nonce.ToByteArray(),
            AssociatedData = request.AssociatedData.ToByteArray()
        };

        return await _mediator.Send(command);
    }

    public override async Task<DeletePrivateMessageResponse> DeletePrivateMessage(DeletePrivateMessageRequest request, ServerCallContext context)
    {
        var command = new DeletePrivateMessageCommand
        {
            MessageId = request.MessageId
        };

        return await _mediator.Send(command);
    }

    public override async Task<MarkPrivateMessagesAsReadResponse> MarkPrivateMessagesAsRead(
        MarkPrivateMessagesAsReadRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ChatId, out var chatId))
        {
            throw new ChatIdNotValidException();
        }

        await _mediator.Send(new MarkPrivateMessagesAsReadCommand
        {
            ChatId = chatId,
            LastReadMessageId = request.LastReadMessageId,
        });

        return new MarkPrivateMessagesAsReadResponse();
    }

    // -- Секретные чаты ------------------------------------------------------

    public override async Task<SendSecretChatInviteResponse> SendSecretChatInvite(SendSecretChatInviteRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RecipientDeviceId, out var recipientDeviceId))
        {
            throw new DeviceIdRequiredException();
        }

        var command = new SendSecretChatInviteCommand
        {
            RecipientUserId = request.RecipientUserId,
            RecipientDeviceId = recipientDeviceId,
            InitialEnvelope = request.InitialEnvelope.ToByteArray()
        };

        return await _mediator.Send(command);
    }

    public override async Task<AcceptSecretChatInviteResponse> AcceptSecretChatInvite(AcceptSecretChatInviteRequest request, ServerCallContext context)
    {
        var command = new AcceptSecretChatInviteCommand
        {
            InviteId = request.InviteId,
            ResponseEnvelope = request.ResponseEnvelope.ToByteArray()
        };

        return await _mediator.Send(command);
    }

    public override async Task<RejectSecretChatInviteResponse> RejectSecretChatInvite(RejectSecretChatInviteRequest request, ServerCallContext context)
    {
        var command = new RejectSecretChatInviteCommand
        {
            InviteId = request.InviteId
        };

        return await _mediator.Send(command);
    }

    public override async Task<SendSecretMessageResponse> SendSecretMessage(SendSecretMessageRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RecipientDeviceId, out var recipientDeviceId))
        {
            throw new DeviceIdRequiredException();
        }

        var command = new SendSecretMessageCommand
        {
            RecipientUserId = request.RecipientUserId,
            RecipientDeviceId = recipientDeviceId,
            Envelope = request.Envelope.ToByteArray()
        };

        return await _mediator.Send(command);
    }

    public override async Task<AckSecretMessageResponse> AckSecretMessage(AckSecretMessageRequest request, ServerCallContext context)
    {
        var command = new AckSecretMessageCommand
        {
            MessageId = request.MessageId
        };

        return await _mediator.Send(command);
    }
}
