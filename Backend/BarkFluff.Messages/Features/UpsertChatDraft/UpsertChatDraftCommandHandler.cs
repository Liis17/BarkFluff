using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Messages.Features.UpsertChatDraft;

public class UpsertChatDraftCommandHandler : IRequestHandler<UpsertChatDraftCommand, UpsertChatDraftResponse>
{
    private const int MaxTextLength = 4096;

    private readonly ChatsStorage _chatsStorage;
    private readonly ChatDraftsStorage _chatDraftsStorage;
    private readonly MessagesStorage _messagesStorage;
    private readonly UserContext _userContext;

    public UpsertChatDraftCommandHandler(
        ChatsStorage chatsStorage,
        ChatDraftsStorage chatDraftsStorage,
        MessagesStorage messagesStorage,
        UserContext userContext)
    {
        _chatsStorage = chatsStorage;
        _chatDraftsStorage = chatDraftsStorage;
        _messagesStorage = messagesStorage;
        _userContext = userContext;
    }

    public async Task<UpsertChatDraftResponse> Handle(UpsertChatDraftCommand request, CancellationToken cancellationToken)
    {
        if (request.Text.Length > MaxTextLength)
            throw new MessageTextTooLongException();

        var chat = await _chatsStorage.GetChat(request.ChatId) ?? throw new ChatNotFoundException();
        if (chat.Type != ChatType.Regular)
            throw new ChatNotRegularException();
        if (!await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId))
            throw new NoAccessToChatException();

        if (request.ReplyToMessageId.HasValue)
        {
            var reply = await _messagesStorage.GetMessageById(request.ReplyToMessageId.Value);
            if (reply is null || reply.ChatId != request.ChatId || reply.IsDeleted)
                throw new MessageNotFoundException();
        }

        var draft = await _chatDraftsStorage.UpsertAsync(
            request.ChatId,
            _userContext.UserId,
            request.Text,
            request.ReplyToMessageId);

        return new UpsertChatDraftResponse
        {
            Draft = new ChatDraftInfo
            {
                Text = draft.Text,
                ReplyToMessageId = draft.ReplyToMessageId ?? 0,
                Revision = draft.Revision.ToString(),
                UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(draft.UpdatedAt, DateTimeKind.Utc))
            }
        };
    }
}
