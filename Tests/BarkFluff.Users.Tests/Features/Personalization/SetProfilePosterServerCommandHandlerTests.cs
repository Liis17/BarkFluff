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

public class SetProfilePosterServerCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_SetsPoster()
    {
        var user = await _h.SeedUser();
        var handler = new SetProfilePosterServerCommandHandler(_h.PersonalizationStorage, TestHelper.CreateLogger<SetProfilePosterServerCommandHandler>());

        await handler.Handle(new SetProfilePosterServerCommand { UserId = user.Id, PosterFileId = "server-poster-123" }, CancellationToken.None);

        var p = await _h.PersonalizationStorage.Get(user.Id);
        p!.ProfilePosterFileId.Should().Be("server-poster-123");
    }

    [Fact]
    public async Task Handle_NullPosterFileId_RemovesPoster()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "old-poster", []);
        var handler = new SetProfilePosterServerCommandHandler(_h.PersonalizationStorage, TestHelper.CreateLogger<SetProfilePosterServerCommandHandler>());

        await handler.Handle(new SetProfilePosterServerCommand { UserId = user.Id, PosterFileId = null }, CancellationToken.None);

        var p = await _h.PersonalizationStorage.Get(user.Id);
        p!.ProfilePosterFileId.Should().BeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
