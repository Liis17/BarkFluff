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

public class UpdateStorageLimitCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_UpdatesStorageLimit()
    {
        var user = await _h.SeedUser();
        var handler = new UpdateStorageLimitCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<UpdateStorageLimitCommandHandler>());

        var result = await handler.Handle(new UpdateStorageLimitCommand
        {
            UserId = user.Id,
            StorageLimitGb = 50
        }, CancellationToken.None);

        result.User.StorageLimitGb.Should().Be(50);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
