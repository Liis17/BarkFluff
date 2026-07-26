using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Features.GetMyCommands;
using BarkFluff.Bots.Features.SetMyCommands;
using BarkFluff.Bots.Persistence;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Shared.Exceptions.Bots;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Bots.Tests.Features;

public class BotCommandsHandlersTests
{
    private const long BotId = 1;

    private readonly BotsContext _context;
    private readonly BotRegistryCache _registry = new(Mock.Of<IBus>(), Mock.Of<ILogger<BotRegistryCache>>());
    private readonly SetMyCommandsCommandHandler _setHandler;
    private readonly GetMyCommandsQueryHandler _getHandler;

    public BotCommandsHandlersTests()
    {
        var options = new DbContextOptionsBuilder<BotsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new BotsContext(options);
        _context.Bots.Add(new Bot { Id = BotId, Username = "testbot" });
        _context.SaveChanges();

        _setHandler = new SetMyCommandsCommandHandler(
            new BotsStorage(_context), _registry, new MetricsCollector());
        _getHandler = new GetMyCommandsQueryHandler(_registry);
    }

    [Fact]
    public async Task Set_ThenGet_ReturnsStoredCommands()
    {
        await _setHandler.Handle(new SetMyCommandsCommand
        {
            BotId = BotId,
            Commands =
            [
                new BotCommand { Command = "start", Description = "Начать" },
                new BotCommand { Command = "help_me", Description = "Помощь" },
            ],
        }, CancellationToken.None);

        var result = await _getHandler.Handle(new GetMyCommandsQuery { BotId = BotId }, CancellationToken.None);

        Assert.Equal(2, result.Commands.Count);
        Assert.Equal("start", result.Commands[0].Command);
        Assert.Equal("Начать", result.Commands[0].Description);
        Assert.Equal("help_me", result.Commands[1].Command);
    }

    [Fact]
    public async Task Set_EmptyList_ClearsCommands()
    {
        await _setHandler.Handle(new SetMyCommandsCommand
        {
            BotId = BotId,
            Commands = [new BotCommand { Command = "start", Description = "Начать" }],
        }, CancellationToken.None);

        await _setHandler.Handle(new SetMyCommandsCommand { BotId = BotId, Commands = [] }, CancellationToken.None);

        var result = await _getHandler.Handle(new GetMyCommandsQuery { BotId = BotId }, CancellationToken.None);

        Assert.Empty(result.Commands);
        Assert.Null(_context.Bots.Single(b => b.Id == BotId).Commands);
    }

    [Fact]
    public async Task Get_BotWithoutCommands_ReturnsEmpty()
    {
        _registry.ApplySet(new Bot { Id = BotId, Username = "testbot" });

        var result = await _getHandler.Handle(new GetMyCommandsQuery { BotId = BotId }, CancellationToken.None);

        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task Get_UnknownBot_ThrowsBotNotFound()
    {
        await Assert.ThrowsAsync<BotNotFoundException>(
            () => _getHandler.Handle(new GetMyCommandsQuery { BotId = 999 }, CancellationToken.None));
    }

    [Theory]
    [InlineData("Start", "заглавные буквы запрещены")]
    [InlineData("with-dash", "дефис запрещён")]
    [InlineData("", "пустое имя")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "33 символа — сверх лимита")]
    public async Task Set_InvalidCommandName_ThrowsInvalidArgument(string name, string reason)
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _setHandler.Handle(new SetMyCommandsCommand
            {
                BotId = BotId,
                Commands = [new BotCommand { Command = name, Description = "Описание" }],
            }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.NotEmpty(reason);
    }

    [Fact]
    public async Task Set_DuplicateCommand_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _setHandler.Handle(new SetMyCommandsCommand
            {
                BotId = BotId,
                Commands =
                [
                    new BotCommand { Command = "start", Description = "Первая" },
                    new BotCommand { Command = "start", Description = "Вторая" },
                ],
            }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Set_EmptyDescription_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _setHandler.Handle(new SetMyCommandsCommand
            {
                BotId = BotId,
                Commands = [new BotCommand { Command = "start", Description = "   " }],
            }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Set_TooManyCommands_ThrowsInvalidArgument()
    {
        var commands = Enumerable.Range(0, 101)
            .Select(i => new BotCommand { Command = $"cmd_{i}", Description = "Описание" })
            .ToList();

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => _setHandler.Handle(
                new SetMyCommandsCommand { BotId = BotId, Commands = commands }, CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }
}
