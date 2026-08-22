using BarkFluff.Bots.Domain;
using BarkFluff.Bots.Features.UpdateBotProfile;
using BarkFluff.Bots.Persistence;
using BarkFluff.Bots.Persistence.Services;
using BarkFluff.Bots.Services;
using BarkFluff.Proto.Users;

using Grpc.Core;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace BarkFluff.Bots.Tests.Features;

public class UpdateBotProfileCommandHandlerTests
{
    private const long BotId = 42;

    [Fact]
    public async Task Handle_ValidProfile_UpdatesUsersBotsAndCache()
    {
        await using var context = CreateContext(new Bot
        {
            Id = BotId,
            Username = "oldbot",
            Name = "Старое имя",
            TokenId = "token-id"
        });
        var usersClient = CreateUsersClient("oldbot", isBot: true, usernameExists: false);
        var cache = CreateCache();
        var handler = CreateHandler(context, usersClient, cache);

        await handler.Handle(new UpdateBotProfileCommand
        {
            BotId = BotId,
            Name = "Новое имя",
            Username = "newbot"
        }, CancellationToken.None);

        var stored = await context.Bots.SingleAsync(b => b.Id == BotId);
        Assert.Equal("Новое имя", stored.Name);
        Assert.Equal("newbot", stored.Username);
        Assert.Equal("Новое имя", cache.Get(BotId)?.Name);
        Assert.Equal("newbot", cache.Get(BotId)?.Username);
        usersClient.Verify(c => c.UpdateProfileServerAsync(
            It.Is<UpdateProfileServerRequest>(r =>
                r.UserId == BotId && r.FirstName == "Новое имя" && r.Username == "newbot"),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsInvalidArgument()
    {
        await using var context = CreateContext(new Bot { Id = BotId, Username = "oldbot", Name = "Имя" });
        var usersClient = CreateUsersClient("oldbot", isBot: true, usernameExists: false);
        var handler = CreateHandler(context, usersClient, CreateCache());

        var exception = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(
            new UpdateBotProfileCommand { BotId = BotId, Name = "   ", Username = "newbot" },
            CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Theory]
    [InlineData("bad-name")]
    [InlineData("ab")]
    [InlineData("имяbot")]
    public async Task Handle_InvalidUsername_ThrowsInvalidArgument(string username)
    {
        await using var context = CreateContext(new Bot { Id = BotId, Username = "oldbot", Name = "Имя" });
        var usersClient = CreateUsersClient("oldbot", isBot: true, usernameExists: false);
        var handler = CreateHandler(context, usersClient, CreateCache());

        var exception = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(
            new UpdateBotProfileCommand { BotId = BotId, Name = "Имя", Username = username },
            CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task Handle_UsernameWithoutBotSuffix_ThrowsInvalidArgument()
    {
        await using var context = CreateContext(new Bot { Id = BotId, Username = "oldbot", Name = "Имя" });
        var usersClient = CreateUsersClient("oldbot", isBot: true, usernameExists: false);
        var handler = CreateHandler(context, usersClient, CreateCache());

        var exception = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(
            new UpdateBotProfileCommand { BotId = BotId, Name = "Имя", Username = "new_user" },
            CancellationToken.None));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    [Fact]
    public async Task Handle_UsernameConflict_ThrowsAlreadyExistsAndDoesNotSync()
    {
        await using var context = CreateContext(new Bot { Id = BotId, Username = "oldbot", Name = "Имя" });
        var usersClient = CreateUsersClient("oldbot", isBot: true, usernameExists: true);
        var cache = CreateCache();
        cache.ApplySet(new Bot { Id = BotId, Username = "oldbot", Name = "Имя" });
        var handler = CreateHandler(context, usersClient, cache);

        var exception = await Assert.ThrowsAsync<RpcException>(() => handler.Handle(
            new UpdateBotProfileCommand { BotId = BotId, Name = "Новое имя", Username = "takenbot" },
            CancellationToken.None));

        Assert.Equal(StatusCode.AlreadyExists, exception.StatusCode);
        Assert.Equal("oldbot", (await context.Bots.SingleAsync(b => b.Id == BotId)).Username);
        Assert.Equal("oldbot", cache.Get(BotId)?.Username);
        usersClient.Verify(c => c.UpdateProfileServerAsync(
            It.IsAny<UpdateProfileServerRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingBotRecord_ThrowsBotNotFound()
    {
        await using var context = CreateContext();
        var usersClient = CreateUsersClient("oldbot", isBot: true, usernameExists: false);
        var handler = CreateHandler(context, usersClient, CreateCache());

        await Assert.ThrowsAsync<BarkFluff.Shared.Exceptions.Bots.BotNotFoundException>(() => handler.Handle(
            new UpdateBotProfileCommand { BotId = BotId, Name = "Имя", Username = "newbot" },
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UserIsNotBot_ThrowsBotNotFound()
    {
        await using var context = CreateContext(new Bot { Id = BotId, Username = "oldbot", Name = "Имя" });
        var usersClient = CreateUsersClient("oldbot", isBot: false, usernameExists: false);
        var handler = CreateHandler(context, usersClient, CreateCache());

        await Assert.ThrowsAsync<BarkFluff.Shared.Exceptions.Bots.BotNotFoundException>(() => handler.Handle(
            new UpdateBotProfileCommand { BotId = BotId, Name = "Имя", Username = "newbot" },
            CancellationToken.None));
    }

    private static BotsContext CreateContext(params Bot[] bots)
    {
        var options = new DbContextOptionsBuilder<BotsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new BotsContext(options);
        context.Bots.AddRange(bots);
        context.SaveChanges();
        return context;
    }

    private static Mock<UsersServerApi.UsersServerApiClient> CreateUsersClient(
        string currentUsername,
        bool isBot,
        bool usernameExists)
    {
        var client = new Mock<UsersServerApi.UsersServerApiClient>();
        client.Setup(c => c.GetByIdAsync(
                It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(new GetByIdResponse
            {
                User = new User { Id = BotId, Username = currentUsername, IsBot = isBot }
            }));
        client.Setup(c => c.CheckExistUsernameAsync(
                It.IsAny<CheckExistUsernameRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(new CheckExistResponse { Exist = usernameExists }));
        client.Setup(c => c.UpdateProfileServerAsync(
                It.IsAny<UpdateProfileServerRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(new UpdateProfileServerResponse()));
        return client;
    }

    private static UpdateBotProfileCommandHandler CreateHandler(
        BotsContext context,
        Mock<UsersServerApi.UsersServerApiClient> usersClient,
        BotRegistryCache cache)
        => new(
            new BotsStorage(context),
            cache,
            usersClient.Object,
            Mock.Of<ILogger<UpdateBotProfileCommandHandler>>());

    private static BotRegistryCache CreateCache()
        => new(Mock.Of<IBus>(), Mock.Of<ILogger<BotRegistryCache>>());

    private static AsyncUnaryCall<T> CreateAsyncCall<T>(T response)
        => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
}
