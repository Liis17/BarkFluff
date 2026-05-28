using BarkFluff.Messages.Features.ExportData;
using BarkFluff.Messages.Persistence;

namespace BarkFluff.Messages.Tests.Features.ExportData;

public class GetUserAllMessagesQueryHandlerTests
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_UserWithMessages_ReturnsExportData()
    {
        var userId = 1L;
        var chat = await _h.SeedChat(memberUserIds: [userId, 2]);
        await _h.SeedMessage(chat.Id, userId, "msg1");
        await _h.SeedMessage(chat.Id, 2, "msg2");

        var handler = new GetUserAllMessagesQueryHandler(_h.DbContext, TestHelper.CreateLogger<GetUserAllMessagesQueryHandler>());

        var result = await handler.Handle(new GetUserAllMessagesQuery { UserId = userId }, CancellationToken.None);

        result.Messages.Should().HaveCount(2);
        result.Chats.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_UserWithNoMessages_ReturnsEmpty()
    {
        var handler = new GetUserAllMessagesQueryHandler(_h.DbContext, TestHelper.CreateLogger<GetUserAllMessagesQueryHandler>());

        var result = await handler.Handle(new GetUserAllMessagesQuery { UserId = 999 }, CancellationToken.None);

        result.Messages.Should().BeEmpty();
        result.Chats.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UserInMultipleChats_ReturnsAllChats()
    {
        var userId = 1L;
        var chat1 = await _h.SeedChat(memberUserIds: [userId, 2]);
        var chat2 = await _h.SeedChat(memberUserIds: [userId, 3]);
        await _h.SeedMessage(chat1.Id, userId, "msg1");
        await _h.SeedMessage(chat2.Id, userId, "msg2");

        var handler = new GetUserAllMessagesQueryHandler(_h.DbContext, TestHelper.CreateLogger<GetUserAllMessagesQueryHandler>());

        var result = await handler.Handle(new GetUserAllMessagesQuery { UserId = userId }, CancellationToken.None);

        result.Chats.Should().HaveCount(2);
        result.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_OnlyReturnsChatsWhereUserIsMember()
    {
        var userId = 1L;
        var userChat = await _h.SeedChat(memberUserIds: [userId, 2]);
        var otherChat = await _h.SeedChat(memberUserIds: [3, 4]);
        await _h.SeedMessage(userChat.Id, userId, "my msg");
        await _h.SeedMessage(otherChat.Id, 3, "other msg");

        var handler = new GetUserAllMessagesQueryHandler(_h.DbContext, TestHelper.CreateLogger<GetUserAllMessagesQueryHandler>());

        var result = await handler.Handle(new GetUserAllMessagesQuery { UserId = userId }, CancellationToken.None);

        result.Chats.Should().HaveCount(1);
        result.Messages.Should().HaveCount(1);
    }
}
