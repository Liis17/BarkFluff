using BarkFluff.Proto.Files;
using BarkFluff.Users.Features.Personalization.GetPersonalization;
using BarkFluff.Users.Features.Personalization.GetProfilePoster;
using BarkFluff.Users.Features.Personalization.GetProfilePosterServer;
using BarkFluff.Users.Features.Personalization.SetProfilePoster;
using BarkFluff.Users.Features.Personalization.SetProfilePosterServer;
using BarkFluff.Users.Features.Personalization.UpdatePersonalization;
using FluentAssertions;
using Grpc.Core;
using Moq;

namespace BarkFluff.Users.Tests.Features.Personalization;

public class SetProfilePosterCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_SetsPoster()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new SetProfilePosterCommandHandler(ctx, _h.PersonalizationStorage, TestHelper.CreateLogger<SetProfilePosterCommandHandler>());

        await handler.Handle(new SetProfilePosterCommand { ProfilePosterFileId = "new-poster" }, CancellationToken.None);

        var p = await _h.PersonalizationStorage.Get(user.Id);
        p!.ProfilePosterFileId.Should().Be("new-poster");
    }

    [Fact]
    public async Task Handle_NullPoster_DeletesPoster()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "old-poster", []);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new SetProfilePosterCommandHandler(ctx, _h.PersonalizationStorage, TestHelper.CreateLogger<SetProfilePosterCommandHandler>());

        await handler.Handle(new SetProfilePosterCommand { ProfilePosterFileId = null }, CancellationToken.None);

        var p = await _h.PersonalizationStorage.Get(user.Id);
        p!.ProfilePosterFileId.Should().BeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
