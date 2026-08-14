using BarkFluff.FastAuth.Domain;
using BarkFluff.FastAuth.Tests.Fakes;
using BarkFluff.Proto.FastAuth;

namespace BarkFluff.FastAuth.Tests.Infrastructure;

/// <summary>
/// Машина состояний QR-сессии. Фейк зеркалит Lua-скрипты RedisFastAuthSessionStore,
/// поэтому эти тесты — исполняемая спецификация переходов для обеих реализаций.
/// </summary>
public class InMemoryFastAuthSessionStoreTests
{
    private readonly InMemoryFastAuthSessionStore _store = new();

    private async Task<(FastAuthSessionState Session, string Code)> CreateScannedAsync(
        long userId = 42, DateTime? expiresAt = null)
    {
        var session = _store.Create("D", "OS", "A", "V", "IP");

        var code = Guid.NewGuid().ToString();
        (await _store.TryScanAsync(session.Id, userId, code)).Should().Be(FastAuthTransition.Ok);

        if (expiresAt.HasValue)
        {
            // Имитируем истечение после скана: время шло, сессия уже отсканирована.
            _store.Seed((await _store.GetAsync(session.Id))! with { ExpiresAt = expiresAt.Value });
        }

        return (await _store.GetAsync(session.Id)!, code);
    }

    private static FastAuthSessionResult AcceptedResult() => new(
        FastAuthStatus.Accepted, "access", DateTime.UtcNow.AddHours(1),
        "refresh", DateTime.UtcNow.AddDays(30));

    #region Create / Get

