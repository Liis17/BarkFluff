using BarkFluff.Users.Features.SearchUsers;
using BarkFluff.Users.Features.SearchUsersServer;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Search;

public class SearchUsersServerQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_SizeZero_ReturnsEmptyResponse()
    {
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery { Query = "", Offset = 0, Size = 0 }, CancellationToken.None);

        result.Users.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SizeOver50_CapsAt50()
    {
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var query = new SearchUsersServerQuery { Query = "", Offset = 0, Size = 100 };
        await handler.Handle(query, CancellationToken.None);

        query.Size.Should().Be(50);
    }

    [Fact]
    public async Task Handle_EmptyQuery_ReturnsAllUsersDescending()
    {
        var user1 = await _h.SeedUser(username: "alpha");
        var user2 = await _h.SeedUser(username: "beta");
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery { Query = "", Offset = 0, Size = 10 }, CancellationToken.None);

        result.TotalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Handle_QueryAsUserId_ReturnsMatchingUser()
    {
        var user = await _h.SeedUser(username: "specific");
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery { Query = user.Id.ToString(), Offset = 0, Size = 10 }, CancellationToken.None);

        result.Users.Should().ContainSingle(u => u.Id == user.Id);
    }

    [Fact]
    public async Task Handle_QueryAsDraftUserId_ReturnsEmpty()
    {
        var user = await _h.SeedUser(username: "draft", isDraft: true);
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery { Query = user.Id.ToString(), Offset = 0, Size = 10 }, CancellationToken.None);

        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyQuery_ExcludesBotsFromUsersAndTotalCount()
    {
        var human = await _h.SeedUser(username: "human");
        await _h.SeedUser(username: "testbot", isBot: true);
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery { Query = "", Offset = 0, Size = 10 }, CancellationToken.None);

        result.Users.Should().ContainSingle(u => u.Id == human.Id);
        result.Users.Should().NotContain(u => u.Username == "testbot");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_QueryAsBotId_ReturnsEmpty()
    {
        var bot = await _h.SeedUser(username: "testbot", isBot: true);
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery { Query = bot.Id.ToString(), Offset = 0, Size = 10 }, CancellationToken.None);

        result.Users.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithBadges_IncludesBadgesInResponse()
    {
        var user = await _h.SeedUser(username: "badged");
        var badge = await _h.SeedBadge();
        await _h.UsersStorage.AssignBadgeToUserAsync(user.Id, badge.Id, 1);

        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery { Query = "", Offset = 0, Size = 50 }, CancellationToken.None);

        var found = result.Users.FirstOrDefault(u => u.Id == user.Id);
        found.Should().NotBeNull();
        found!.Badges.Should().ContainSingle(b => b.Badge.Id == badge.Id);
    }

    [Fact]
    public async Task Handle_StatusFilterMasksProfileAndReturnsRestrictionDetailsForAdmins()
    {
        var blocked = await _h.SeedUser(username: "blockeduser", firstName: "Private");
        var badge = await _h.SeedBadge();
        await _h.UsersStorage.AssignBadgeToUserAsync(blocked.Id, badge.Id, 0);
        blocked.AccountStatus = BarkFluff.Users.Domain.AccountStatus.Blocked;
        blocked.RestrictionReason = "abuse";
        blocked.RestrictedAt = DateTime.UtcNow;
        await _h.DbContext.SaveChangesAsync();
        await _h.SeedUser(username: "activeuser");
        var handler = new SearchUsersServerQueryHandler(_h.UsersStorage, TestHelper.CreateLogger<SearchUsersServerQueryHandler>());

        var result = await handler.Handle(new SearchUsersServerQuery
        {
            Query = string.Empty,
            Offset = 0,
            Size = 10,
            StatusFilter = BarkFluff.Proto.Users.AccountStatus.Blocked,
        }, CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Users.Should().ContainSingle(user => user.Id == blocked.Id);
        var maskedUser = result.Users.Single();
        maskedUser.FirstName.Should().Be("Заблокированный пользователь");
        maskedUser.LastName.Should().BeEmpty();
        maskedUser.Username.Should().BeEmpty();
        maskedUser.Bio.Should().BeEmpty();
        maskedUser.ProfilePicture.Should().BeEmpty();
        result.Results.Should().ContainSingle().Which.Restriction.Reason.Should().Be("abuse");
        result.Results.Single().User.AccountStatus.Should().Be(BarkFluff.Proto.Users.AccountStatus.Blocked);
        result.Results.Single().User.Badges.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
