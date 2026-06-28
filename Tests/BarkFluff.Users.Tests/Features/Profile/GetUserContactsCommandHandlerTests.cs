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

public class GetUserContactsCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingUser_ReturnsUserAndContact()
    {
        var user = await _h.SeedUser(email: "john@test.com");
        var handler = new GetUserContactsCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<GetUserContactsCommandHandler>());

        var result = await handler.Handle(new GetUserContactsCommand { UserId = user.Id }, CancellationToken.None);

        result.User.Should().NotBeNull();
        result.Contact.Email.Should().Be("john@test.com");
    }

    [Fact]
    public async Task Handle_UserNotFound_ThrowsUserNotFoundException()
    {
        var handler = new GetUserContactsCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<GetUserContactsCommandHandler>());

        var act = () => handler.Handle(new GetUserContactsCommand { UserId = 9999999 }, CancellationToken.None);

        await act.Should().ThrowAsync<UserNotFoundException>();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
