using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Messages.Features.ListChatMembers;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Messages;

using Grpc.Core;

namespace BarkFluff.Messages.Tests.Features.ListChatMembers;

public class ListChatMembersCommandHandlerTests
{
    private readonly TestHelper _h = new();
    private readonly Mock<UsersServerApi.UsersServerApiClient> _usersClient;

    public ListChatMembersCommandHandlerTests()
    {
        _usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        SetupUsersClient();
    }

    private ListChatMembersCommandHandler CreateHandler(long userId)
    {
        return new ListChatMembersCommandHandler(
            _h.ChatsStorage,
            _h.CreateUserContext(userId),
            _usersClient.Object,
            TestHelper.CreateLogger<ListChatMembersCommandHandler>());
    }

    private void SetupUsersClient()
    {
        var response = new ListByIdsResponse();
        _usersClient.Setup(c => c.ListByIdsAsync(It.IsAny<ListByIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<ListByIdsResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsMembers()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2, 3]);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new ListChatMembersCommand { ChatId = chat.Id, Skip = 0, Count = 10 }, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NoAccess_ThrowsNoAccessToChatException()
    {
        var chat = await _h.SeedChat(memberUserIds: [99, 100]);
        var handler = CreateHandler(1);

        var act = async () => await handler.Handle(new ListChatMembersCommand { ChatId = chat.Id, Skip = 0, Count = 10 }, CancellationToken.None);

        await act.Should().ThrowAsync<NoAccessToChatException>();
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectTotalCount()
    {
        var chat = await _h.SeedChat(memberUserIds: [1, 2, 3, 4, 5]);
        var handler = CreateHandler(1);

        var result = await handler.Handle(new ListChatMembersCommand { ChatId = chat.Id, Skip = 0, Count = 2 }, CancellationToken.None);

        result.TotalCount.Should().Be(5);
    }
}
