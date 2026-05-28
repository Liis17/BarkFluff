using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.ListPrivateMessages;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Tests.Features.ListPrivateMessages;

public class ListPrivateMessagesQueryHandlerTests
{
    private readonly TestHelper _h = new();

    private ListPrivateMessagesQueryHandler CreateHandler(long userId)
    {
        return new ListPrivateMessagesQueryHandler(
            _h.ChatsStorage,
            _h.EncryptedMessagesStorage,
            _h.CreateUserContext(userId));
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsMessages()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [userId, 2], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        await _h.SeedEncryptedMessage(chat.Id, userId, Guid.NewGuid());
        await _h.SeedEncryptedMessage(chat.Id, 2, Guid.NewGuid());
        var handler = CreateHandler(userId);

        var result = await handler.Handle(new ListPrivateMessagesQuery
        {
            ChatId = chat.Id,
            OffsetBefore = 50
        }, CancellationToken.None);

        result.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ChatNotFound_ThrowsChatNotFoundException()
    {
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new ListPrivateMessagesQuery
        {
            ChatId = Guid.NewGuid(),
            OffsetBefore = 50
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotFoundException>();
    }

    [Fact]
    public async Task Handle_NotPrivateChat_ThrowsChatNotPrivateException()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new ListPrivateMessagesQuery
        {
            ChatId = chat.Id,
            OffsetBefore = 50
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ChatNotPrivateException>();
    }

    [Fact]
    public async Task Handle_NotMember_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(type: Domain.ChatType.Private, memberUserIds: [99, 100], kdfSalt: new byte[32], passphraseVerifier: new byte[32]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new ListPrivateMessagesQuery
        {
            ChatId = chat.Id,
            OffsetBefore = 50
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }
}
