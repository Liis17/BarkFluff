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

public class UpdatePersonalizationCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_UpdatesAllFields()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.GetOrCreate(user.Id);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdatePersonalizationCommandHandler(ctx, _h.PersonalizationStorage, _h.UserSettingsStorage, TestHelper.CreateLogger<UpdatePersonalizationCommandHandler>());

        await handler.Handle(new UpdatePersonalizationCommand
        {
            Personalization = new Proto.Users.UserPersonalizationData
            {
                ProfilePosterFileId = "new-poster",
                ChatBackgroundFileIds = { "bg3", "bg4" }
            }
        }, CancellationToken.None);

        var p = await _h.PersonalizationStorage.Get(user.Id);
        p!.ProfilePosterFileId.Should().Be("new-poster");
        p.ChatBackgroundFileIds.Should().Equal("bg3", "bg4");
    }

    [Fact]
    public async Task Handle_EmptyPoster_SetsToNull()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "old-poster", []);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdatePersonalizationCommandHandler(ctx, _h.PersonalizationStorage, _h.UserSettingsStorage, TestHelper.CreateLogger<UpdatePersonalizationCommandHandler>());

        await handler.Handle(new UpdatePersonalizationCommand
        {
            Personalization = new Proto.Users.UserPersonalizationData
            {
                ProfilePosterFileId = "",
            }
        }, CancellationToken.None);

        var p = await _h.PersonalizationStorage.Get(user.Id);
        p!.ProfilePosterFileId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NullPersonalization_UsesDefaults()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new UpdatePersonalizationCommandHandler(ctx, _h.PersonalizationStorage, _h.UserSettingsStorage, TestHelper.CreateLogger<UpdatePersonalizationCommandHandler>());

        await handler.Handle(new UpdatePersonalizationCommand { Personalization = null }, CancellationToken.None);

        var p = await _h.PersonalizationStorage.Get(user.Id);
        p.Should().NotBeNull();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
