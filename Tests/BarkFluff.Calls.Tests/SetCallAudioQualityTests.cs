using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Services;
using BarkFluff.Proto.Calls;

using Grpc.Core;

namespace BarkFluff.Calls.Tests;

public class SetCallAudioQualityTests
{
    [Fact]
    public async Task ActiveDirectCall_StoresQualityAndBroadcastsToAllParticipants()
    {
        var db = TestHelper.CreateContext();
        var call = TestHelper.AddDirectCall(db, caller: 1, callee: 2, CallStatus.Active);
        var subs = new CallEventSubscriptionsManager();
        var quality = new CallQualityStore();
        var service = TestHelper.CreateService(db, actingUserId: 1, subs, quality, deviceId: Guid.NewGuid().ToString());

        var callerStream = new CapturingStreamWriter();
        var calleeStream = new CapturingStreamWriter();
        subs.RegisterSubscription(1, Guid.NewGuid(), callerStream);
        subs.RegisterSubscription(2, Guid.NewGuid(), calleeStream);

        await service.SetAudioQualityAsync(
            new SetCallAudioQualityRequest { CallId = call.Id.ToString(), Quality = CallAudioQuality.High },
            CancellationToken.None);

        // Сохранено как общее состояние звонка.
        quality.GetAudio(call.Id).Should().Be(CallAudioQualityKind.High);

        // Разослано ВСЕМ участникам, включая инициатора смены (единый источник истины).
        foreach (var stream in new[] { callerStream, calleeStream })
        {
            stream.Events.Should().ContainSingle();
            var evt = stream.Events[0];
            evt.EventCase.Should().Be(CallEvent.EventOneofCase.AudioQuality);
            evt.AudioQuality.CallId.Should().Be(call.Id.ToString());
            evt.AudioQuality.Quality.Should().Be(CallAudioQuality.High);
            evt.AudioQuality.ChangedByUserId.Should().Be(1);
        }
    }

    [Fact]
    public async Task CalleeCanAlsoChange_SincePolicyIsShared()
    {
        var db = TestHelper.CreateContext();
        var call = TestHelper.AddDirectCall(db, caller: 1, callee: 2, CallStatus.Active);
        var quality = new CallQualityStore();
        // Меняет получатель (callee=2) — качество голоса общее, вправе любой участник.
        var service = TestHelper.CreateService(db, actingUserId: 2, new CallEventSubscriptionsManager(), quality);

        await service.SetAudioQualityAsync(
            new SetCallAudioQualityRequest { CallId = call.Id.ToString(), Quality = CallAudioQuality.Medium },
            CancellationToken.None);

        quality.GetAudio(call.Id).Should().Be(CallAudioQualityKind.Medium);
    }

    [Fact]
    public async Task RingingCall_Throws_FailedPrecondition()
    {
        var db = TestHelper.CreateContext();
        var call = TestHelper.AddDirectCall(db, 1, 2, CallStatus.Ringing);
        var service = TestHelper.CreateService(db, 1, new CallEventSubscriptionsManager(), new CallQualityStore());

        var act = () => service.SetAudioQualityAsync(
            new SetCallAudioQualityRequest { CallId = call.Id.ToString(), Quality = CallAudioQuality.Low },
            CancellationToken.None);

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task NonParticipant_Throws_PermissionDenied()
    {
        var db = TestHelper.CreateContext();
        var call = TestHelper.AddDirectCall(db, caller: 1, callee: 2, CallStatus.Active);
        var service = TestHelper.CreateService(db, actingUserId: 99, new CallEventSubscriptionsManager(), new CallQualityStore());

        var act = () => service.SetAudioQualityAsync(
            new SetCallAudioQualityRequest { CallId = call.Id.ToString(), Quality = CallAudioQuality.Medium },
            CancellationToken.None);

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task UnknownCall_Throws_NotFound()
    {
        var db = TestHelper.CreateContext();
        var service = TestHelper.CreateService(db, 1, new CallEventSubscriptionsManager(), new CallQualityStore());

        var act = () => service.SetAudioQualityAsync(
            new SetCallAudioQualityRequest { CallId = Guid.NewGuid().ToString(), Quality = CallAudioQuality.High },
            CancellationToken.None);

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.NotFound);
    }
}
