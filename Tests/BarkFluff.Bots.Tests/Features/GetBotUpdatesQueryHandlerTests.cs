using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Features.GetBotUpdates;
using BarkFluff.Bots.Persistence;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Shared.Exceptions.Bots;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Bots.Tests.Features;

public class GetBotUpdatesQueryHandlerTests
{
    private readonly BotsContext _context;
    private readonly BotPollingGuard _pollingGuard = new();
    private readonly GetBotUpdatesQueryHandler _handler;

    public GetBotUpdatesQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<BotsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new BotsContext(options);

        _handler = new GetBotUpdatesQueryHandler(
            new BotUpdatesStorage(_context), new BotUpdateNotifier(), _pollingGuard);
    }

    [Fact]
    public async Task Handle_BacklogAvailable_ReturnsOnlyOwnUpdates()
    {
        _context.Bots.AddRange(new Bot { Id = 1 }, new Bot { Id = 2 });
        _context.BotUpdates.AddRange(
            new BotUpdate { BotId = 1, Payload = "{}", CreatedAt = DateTime.UtcNow },
            new BotUpdate { BotId = 2, Payload = "{}", CreatedAt = DateTime.UtcNow });
        _context.SaveChanges();

        var result = await _handler.Handle(
            new GetBotUpdatesQuery { BotId = 1, Offset = 0, Limit = 10 }, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1, result[0].BotId);
    }

    [Fact]
    public async Task Handle_ActivePollingStream_ThrowsConflict()
    {
        _pollingGuard.TryEnter(1);

        await Assert.ThrowsAsync<BotPollingConflictException>(
            () => _handler.Handle(new GetBotUpdatesQuery { BotId = 1, Limit = 10 }, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReleasesGuardAfterCompletion()
    {
        var query = new GetBotUpdatesQuery { BotId = 1, Limit = 10 };

        await _handler.Handle(query, CancellationToken.None);

        // Слот освобождён — повторный вызов не бросает конфликт
        await _handler.Handle(query, CancellationToken.None);
    }
}
