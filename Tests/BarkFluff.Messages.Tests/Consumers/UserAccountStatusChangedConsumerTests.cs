using BarkFluff.Messages.Consumers;
using BarkFluff.Messages.Features.Shared;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Queue.Users;

using Grpc.Core;

using MassTransit;

namespace BarkFluff.Messages.Tests.Consumers;

public class UserAccountStatusChangedConsumerTests
{
    [Fact]
    public async Task Consume_InvalidatesLocalStatusCacheAndRedeliveryIsSafe()
    {
        var status = AccountStatus.Active;
        var usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        usersClient.Setup(client => client.GetAccountRestrictionAsync(
                It.IsAny<GetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() => TestHelper.CreateAsyncCall(new GetAccountRestrictionResponse
            {
                Restriction = new AccountRestrictionInfo { Status = status }
            }));
        var cache = new UserAccountStatusCache();

        (await cache.GetStatusAsync(42, usersClient.Object, CancellationToken.None)).Should().Be(AccountStatus.Active);
        (await cache.GetStatusAsync(42, usersClient.Object, CancellationToken.None)).Should().Be(AccountStatus.Active);
        usersClient.Verify(client => client.GetAccountRestrictionAsync(
            It.IsAny<GetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Once);

        status = AccountStatus.Blocked;
        var context = new Mock<ConsumeContext<UserAccountStatusChanged>>();
        context.SetupGet(consumeContext => consumeContext.Message).Returns(new UserAccountStatusChanged { UserId = 42, Status = 1 });
        var consumer = new UserAccountStatusChangedConsumer(cache);

        await consumer.Consume(context.Object);
        await consumer.Consume(context.Object);

        (await cache.GetStatusAsync(42, usersClient.Object, CancellationToken.None)).Should().Be(AccountStatus.Blocked);
        usersClient.Verify(client => client.GetAccountRestrictionAsync(
            It.IsAny<GetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Consume_DoesNotAllowAnInFlightOldStatusToRefillTheCache()
    {
        var oldResponse = new TaskCompletionSource<GetAccountRestrictionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        usersClient.Setup(client => client.GetAccountRestrictionAsync(
                It.IsAny<GetAccountRestrictionRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstCallStarted.SetResult();
                    return CreateAsyncCall(oldResponse.Task);
                }

                return TestHelper.CreateAsyncCall(new GetAccountRestrictionResponse
                {
                    Restriction = new AccountRestrictionInfo { Status = AccountStatus.Blocked },
                });
            });

        var cache = new UserAccountStatusCache();
        var lookup = cache.GetStatusAsync(42, usersClient.Object, CancellationToken.None);
        await firstCallStarted.Task;

        var context = new Mock<ConsumeContext<UserAccountStatusChanged>>();
        context.SetupGet(consumeContext => consumeContext.Message).Returns(new UserAccountStatusChanged { UserId = 42, Status = 1 });
        await new UserAccountStatusChangedConsumer(cache).Consume(context.Object);
        oldResponse.SetResult(new GetAccountRestrictionResponse
        {
            Restriction = new AccountRestrictionInfo { Status = AccountStatus.Active },
        });

        (await lookup).Should().Be(AccountStatus.Blocked);
        Assert.Equal(2, calls);
    }

    private static AsyncUnaryCall<T> CreateAsyncCall<T>(Task<T> response)
        => new(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
