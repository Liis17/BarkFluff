using BarkFluff.Users.Features.Badges;
using BarkFluff.Users.Features.Badges.SetPrimaryBadge;
using BarkFluff.Shared.Exceptions.Identity;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Tests.Features.Badges;

public class SetPrimaryBadgeCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _helper = new();

    [Fact]
    public async Task Handle_NegativeBadgeId_ReturnsInvalidArgument()
    {
        var handler = new SetPrimaryBadgeCommandHandler(
            _helper.UsersStorage,
            _helper.CreateUserContext(1234),
            TestHelper.CreateLogger<SetPrimaryBadgeCommandHandler>());

        var exception = await FluentActions.Awaiting(() => handler.Handle(
                new SetPrimaryBadgeCommand { BadgeId = -1 }, CancellationToken.None))
            .Should().ThrowAsync<BadgeException>();

        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Theory]
    [InlineData(BarkFluff.Users.Domain.AccountStatus.Blocked)]
    [InlineData(BarkFluff.Users.Domain.AccountStatus.PendingDeletion)]
    [InlineData(BarkFluff.Users.Domain.AccountStatus.Deleted)]
    public async Task Handle_RestrictedAccountCannotClearPrimaryBadge(BarkFluff.Users.Domain.AccountStatus status)
    {
        var user = await _helper.SeedUser();
        user.AccountStatus = status;
        await _helper.DbContext.SaveChangesAsync();
        var handler = new SetPrimaryBadgeCommandHandler(
            _helper.UsersStorage,
            _helper.CreateUserContext(user.Id),
            TestHelper.CreateLogger<SetPrimaryBadgeCommandHandler>());

        await FluentActions.Awaiting(() => handler.Handle(
                new SetPrimaryBadgeCommand { BadgeId = 0 }, CancellationToken.None))
            .Should().ThrowAsync<InvalidAccountRestrictionTransitionException>();
    }

    [Fact]
    public void UserBadgesModel_HasFilteredUniquePrimaryIndex()
    {
        var index = _helper.DbContext.Model.FindEntityType(typeof(BarkFluff.Users.Domain.UserBadge))!
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "IX_UserBadges_UserId_Primary");

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be("\"IsPrimary\" = TRUE");
    }

    public ValueTask DisposeAsync() => _helper.DbContext.DisposeAsync();
}
