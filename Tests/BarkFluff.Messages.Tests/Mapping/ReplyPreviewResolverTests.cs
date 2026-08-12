using BarkFluff.Messages.Domain;
using BarkFluff.Proto.Users;

namespace BarkFluff.Messages.Tests.Mapping;

/// <summary>
/// Ради чего reply перестал быть снапшотом: цитата резолвится на каждой выдаче, поэтому она
/// живая. Раньше копия текста лежала внутри отвечающего сообщения и переживала и правку, и
/// удаление оригинала.
/// </summary>
public class ReplyPreviewResolverTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient = new();

    public ReplyPreviewResolverTests()
    {
        _usersClient.Setup(c => c.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new ListByIdsResponse
            {
                Users = { new User { Id = 2, FirstName = "Jamie", LastName = "Lee" } }
            }));
    }

    [Fact]
    public async Task Preview_ReflectsEditedOriginal()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var original = await _h.SeedMessage(chat.Id, senderId: 2, text: "before edit");
        var reply = await SeedReply(chat.Id, original.Id);

        original.Content!.Text = "after edit";
        await _h.DbContext.SaveChangesAsync();

        var previews = await _h.CreateReplyPreviewResolver(_usersClient.Object).ResolveAsync([reply]);

        previews[original.Id].TextPreview.Should().Be("after edit");
        previews[original.Id].SenderName.Should().Be("Jamie Lee");
    }

    [Fact]
    public async Task Preview_OfDeletedOriginal_HidesContentButKeepsMarker()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var original = await _h.SeedMessage(chat.Id, senderId: 2, text: "secret", isDeleted: true);
        var reply = await SeedReply(chat.Id, original.Id);

        var previews = await _h.CreateReplyPreviewResolver(_usersClient.Object).ResolveAsync([reply]);

        // Цитата не должна становиться способом прочитать удалённое сообщение.
        previews[original.Id].IsDeleted.Should().BeTrue();
        previews[original.Id].TextPreview.Should().BeEmpty();
        previews[original.Id].SenderName.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_TruncatesLongText()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var original = await _h.SeedMessage(chat.Id, senderId: 2, text: new string('x', 4096));
        var reply = await SeedReply(chat.Id, original.Id);

        var previews = await _h.CreateReplyPreviewResolver(_usersClient.Object).ResolveAsync([reply]);

        previews[original.Id].TextPreview.Should().HaveLength(200);
    }

    [Fact]
    public async Task Resolve_WithoutReplies_DoesNotCallUsers()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var plain = await _h.SeedMessage(chat.Id, senderId: 2);

        var previews = await _h.CreateReplyPreviewResolver(_usersClient.Object).ResolveAsync([plain]);

        previews.Should().BeEmpty();
        _usersClient.Verify(
            c => c.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null, null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resolve_ManyRepliesToSameOriginal_QueriesUsersOnce()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2]);
        var original = await _h.SeedMessage(chat.Id, senderId: 2, text: "anchor");

        var replies = new List<Message>();
        for (var i = 0; i < 10; i++)
            replies.Add(await SeedReply(chat.Id, original.Id));

        await _h.CreateReplyPreviewResolver(_usersClient.Object).ResolveAsync(replies);

        // Страница ответов не должна превращаться в N+1 — этим резолвер и оправдан.
        _usersClient.Verify(
            c => c.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private async Task<Message> SeedReply(Guid chatId, long replyToMessageId)
    {
        var reply = await _h.SeedMessage(chatId, senderId: 1, text: "answer");
        reply.ReplyToMessageId = replyToMessageId;
        await _h.DbContext.SaveChangesAsync();
        return reply;
    }
}
