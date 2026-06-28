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

public class GetProfilePosterQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_WithPoster_ReturnsFileId()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "poster-abc", []);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetProfilePosterQueryHandler(ctx, _h.PersonalizationStorage);

        var result = await handler.Handle(new GetProfilePosterQuery(), CancellationToken.None);

        result.ProfilePosterFileId.Should().Be("poster-abc");
    }

    [Fact]
    public async Task Handle_WithoutPoster_ReturnsEmpty()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetProfilePosterQueryHandler(ctx, _h.PersonalizationStorage);

        var result = await handler.Handle(new GetProfilePosterQuery(), CancellationToken.None);

        result.ProfilePosterFileId.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
