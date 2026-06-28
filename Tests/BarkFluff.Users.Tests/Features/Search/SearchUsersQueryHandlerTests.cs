using BarkFluff.Users.Features.SearchUsers;
using BarkFluff.Users.Features.SearchUsersServer;
using FluentAssertions;

namespace BarkFluff.Users.Tests.Features.Search;

public class SearchUsersQueryHandlerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task Handle_SizeZero_ReturnsEmptyResult()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new SearchUsersQueryHandler(_h.UsersStorage, ctx, TestHelper.CreateLogger<SearchUsersQueryHandler>());

        var result = await handler.Handle(new SearchUsersQuery { Query = "test", Skip = 0, Size = 0 }, CancellationToken.None);

        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SizeOver50_CapsAt50()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new SearchUsersQueryHandler(_h.UsersStorage, ctx, TestHelper.CreateLogger<SearchUsersQueryHandler>());

        var query = new SearchUsersQuery { Query = "test", Skip = 0, Size = 100 };
        try { await handler.Handle(query, CancellationToken.None); } catch (InvalidOperationException) { }

        query.Size.Should().Be(50);
    }

    [Fact]
    public async Task Handle_EmptyQuery_ReturnsEmptyResult()
    {
        var user = await _h.SeedUser();
        var ctx = _h.CreateUserContext(user.Id);
        var handler = new SearchUsersQueryHandler(_h.UsersStorage, ctx, TestHelper.CreateLogger<SearchUsersQueryHandler>());

        var result = await handler.Handle(new SearchUsersQuery { Query = "", Skip = 0, Size = 10 }, CancellationToken.None);

        result.Users.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