    [Fact]
    public async Task CreateAsync_SetsGuidIdAndPendingStatus()
    {
        var session = await _store.CreateAsync("Device", "Android", "BarkFluff", "2.0", "10.0.0.1");

        Guid.TryParse(session.Id, out _).Should().BeTrue();
        session.Status.Should().Be(FastAuthStatus.Pending);
        session.DeviceName.Should().Be("Device");
        session.OperationSystem.Should().Be("Android");
        session.AppName.Should().Be("BarkFluff");
        session.AppVersion.Should().Be("2.0");
        session.IpAddress.Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task CreateAsync_SetsExpiresAtToCreatedAtPlusTtl()
    {
        var before = DateTime.UtcNow;
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");

        session.ExpiresAt.Should().BeOnOrAfter(before + FastAuthSessionTiming.SessionTtl - TimeSpan.FromSeconds(1));
        session.ExpiresAt.Should().BeOnOrBefore(DateTime.UtcNow + FastAuthSessionTiming.SessionTtl + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetAsync_ExistingSession_ReturnsState()
    {
        var created = await _store.CreateAsync("D", "OS", "A", "V", "IP");

        var found = await _store.GetAsync(created.Id);

        found.Should().NotBeNull();
        found!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetAsync_NonexistentSession_ReturnsNull()
    {
        (await _store.GetAsync("nonexistent")).Should().BeNull();
    }

    #endregion

    #region Scan

    [Fact]
    public async Task TryScanAsync_PendingSession_ReturnsOkAndStoresCodeAndUser()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");

        var transition = await _store.TryScanAsync(session.Id, 42, "code-1");

        transition.Should().Be(FastAuthTransition.Ok);
        var stored = await _store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Scanned);
        stored.ConfirmationCode.Should().Be("code-1");
        stored.UserId.Should().Be(42);
    }

    [Fact]
    public async Task TryScanAsync_Nonexistent_ReturnsNotFound()
    {
        (await _store.TryScanAsync("nonexistent", 42, "code"))
            .Should().Be(FastAuthTransition.NotFound);
    }

    [Fact]
    public async Task TryScanAsync_ExpiredByWallClock_ReturnsExpiredAndFinalizes()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");
        _store.Seed(session with { ExpiresAt = DateTime.UtcNow - TimeSpan.FromSeconds(1) });

        (await _store.TryScanAsync(session.Id, 42, "code")).Should().Be(FastAuthTransition.Expired);

        var stored = await _store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Expired);
        stored.Result!.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public async Task TryScanAsync_AlreadyScanned_ReturnsInvalidState()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");
        await _store.TryScanAsync(session.Id, 42, "code-1");

        (await _store.TryScanAsync(session.Id, 42, "code-2"))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    [Fact]
    public async Task TryScanAsync_AlreadyFinal_ReturnsInvalidState()
    {
        var (session, _) = await CreateScannedAsync();
        await _store.TryRejectAsync(session.Id, session.ConfirmationCode!, 42);

        (await _store.TryScanAsync(session.Id, 42, "code"))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    #endregion

    #region Accept

    [Fact]
    public async Task TryAcceptAsync_ScannedSessionWithMatchingCodeAndUser_ReturnsOkAndStoresResult()
    {
        var (session, code) = await CreateScannedAsync(userId: 42);
        var result = AcceptedResult();

        var transition = await _store.TryAcceptAsync(session.Id, code, 42, result);

        transition.Should().Be(FastAuthTransition.Ok);
        var stored = await _store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Accepted);
        stored.IsFinal.Should().BeTrue();
        stored.FinalizedAt.Should().NotBeNull();
        stored.Result!.AccessToken.Should().Be("access");
        stored.Result.RefreshToken.Should().Be("refresh");
    }

    [Fact]
    public async Task TryAcceptAsync_WrongCode_ReturnsInvalidState()
    {
        var (session, _) = await CreateScannedAsync();

        (await _store.TryAcceptAsync(session.Id, "wrong", 42, AcceptedResult()))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    [Fact]
    public async Task TryAcceptAsync_WrongUser_ReturnsInvalidState()
    {
        var (session, code) = await CreateScannedAsync(userId: 42);

        (await _store.TryAcceptAsync(session.Id, code, 99, AcceptedResult()))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    [Fact]
    public async Task TryAcceptAsync_PendingSession_ReturnsInvalidState()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");

        (await _store.TryAcceptAsync(session.Id, "code", 42, AcceptedResult()))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    [Fact]
    public async Task TryAcceptAsync_AfterReject_ReturnsInvalidState()
    {
        var (session, code) = await CreateScannedAsync();
        await _store.TryRejectAsync(session.Id, code, 42);

        (await _store.TryAcceptAsync(session.Id, code, 42, AcceptedResult()))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    [Fact]
    public async Task TryAcceptAsync_ExpiredByWallClock_ReturnsExpiredAndFinalizes()
    {
        var (session, code) = await CreateScannedAsync(
            expiresAt: DateTime.UtcNow - TimeSpan.FromSeconds(1));

        (await _store.TryAcceptAsync(session.Id, code, 42, AcceptedResult()))
            .Should().Be(FastAuthTransition.Expired);

        var stored = await _store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public async Task TryAcceptAsync_Nonexistent_ReturnsNotFound()
    {
        (await _store.TryAcceptAsync("nonexistent", "code", 42, AcceptedResult()))
            .Should().Be(FastAuthTransition.NotFound);
    }

    #endregion

    #region Reject

    [Fact]
    public async Task TryRejectAsync_ScannedSessionWithMatchingCodeAndUser_ReturnsOk()
    {
        var (session, code) = await CreateScannedAsync(userId: 42);

        var transition = await _store.TryRejectAsync(session.Id, code, 42);

        transition.Should().Be(FastAuthTransition.Ok);
        var stored = await _store.GetAsync(session.Id);
        stored!.Status.Should().Be(FastAuthStatus.Rejected);
        stored.Result!.Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public async Task TryRejectAsync_WrongCode_ReturnsInvalidState()
    {
        var (session, _) = await CreateScannedAsync();

        (await _store.TryRejectAsync(session.Id, "wrong", 42))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    [Fact]
    public async Task TryRejectAsync_WrongUser_ReturnsInvalidState()
    {
        var (session, code) = await CreateScannedAsync(userId: 42);

        (await _store.TryRejectAsync(session.Id, code, 99))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    [Fact]
    public async Task TryRejectAsync_PendingSession_ReturnsInvalidState()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");

        (await _store.TryRejectAsync(session.Id, "code", 42))
            .Should().Be(FastAuthTransition.InvalidState);
    }

    #endregion

    #region Expire

    [Fact]
    public async Task TryExpireAsync_PendingSession_ReturnsTrue()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");

        (await _store.TryExpireAsync(session.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task TryExpireAsync_ScannedSession_ReturnsTrue()
    {
        var (session, _) = await CreateScannedAsync();

        (await _store.TryExpireAsync(session.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task TryExpireAsync_AlreadyFinal_ReturnsFalse()
    {
        var (session, code) = await CreateScannedAsync();
        await _store.TryRejectAsync(session.Id, code, 42);

        (await _store.TryExpireAsync(session.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task TryExpireAsync_Nonexistent_ReturnsFalse()
    {
        (await _store.TryExpireAsync("nonexistent")).Should().BeFalse();
    }

    #endregion

    #region Subscriber lock

    [Fact]
    public async Task TryAttachSubscriberAsync_FirstCall_ReturnsOwnerToken()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");

        var token = await _store.TryAttachSubscriberAsync(session.Id, TimeSpan.FromMinutes(6));

        token.Should().NotBeNull();
    }

    [Fact]
    public async Task TryAttachSubscriberAsync_SecondCall_ReturnsNull()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");
        await _store.TryAttachSubscriberAsync(session.Id, TimeSpan.FromMinutes(6));

        (await _store.TryAttachSubscriberAsync(session.Id, TimeSpan.FromMinutes(6)))
            .Should().BeNull();
    }

    [Fact]
    public async Task ReleaseSubscriberAsync_WithWrongToken_DoesNotRelease()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");
        await _store.TryAttachSubscriberAsync(session.Id, TimeSpan.FromMinutes(6));

        await _store.ReleaseSubscriberAsync(session.Id, "wrong-token");

        (await _store.TryAttachSubscriberAsync(session.Id, TimeSpan.FromMinutes(6)))
            .Should().BeNull();
    }

    [Fact]
    public async Task ReleaseSubscriberAsync_WithOwnerToken_AllowsReattach()
    {
        var session = await _store.CreateAsync("D", "OS", "A", "V", "IP");
        var token = await _store.TryAttachSubscriberAsync(session.Id, TimeSpan.FromMinutes(6));

        await _store.ReleaseSubscriberAsync(session.Id, token!);

        (await _store.TryAttachSubscriberAsync(session.Id, TimeSpan.FromMinutes(6)))
            .Should().NotBeNull();
    }

    #endregion

    #region Timing constants

    [Fact]
    public void SessionTtl_IsFiveMinutes()
    {
        FastAuthSessionTiming.SessionTtl.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void FinalRetention_IsThirtySeconds()
    {
        FastAuthSessionTiming.FinalRetention.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion
}
