using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.DeleteChatDraft;

public class DeleteChatDraftCommandHandler : IRequestHandler<DeleteChatDraftCommand, bool>
{
    private readonly ChatsStorage _chatsStorage;
    private readonly ChatDraftsStorage _chatDraftsStorage;
    private readonly UserContext _userContext;

    public DeleteChatDraftCommandHandler(
        ChatsStorage chatsStorage,
        ChatDraftsStorage chatDraftsStorage,
        UserContext userContext)
    {
        _chatsStorage = chatsStorage;
        _chatDraftsStorage = chatDraftsStorage;
        _userContext = userContext;
    }

    public async Task<bool> Handle(DeleteChatDraftCommand request, CancellationToken cancellationToken)
    {
        var chat = await _chatsStorage.GetChat(request.ChatId) ?? throw new ChatNotFoundException();
        if (chat.Type != ChatType.Regular)
            throw new ChatNotRegularException();
        if (!await _chatsStorage.CheckAccessToChat(request.ChatId, _userContext.UserId))
            throw new NoAccessToChatException();

        return await _chatDraftsStorage.DeleteIfRevisionMatchesAsync(
            request.ChatId,
            _userContext.UserId,
            request.Revision);
    }
}
