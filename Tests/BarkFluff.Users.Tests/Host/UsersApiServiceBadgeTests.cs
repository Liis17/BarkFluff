using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Users;
using BarkFluff.Users.Features.Badges.GetUserBadges;
using BarkFluff.Users.Host;

using MediatR;

namespace BarkFluff.Users.Tests.Host;

public class UsersApiServiceBadgeTests
{
    [Fact]
    public async Task GetUserBadges_RestrictedAccountReturnsNoBadges()
    {
        var testHelper = new TestHelper();
        var user = await testHelper.SeedUser();
        var badge = await testHelper.SeedBadge();
        await testHelper.UsersStorage.AssignBadgeToUserAsync(user.Id, badge.Id, 0);
        user.AccountStatus = BarkFluff.Users.Domain.AccountStatus.Blocked;
        await testHelper.DbContext.SaveChangesAsync();

        var handler = new GetUserBadgesQueryHandler(testHelper.UsersStorage, TestHelper.CreateLogger<GetUserBadgesQueryHandler>());
        var response = await handler.Handle(new GetUserBadgesQuery { UserId = user.Id }, CancellationToken.None);

        response.Badges.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserBadges_WithoutLimit_ReturnsAllActiveBadges()
    {
        var testHelper = new TestHelper();
        var user = await testHelper.SeedUser();

        for (var index = 0; index < 3; index++)
        {
            var badge = await testHelper.SeedBadge(name: $"Badge {index}");
            await testHelper.UsersStorage.AssignBadgeToUserAsync(user.Id, badge.Id, index);
        }
        var inactiveBadge = await testHelper.SeedBadge(name: "Inactive badge", isActive: false);
        await testHelper.UsersStorage.AssignBadgeToUserAsync(user.Id, inactiveBadge.Id, 3);

        var handler = new GetUserBadgesQueryHandler(testHelper.UsersStorage, TestHelper.CreateLogger<GetUserBadgesQueryHandler>());
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetUserBadgesQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetUserBadgesQuery query, CancellationToken cancellationToken) => handler.Handle(query, cancellationToken));

        var service = new UsersApiService(mediator.Object, new MetricsCollector());

        var response = await service.GetUserBadges(new GetUserBadgesRequest { UserId = user.Id }, null!);

        response.Badges.Should().HaveCount(3);
        response.Badges.Select(badge => badge.Badge.Name).Should().Contain(["Badge 0", "Badge 1", "Badge 2"]);
        response.Badges.Select(badge => badge.Badge.Name).Should().NotContain("Inactive badge");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task GetUserBadges_WithLimit_ReturnsRequestedNumberOfBadges(int limit)
    {
        var testHelper = new TestHelper();
        var user = await testHelper.SeedUser();

        for (var index = 0; index < 3; index++)
        {
            var badge = await testHelper.SeedBadge(name: $"Badge {index}");
            await testHelper.UsersStorage.AssignBadgeToUserAsync(user.Id, badge.Id, index);
        }

        var handler = new GetUserBadgesQueryHandler(testHelper.UsersStorage, TestHelper.CreateLogger<GetUserBadgesQueryHandler>());
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<GetUserBadgesQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetUserBadgesQuery query, CancellationToken cancellationToken) => handler.Handle(query, cancellationToken));

        var service = new UsersApiService(mediator.Object, new MetricsCollector());

        var response = await service.GetUserBadges(new GetUserBadgesRequest { UserId = user.Id, Limit = limit }, null!);

        response.Badges.Should().HaveCount(limit);
    }
}
