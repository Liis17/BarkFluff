using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Messages.Consumers;
using BarkFluff.Messages.Persistence.Services;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Users;

using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BarkFluff.Messages.Tests.Consumers;

public sealed class UserChangedProfileConsumerTests : IAsyncDisposable
{
    private readonly TestHelper _h = new();

    [Fact]
    public async Task UserChangedNameConsumer_UsesCurrentMaskedProfileInsteadOfEventPayload()
    {
        const long restrictedUserId = 101;
        const long peerUserId = 202;
        var chat = await _h.SeedChat(memberUserIds: [restrictedUserId, peerUserId]);
        using var services = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var cache = new ChatCache(services.GetRequiredService<IDistributedCache>(), TestHelper.CreateLogger<ChatCache>());
        var usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new GetByIdResponse
            {
                User = new User { FirstName = "Заблокированный пользователь", AccountStatus = AccountStatus.Blocked }
            }));
        var consumer = new UserChangedNameConsumer(_h.ChatsStorage, cache, usersClient.Object,
            TestHelper.CreateLogger<UserChangedNameConsumer>(), new MetricsCollector());

        await consumer.Consume(CreateContext(new UserChangedName
        {
            UserId = restrictedUserId,
            NewFirstName = "Private",
            NewLastName = "Person",
        }));

        (await cache.GetChatName(chat.Id, peerUserId)).Should().Be("Заблокированный пользователь");
    }

    [Fact]
    public async Task UserChangedAvatarConsumer_UsesCurrentMaskedProfileInsteadOfEventPayload()
    {
        const long restrictedUserId = 303;
        const long peerUserId = 404;
        var chat = await _h.SeedChat(memberUserIds: [restrictedUserId, peerUserId]);
        using var services = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var cache = new ChatCache(services.GetRequiredService<IDistributedCache>(), TestHelper.CreateLogger<ChatCache>());
        var usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(TestHelper.CreateAsyncCall(new GetByIdResponse
            {
                User = new User { FirstName = "Удалённый пользователь", ProfilePicture = string.Empty, AccountStatus = AccountStatus.PendingDeletion }
            }));
        var consumer = new UserChangedAvatarConsumer(_h.ChatsStorage, cache, usersClient.Object,
            TestHelper.CreateLogger<UserChangedAvatarConsumer>(), new MetricsCollector());

        await consumer.Consume(CreateContext(new UserChangedAvatar
        {
            UserId = restrictedUserId,
            ProfilePictureUrl = "old-private-avatar",
            ProfilePictureUrlPreview = "old-private-preview",
        }));

        (await cache.GetChatImage(chat.Id, peerUserId)).Should().BeEmpty();
    }

    private static ConsumeContext<T> CreateContext<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    public ValueTask DisposeAsync() => _h.DbContext.DisposeAsync();
}
