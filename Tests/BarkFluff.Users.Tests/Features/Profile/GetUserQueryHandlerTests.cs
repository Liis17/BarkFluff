using BarkFluff.Proto.Files;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Shared.Exceptions.Users;
using BarkFluff.Users.Features.ChangeBio;
using BarkFluff.Users.Features.ChangeName;
using BarkFluff.Users.Features.ChangeUsername;
using BarkFluff.Users.Features.GetUser;
using BarkFluff.Users.Features.GetUserContacts;
using BarkFluff.Users.Features.SetProfilePicture;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BarkFluff.Users.Tests.Features.Profile;

public class GetUserQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingUser_ReturnsUser()
    {
        var user = await _h.SeedUser(username: "john", firstName: "John", lastName: "Doe");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetUserQueryHandler(_h.UsersStorage, _h.PersonalizationStorage, ctx, TestHelper.CreateLogger<GetUserQueryHandler>());

        var result = await handler.Handle(new GetUserQuery { UserId = user.Id }, CancellationToken.None);

        result.User.Should().NotBeNull();
        result.User.Id.Should().Be(user.Id);
        result.User.Username.Should().Be("john");
        result.User.FirstName.Should().Be("John");
        result.User.LastName.Should().Be("Doe");
    }

    [Fact]
    public async Task Handle_UserIdNull_UsesCurrentUserContext()
    {
        var user = await _h.SeedUser(username: "me");
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetUserQueryHandler(_h.UsersStorage, _h.PersonalizationStorage, ctx, TestHelper.CreateLogger<GetUserQueryHandler>());

        var result = await handler.Handle(new GetUserQuery { UserId = null }, CancellationToken.None);

        result.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var ctx = _h.CreateUserContext(9999999);
        var handler = new GetUserQueryHandler(_h.UsersStorage, _h.PersonalizationStorage, ctx, TestHelper.CreateLogger<GetUserQueryHandler>());

        var act = () => handler.Handle(new GetUserQuery { UserId = 9999999 }, CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    [Fact]
    public async Task Handle_ReturnsProfilePosterFromPersonalization()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "poster-file-123", []);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetUserQueryHandler(_h.UsersStorage, _h.PersonalizationStorage, ctx, TestHelper.CreateLogger<GetUserQueryHandler>());

        var result = await handler.Handle(new GetUserQuery { UserId = user.Id }, CancellationToken.None);

        result.User.ProfilePosterFileId.Should().Be("poster-file-123");
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
