using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using Google.Protobuf.WellKnownTypes;

using MediatR;

namespace BarkFluff.Messages.Features.GetChatDraft;

public class GetChatDraftQueryHandler : IRequestHandler<GetChatDraftQuery, GetChatDraftResponse>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatDraftsStorage _chatDraftsStorage;
    private readonly UserContext _userContext;

    public GetChatDraftQueryHandler(
        ChatsStorage chatsStorage,
        ChatDraftsStorage chatDraftsStorage,
        UserContext userContext)
    {
        _chatsStorage = chatsStorage;
        _chatDraftsStorage = chatDraftsStorage;
        _userContext = userContext;
    }

    public async Task<GetChatDraftResponse> Handle(GetChatDraftQuery request, CancellationToken cancellationToken)
    {
        var chat = await _chatsStorage.GetChat(request.ChatId) ?? throw new ChatNotFoundException();
        if (chat.Type != ChatType.Regular)
            throw new ChatNotRegularException();
        if (!await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId))
            throw new NoAccessToChatException();

        var draft = await _chatDraftsStorage.GetAsync(request.ChatId, _userContext.UserId);
        if (draft is null)
            return new GetChatDraftResponse();

        return new GetChatDraftResponse
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
