using BarkFluff.Proto.Files;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Features.FindByLogin;
using BarkFluff.Users.Features.ListByIds;
using BarkFluff.Users.Features.GetUserByUsername;
using BarkFluff.Users.Features.SetProfilePictureServer;
using BarkFluff.Users.Features.UpdateProfileServer;
using BarkFluff.Users.Features.UpdateStorageLimit;
using FluentAssertions;
using Grpc.Core;
using Moq;

namespace BarkFluff.Users.Tests.Features.ServerApi;

public class ListByIdsCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ReturnsMatchingUsers()
    {
        var u1 = await _h.SeedUser(username: "user1");
        var u2 = await _h.SeedUser(username: "user2");
        var u3 = await _h.SeedUser(username: "user3");
        var handler = new ListByIdsCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<ListByIdsCommandHandler>());

        var result = await handler.Handle(new ListByIdsCommand { Ids = [u1.Id, u3.Id] }, CancellationToken.None);

        result.Users.Should().HaveCount(2);
        result.Users.Select(u => u.Id).Should().Contain([u1.Id, u3.Id]);
    }

    [Fact]
    public async Task Handle_NoMatchingIds_ReturnsEmpty()
    {
        var handler = new ListByIdsCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<ListByIdsCommandHandler>());

        var result = await handler.Handle(new ListByIdsCommand { Ids = [9999999] }, CancellationToken.None);

        result.Users.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
