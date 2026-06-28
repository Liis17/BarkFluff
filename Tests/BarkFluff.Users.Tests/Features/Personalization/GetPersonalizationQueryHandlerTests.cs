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

public class GetPersonalizationQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingPersonalization_ReturnsData()
    {
        var user = await _h.SeedUser();
        await _h.PersonalizationStorage.Update(user.Id, "poster-123", ["bg1", "bg2"]);
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetPersonalizationQueryHandler(ctx, _h.PersonalizationStorage, TestHelper.CreateLogger<GetPersonalizationQueryHandler>());

        var result = await handler.Handle(new GetPersonalizationQuery(), CancellationToken.None);

        result.Personalization.ProfilePosterFileId.Should().Be("poster-123");
        result.Personalization.ChatBackgroundFileIds.Should().Equal("bg1", "bg2");
    }

    [Fact]
    public async Task Handle_NoPersonalization_CreatesDefault()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new GetPersonalizationQueryHandler(ctx, _h.PersonalizationStorage, TestHelper.CreateLogger<GetPersonalizationQueryHandler>());

        var result = await handler.Handle(new GetPersonalizationQuery(), CancellationToken.None);

        result.Personalization.Should().NotBeNull();
        result.Personalization.ProfilePosterFileId.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
