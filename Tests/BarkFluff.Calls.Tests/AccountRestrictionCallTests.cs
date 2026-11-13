using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Features.CallLifecycle;
using BarkFluff.Calls.Services;
using BarkFluff.Proto.Calls;
using BarkFluff.Proto.Users;
using BarkFluff.Shared.Exceptions.Users;

using Grpc.Core;

namespace BarkFluff.Calls.Tests;

public class AccountRestrictionCallTests
{
    [Fact]
    public async Task InitiateDirectCall_RestrictedCallee_IsRejectedBeforeCallCreation()
    {
        var db = TestHelper.CreateContext();
        var usersClient = CreateUsersClient(AccountStatus.Blocked);
        var service = TestHelper.CreateService(
            db,
            actingUserId: 1,
            new CallEventSubscriptionsManager(),
            new CallQualityStore(),
            usersClient: usersClient);

        var act = () => service.InitiateAsync(new InitiateCallRequest
        {
            CalleeUserId = 2,
            MediaType = CallMediaType.CallMediaAudio
        }, CancellationToken.None);

        await act.Should().ThrowAsync<UserUnavailableException>();
        db.CallSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Accept_RingingCallPeerBecameRestricted_IsRejectedWithoutActivation()
    {
        var db = TestHelper.CreateContext();
        var call = TestHelper.AddDirectCall(db, caller: 1, callee: 2, CallStatus.Ringing);
        var service = TestHelper.CreateService(
            db,
            actingUserId: 2,
            new CallEventSubscriptionsManager(),
            new CallQualityStore(),
            deviceId: Guid.NewGuid().ToString(),
            usersClient: CreateUsersClient(AccountStatus.PendingDeletion));

        var act = () => service.AcceptAsync(call.Id.ToString(), CancellationToken.None);

        await act.Should().ThrowAsync<UserUnavailableException>();
        (await db.CallSessions.FindAsync(call.Id))!.Status.Should().Be(CallStatus.Ringing);
    }

    [Fact]
    public async Task Join_ActiveDirectCallPeerRestricted_IsRejected()
    {
        var db = TestHelper.CreateContext();
        var call = TestHelper.AddDirectCall(db, caller: 1, callee: 2, CallStatus.Active);
        var service = TestHelper.CreateService(
            db,
            actingUserId: 1,
            new CallEventSubscriptionsManager(),
            new CallQualityStore(),
            deviceId: Guid.NewGuid().ToString(),
            usersClient: CreateUsersClient(AccountStatus.Blocked));

        var act = () => service.JoinAsync(call.Id.ToString(), CancellationToken.None);

        await act.Should().ThrowAsync<UserUnavailableException>();
    }

    private static UsersServerApi.UsersServerApiClient CreateUsersClient(AccountStatus status)
    {
        var usersClient = new Mock<UsersServerApi.UsersServerApiClient>();
        usersClient.Setup(c => c.GetByIdAsync(It.IsAny<GetByIdRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns((GetByIdRequest request, Metadata? _, DateTime? _, CancellationToken _) =>
                new AsyncUnaryCall<GetByIdResponse>(
                    Task.FromResult(new GetByIdResponse { User = new User { Id = request.UserId, AccountStatus = status } }),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => new Metadata(),
                    () => { }));
        return usersClient.Object;
    }
}
