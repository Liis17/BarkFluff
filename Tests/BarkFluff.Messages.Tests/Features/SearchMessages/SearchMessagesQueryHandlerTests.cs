using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Features.SearchMessages;
using BarkFluff.Messages.Persistence.Services.Dtos;
using BarkFluff.Proto.Users;

namespace BarkFluff.Messages.Tests.Features.SearchMessages;

public class SearchMessagesQueryHandlerTests
{
    [Fact]
    public async Task FilterOnlySearchReturnsMessagesFromVisibleRegularChats()
    {
        var helper = new TestHelper();
        var visible = await helper.SeedChat(isGroupChat: true, title: "Команда", memberUserIds: [1, 2]);
        var foreign = await helper.SeedChat(memberUserIds: [99, 100]);
        var privateChat = await helper.SeedChat(type: ChatType.Private, memberUserIds: [1, 2]);
        var hidden = await helper.SeedChat(memberUserIds: [1, 2]);
        hidden.Members!.Single(member => member.UserId == 1).HiddenAt = DateTime.UtcNow;
        var time = DateTime.UtcNow.AddMinutes(-1);
        var expected = await helper.SeedMessage(visible.Id, 2, "Проверяем фильтр", sentAt: time);
        await helper.SeedMessage(foreign.Id, 2, "Чужое сообщение", sentAt: time);
        await helper.SeedMessage(privateChat.Id, 2, "Не обычный чат", sentAt: time);
        await helper.SeedMessage(hidden.Id, 2, "Скрытый чат", sentAt: time);
        await helper.SeedMessage(visible.Id, 2, isDeleted: true, sentAt: time);
        await helper.SeedMessage(visible.Id, 2, type: MessageContentType.System, sentAt: time);
        var users = new Mock<UsersServerApi.UsersServerApiClient>();
        users.Setup(client => client.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new ListByIdsResponse
            {
                Users = { new User { Id = 2, FirstName = "Анна", LastName = "Иванова" } },
            }));
        var handler = new SearchMessagesQueryHandler(helper.CreateUserContext(1), helper.MessagesStorage, users.Object);

        var result = await handler.Handle(new SearchMessagesQuery
        {
            Filter = new MessageSearchFilter { AuthorUserId = 2 },
        }, CancellationToken.None);

        result.Hits.Should().ContainSingle();
        result.Hits[0].MessageId.Should().Be(expected.Id);
        result.Hits[0].ChatTitle.Should().Be("Команда");
        result.Hits[0].Author.DisplayName.Should().Be("Анна Иванова");
    }
}
