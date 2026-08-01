using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Features.CallLifecycle;
using BarkFluff.Calls.Persistence;
using BarkFluff.Calls.Services;
using BarkFluff.Proto.Calls;
using BarkFluff.Proto.Messages;

using Grpc.Core;

namespace BarkFluff.Calls.Tests;

public class InitiateGroupCallTests
{
    [Fact]
    public async Task ExistingRingingCallInChat_ThrowsFailedPrecondition()
    {
        var db = TestHelper.CreateContext();
        var chatId = Guid.NewGuid();
        AddGroupCall(db, chatId, CallStatus.Ringing, DateTime.UtcNow.AddMinutes(-1));
        var service = CreateService(db, chatId);

        var act = () => service.InitiateAsync(CreateRequest(chatId), CancellationToken.None);

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        db.CallSessions.Should().HaveCount(1);
    }

    [Fact]
    public async Task CallStartedLessThanTenSecondsAgo_ThrowsResourceExhausted()
    {
        var db = TestHelper.CreateContext();
        var chatId = Guid.NewGuid();
        AddGroupCall(db, chatId, CallStatus.Ended, DateTime.UtcNow.AddSeconds(-9));
        var service = CreateService(db, chatId);

        var act = () => service.InitiateAsync(CreateRequest(chatId), CancellationToken.None);

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.ResourceExhausted);
        db.CallSessions.Should().HaveCount(1);
    }

    [Fact]
    public async Task CallStartedMoreThanTenSecondsAgo_CanStartNewCall()
    {
        var db = TestHelper.CreateContext();
        var chatId = Guid.NewGuid();
        AddGroupCall(db, chatId, CallStatus.Ended, DateTime.UtcNow.AddSeconds(-11));
        var service = CreateService(db, chatId);

        await service.InitiateAsync(CreateRequest(chatId), CancellationToken.None);

        db.CallSessions.Should().HaveCount(2);
        db.CallSessions.Single(c => c.Status != CallStatus.Ended).ChatId.Should().Be(chatId);
    }

    private static CallLifecycleHandler CreateService(CallsContext db, Guid chatId)
    {
        var messages = new Mock<MessagesServerApi.MessagesServerApiClient>();
        var membership = new CheckChatMembershipResponse();
        membership.MemberChatIds.Add(chatId.ToString());
        messages
            .Setup(c => c.CheckChatMembershipAsync(
                It.IsAny<CheckChatMembershipRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<CheckChatMembershipResponse>(
                Task.FromResult(membership),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        var members = new GetChatMemberIdsResponse();
        members.UserIds.Add(1);
        members.UserIds.Add(2);
        messages
            .Setup(c => c.GetChatMemberIdsAsync(
                It.IsAny<GetChatMemberIdsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(new AsyncUnaryCall<GetChatMemberIdsResponse>(
                Task.FromResult(members),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }));

        return TestHelper.CreateService(
            db,
            actingUserId: 1,
            new CallEventSubscriptionsManager(),
            new CallQualityStore(),
            messagesClient: messages.Object);
    }

    private static InitiateCallRequest CreateRequest(Guid chatId)
        => new()
        {
            ChatId = chatId.ToString(),
            MediaType = CallMediaType.CallMediaAudio,
        };

    private static void AddGroupCall(CallsContext db, Guid chatId, CallStatus status, DateTime startedAt)
    {
        db.CallSessions.Add(new CallSession
        {
            Id = Guid.NewGuid(),
            CallerUserId = 2,
            ChatId = chatId,
            RoomName = "call:test",
            Media = CallMediaKind.Audio,
            Status = status,
            EndReason = status == CallStatus.Ended ? CallEndReasonKind.Hangup : CallEndReasonKind.None,
            StartedAt = startedAt,
            EndedAt = status == CallStatus.Ended ? startedAt.AddSeconds(1) : null,
        });
        db.SaveChanges();
    }
}
