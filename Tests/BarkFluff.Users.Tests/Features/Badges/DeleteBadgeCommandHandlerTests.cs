using BarkFluff.Users.Features.Badges.AssignUserBadge;
using BarkFluff.Users.Features.Badges.Commands;
using BarkFluff.Users.Features.Badges.DeleteBadge;
using BarkFluff.Users.Features.Badges.GetUserBadges;
using BarkFluff.Users.Features.Badges.Queries;
using BarkFluff.Users.Features.Badges.RemoveUserBadge;
using BarkFluff.Users.Features.Badges.UpdateBadge;
using BarkFluff.Users.Features.Badges.UpdateUserBadgesPriority;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Badges;

public class DeleteBadgeCommandHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_ExistingBadge_ReturnsSuccess()
    {
        var badge = await _h.SeedBadge();
        var handler = new DeleteBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<DeleteBadgeCommandHandler>());

        var result = await handler.Handle(new DeleteBadgeCommand { Id = badge.Id }, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonExistingBadge_ReturnsFalse()
    {
        var handler = new DeleteBadgeCommandHandler(_h.UsersStorage, TestHelper.CreateLogger<DeleteBadgeCommandHandler>());

        var result = await handler.Handle(new DeleteBadgeCommand { Id = 999 }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
