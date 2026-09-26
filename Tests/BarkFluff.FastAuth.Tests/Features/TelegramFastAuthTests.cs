using BarkFluff.FastAuth.Features.AcceptFastAuth;
using BarkFluff.FastAuth.Features.GenerateFastAuthToken;
using BarkFluff.FastAuth.Features.SubscribeFastAuthResult;
using BarkFluff.FastAuth.Infrastructure;
using BarkFluff.Proto.FastAuth;
using BarkFluff.Proto.Identity;
using Grpc.Core;

namespace BarkFluff.FastAuth.Tests.Features;

public class TelegramFastAuthTests
{
    [Fact]
    public async Task ApprovedQr_WaitsWithoutTokens_AndReconnectCompletesAfterTelegram()
    {
        var h = new TestHelper();
        var identity = h.CreateIdentityClientMock();
        identity.Setup(x => x.CreateSessionForUserServerAsync(It.IsAny<CreateSessionForUserServerRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(() => Unary(new CreateSessionForUserServerResponse { ConfirmationState = AuthChallengeState.Waiting }));
        var (session, code) = await h.CreateAndScanSessionAsync();
        var accept = new AcceptFastAuthCommandHandler(h.Store, h.EventBus, identity.Object, h.CreateUserContext(42),
            h.Metrics, TestHelper.CreateLogger<AcceptFastAuthCommandHandler>());
        await accept.Handle(new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code }, default);
        var waiting = await h.Store.GetAsync(session.Id);
        waiting!.Status.Should().Be(FastAuthStatus.TelegramPending);
        waiting.ExpiresAt.Should().Be(session.ExpiresAt);
        waiting.Result.Should().BeNull();
        var stream = h.CreateMockStreamWriter();
        var completion = new FastAuthCompletion(h.Store, h.EventBus, identity.Object, h.Metrics);
        var subscriber = new SubscribeFastAuthResultQueryHandler(h.Store, h.EventBus, h.Metrics,
            TestHelper.CreateLogger<SubscribeFastAuthResultQueryHandler>(), completion);
        using var disconnected = new CancellationTokenSource();
        var first = subscriber.Handle(new SubscribeFastAuthResultQuery { FastAuthId = session.Id,
            ResponseStream = stream.Object, CancellationToken = disconnected.Token });
        disconnected.Cancel(); await first;
        stream.Verify(x => x.WriteAsync(It.Is<FastAuthResult>(r => !string.IsNullOrEmpty(r.AccessToken)), It.IsAny<CancellationToken>()), Times.Never);
        h.SetupIdentityClientSuccess(identity);
        await subscriber.Handle(new SubscribeFastAuthResultQuery { FastAuthId = session.Id, ResponseStream = stream.Object, CancellationToken = default });
        (await h.Store.GetAsync(session.Id))!.Status.Should().Be(FastAuthStatus.Accepted);
        stream.Verify(x => x.WriteAsync(It.Is<FastAuthResult>(r => r.Status == FastAuthStatus.Accepted), It.IsAny<CancellationToken>()), Times.Once);
        await accept.Handle(new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code }, default);
        identity.Verify(x => x.RemoveActiveSessionServerAsync(It.IsAny<RemoveActiveSessionServerRequest>(), null, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WaitingTelegram_CanExpireWithoutExtendingQrLifetime()
    {
        var h = new TestHelper(); var (session, code) = await h.CreateAndScanSessionAsync();
        await h.Store.TryWaitForTelegramAsync(session.Id, code, 42);
        await h.Store.TryExpireAsync(session.Id);
        (await h.Store.GetAsync(session.Id))!.Status.Should().Be(FastAuthStatus.Expired);
        (await h.Store.TryWaitForTelegramAsync(session.Id, code, 42)).Should().NotBe(BarkFluff.FastAuth.Domain.FastAuthTransition.Ok);
    }

    [Fact]
    public async Task AcceptedQr_UsesBrowserDeviceIdAndQrAttemptIdSeparately()
    {
        var h = new TestHelper();
        var deviceId = Guid.NewGuid().ToString();
        var generate = new GenerateFastAuthTokenCommandHandler(h.Store, new QrCodeGenerator(),
            h.CreateRequestContext(deviceId: deviceId), h.Metrics,
            TestHelper.CreateLogger<GenerateFastAuthTokenCommandHandler>());
        var generated = await generate.Handle(new GenerateFastAuthTokenCommand { Format = TokenFormat.Text }, default);
        var session = (await h.Store.GetAsync(generated.FastAuthId))!;
        var code = Guid.NewGuid().ToString();
        await h.Store.TryScanAsync(session.Id, 42, code);

        var identity = h.CreateIdentityClientMock();
        h.SetupIdentityClientSuccess(identity);
        var accept = new AcceptFastAuthCommandHandler(h.Store, h.EventBus, identity.Object,
            h.CreateUserContext(42), h.Metrics, TestHelper.CreateLogger<AcceptFastAuthCommandHandler>());

        await accept.Handle(new AcceptFastAuthCommand { FastAuthId = session.Id, ConfirmationCode = code }, default);

        identity.Verify(x => x.CreateSessionForUserServerAsync(
            It.Is<CreateSessionForUserServerRequest>(request =>
                request.DeviceId == deviceId && request.AttemptId == session.Id),
            null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AsyncUnaryCall<T> Unary<T>(T response) => new(Task.FromResult(response), Task.FromResult(new Metadata()),
        () => Status.DefaultSuccess, () => new Metadata(), () => { });
}
