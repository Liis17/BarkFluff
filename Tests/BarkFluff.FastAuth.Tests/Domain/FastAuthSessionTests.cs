using BarkFluff.FastAuth.Domain;
using BarkFluff.Proto.FastAuth;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.FastAuth.Tests.Domain;

public class FastAuthSessionTests
{
    private FastAuthSession CreateSession(DateTime? expiresAt = null)
    {
        return new FastAuthSession
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt ?? DateTime.UtcNow + TimeSpan.FromMinutes(5),
            DeviceName = "TestDevice",
            OperationSystem = "Windows",
            AppName = "BarkFluff",
            AppVersion = "1.0",
            IpAddress = "127.0.0.1"
        };
    }

    private FastAuthResult CreateAcceptedResult()
    {
        return new FastAuthResult
        {
            Status = FastAuthStatus.Accepted,
            AccessToken = "access_token",
            RefreshToken = "refresh_token"
        };
    }

    #region Initial State

    [Fact]
    public void InitialStatus_IsPending()
    {
        var session = CreateSession();
        session.Status.Should().Be(FastAuthStatus.Pending);
    }

    [Fact]
    public void InitialConfirmationCode_IsNull()
    {
        var session = CreateSession();
        session.ConfirmationCode.Should().BeNull();
    }

    [Fact]
    public void InitialUserId_IsNull()
    {
        var session = CreateSession();
        session.UserId.Should().BeNull();
    }

    [Fact]
    public void InitialFinalizedAt_IsNull()
    {
        var session = CreateSession();
        session.FinalizedAt.Should().BeNull();
    }

    [Fact]
    public void IsInitial_Final_ShouldBeFalse()
    {
        var session = CreateSession();
        session.IsFinal.Should().BeFalse();
    }

    [Fact]
    public void Properties_AreCorrectlyAssigned()
    {
        var session = CreateSession();
        session.DeviceName.Should().Be("TestDevice");
        session.OperationSystem.Should().Be("Windows");
        session.AppName.Should().Be("BarkFluff");
        session.AppVersion.Should().Be("1.0");
        session.IpAddress.Should().Be("127.0.0.1");
    }

    #endregion

    #region TryAttachSubscriber

    [Fact]
    public void TryAttachSubscriber_FirstCall_ReturnsTrue()
    {
        var session = CreateSession();
        session.TryAttachSubscriber().Should().BeTrue();
    }

    [Fact]
    public void TryAttachSubscriber_SecondCall_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryAttachSubscriber();
        session.TryAttachSubscriber().Should().BeFalse();
    }

    [Fact]
    public void TryAttachSubscriber_MultipleCalls_OnlyFirstSucceeds()
    {
        var session = CreateSession();
        session.TryAttachSubscriber().Should().BeTrue();
        session.TryAttachSubscriber().Should().BeFalse();
        session.TryAttachSubscriber().Should().BeFalse();
        session.TryAttachSubscriber().Should().BeFalse();
    }

    #endregion

    #region TryScan

    [Fact]
    public void TryScan_FromPending_ReturnsOk()
    {
        var session = CreateSession();
        var outcome = session.TryScan(userId: 42);
        outcome.Should().Be(ScanOutcome.Ok);
    }

    [Fact]
    public void TryScan_FromPending_SetsStatusToScanned()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public void TryScan_FromPending_SetsUserId()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.UserId.Should().Be(42);
    }

    [Fact]
    public void TryScan_FromPending_SetsConfirmationCode()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.ConfirmationCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryScan_FromPending_WritesScannedEvent()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.Events.TryRead(out var evt).Should().BeTrue();
        evt.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public void TryScan_WhenExpired_ReturnsExpired()
    {
        var session = CreateSession(expiresAt: DateTime.UtcNow - TimeSpan.FromMinutes(1));
        var outcome = session.TryScan(userId: 42);
        outcome.Should().Be(ScanOutcome.Expired);
    }

    [Fact]
    public void TryScan_WhenExpired_DoesNotChangeStatus()
    {
        var session = CreateSession(expiresAt: DateTime.UtcNow - TimeSpan.FromMinutes(1));
        session.TryScan(userId: 42);
        session.Status.Should().Be(FastAuthStatus.Pending);
    }

    [Fact]
    public void TryScan_AlreadyScanned_ReturnsAlreadyHandled()
    {
        var session = CreateSession();
        session.TryScan(userId: 1);
        var outcome = session.TryScan(userId: 2);
        outcome.Should().Be(ScanOutcome.AlreadyHandled);
    }

    [Fact]
    public void TryScan_AlreadyScanned_KeepsOriginalUserId()
    {
        var session = CreateSession();
        session.TryScan(userId: 1);
        session.TryScan(userId: 2);
        session.UserId.Should().Be(1);
    }

    [Fact]
    public void TryScan_AlreadyAccepted_ReturnsAlreadyHandled()
    {
        var session = CreateSession();
        session.TryScan(userId: 1);
        session.TryAccept(session.ConfirmationCode!, 1, CreateAcceptedResult());
        var outcome = session.TryScan(userId: 2);
        outcome.Should().Be(ScanOutcome.AlreadyHandled);
    }

    [Fact]
    public void TryScan_AlreadyRejected_ReturnsAlreadyHandled()
    {
        var session = CreateSession();
        session.TryScan(userId: 1);
        session.TryReject(session.ConfirmationCode!, 1);
        var outcome = session.TryScan(userId: 2);
        outcome.Should().Be(ScanOutcome.AlreadyHandled);
    }

    [Fact]
    public void TryScan_AlreadyExpired_ReturnsAlreadyHandled()
    {
        var session = CreateSession();
        session.TryExpire();
        var outcome = session.TryScan(userId: 1);
        outcome.Should().Be(ScanOutcome.AlreadyHandled);
    }

    #endregion

    #region TryAccept

    [Fact]
    public void TryAccept_CorrectCodeAndUser_ReturnsTrue()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var result = session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        result.Should().BeTrue();
    }

    [Fact]
    public void TryAccept_CorrectCodeAndUser_SetsStatusAccepted()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        session.Status.Should().Be(FastAuthStatus.Accepted);
    }

    [Fact]
    public void TryAccept_CorrectCodeAndUser_SetsFinalizedAt()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var before = DateTime.UtcNow;
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        var after = DateTime.UtcNow;
        session.FinalizedAt.Should().NotBeNull();
        session.FinalizedAt.Should().BeOnOrAfter(before);
        session.FinalizedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void TryAccept_CorrectCodeAndUser_MakesIsFinalTrue()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void TryAccept_CorrectCodeAndUser_WritesAcceptedEvent()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var acceptedResult = CreateAcceptedResult();
        session.TryAccept(session.ConfirmationCode!, 42, acceptedResult);

        session.Events.TryRead(out var scanEvt).Should().BeTrue();
        session.Events.TryRead(out var acceptEvt).Should().BeTrue();
        acceptEvt.Status.Should().Be(FastAuthStatus.Accepted);
        acceptEvt.AccessToken.Should().Be("access_token");
        acceptEvt.RefreshToken.Should().Be("refresh_token");
    }

    [Fact]
    public void TryAccept_CorrectCodeAndUser_CompletesChannel()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());

        while (session.Events.TryRead(out _)) { }
        session.Events.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void TryAccept_WrongConfirmationCode_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var result = session.TryAccept("wrong-code", 42, CreateAcceptedResult());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryAccept_WrongConfirmationCode_DoesNotChangeStatus()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept("wrong-code", 42, CreateAcceptedResult());
        session.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public void TryAccept_WrongUserId_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var result = session.TryAccept(session.ConfirmationCode!, 99, CreateAcceptedResult());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryAccept_WrongUserId_DoesNotChangeStatus()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 99, CreateAcceptedResult());
        session.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public void TryAccept_WhenPending_ReturnsFalse()
    {
        var session = CreateSession();
        var result = session.TryAccept("any-code", 42, CreateAcceptedResult());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryAccept_WhenAlreadyAccepted_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        var result = session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryAccept_WhenRejected_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);
        var result = session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryAccept_WhenExpired_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryExpire();
        var result = session.TryAccept("any-code", 42, CreateAcceptedResult());
        result.Should().BeFalse();
    }

    #endregion

    #region TryReject

    [Fact]
    public void TryReject_CorrectCodeAndUser_ReturnsTrue()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var result = session.TryReject(session.ConfirmationCode!, 42);
        result.Should().BeTrue();
    }

    [Fact]
    public void TryReject_CorrectCodeAndUser_SetsStatusRejected()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);
        session.Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public void TryReject_CorrectCodeAndUser_SetsFinalizedAt()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var before = DateTime.UtcNow;
        session.TryReject(session.ConfirmationCode!, 42);
        var after = DateTime.UtcNow;
        session.FinalizedAt.Should().NotBeNull();
        session.FinalizedAt.Should().BeOnOrAfter(before);
        session.FinalizedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void TryReject_CorrectCodeAndUser_MakesIsFinalTrue()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);
        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void TryReject_CorrectCodeAndUser_WritesRejectedEvent()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);

        session.Events.TryRead(out var scanEvt).Should().BeTrue();
        session.Events.TryRead(out var rejectEvt).Should().BeTrue();
        rejectEvt.Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public void TryReject_CorrectCodeAndUser_CompletesChannel()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);

        while (session.Events.TryRead(out _)) { }
        session.Events.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void TryReject_WrongConfirmationCode_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var result = session.TryReject("wrong-code", 42);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReject_WrongConfirmationCode_DoesNotChangeStatus()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject("wrong-code", 42);
        session.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public void TryReject_WrongUserId_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var result = session.TryReject(session.ConfirmationCode!, 99);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReject_WrongUserId_DoesNotChangeStatus()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 99);
        session.Status.Should().Be(FastAuthStatus.Scanned);
    }

    [Fact]
    public void TryReject_WhenPending_ReturnsFalse()
    {
        var session = CreateSession();
        var result = session.TryReject("any-code", 42);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReject_WhenAlreadyAccepted_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        var result = session.TryReject(session.ConfirmationCode!, 42);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReject_WhenAlreadyRejected_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);
        var result = session.TryReject(session.ConfirmationCode!, 42);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReject_WhenExpired_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryExpire();
        var result = session.TryReject("any-code", 42);
        result.Should().BeFalse();
    }

    #endregion

    #region TryExpire

    [Fact]
    public void TryExpire_FromPending_ReturnsTrue()
    {
        var session = CreateSession();
        session.TryExpire().Should().BeTrue();
    }

    [Fact]
    public void TryExpire_FromPending_SetsStatusExpired()
    {
        var session = CreateSession();
        session.TryExpire();
        session.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public void TryExpire_FromPending_SetsFinalizedAt()
    {
        var session = CreateSession();
        var before = DateTime.UtcNow;
        session.TryExpire();
        var after = DateTime.UtcNow;
        session.FinalizedAt.Should().NotBeNull();
        session.FinalizedAt.Should().BeOnOrAfter(before);
        session.FinalizedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void TryExpire_FromPending_MakesIsFinalTrue()
    {
        var session = CreateSession();
        session.TryExpire();
        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void TryExpire_FromPending_WritesExpiredEvent()
    {
        var session = CreateSession();
        session.TryExpire();
        session.Events.TryRead(out var evt).Should().BeTrue();
        evt.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public void TryExpire_FromPending_CompletesChannel()
    {
        var session = CreateSession();
        session.TryExpire();

        while (session.Events.TryRead(out _)) { }
        session.Events.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void TryExpire_FromScanned_ReturnsTrue()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryExpire().Should().BeTrue();
    }

    [Fact]
    public void TryExpire_FromScanned_SetsStatusExpired()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryExpire();
        session.Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public void TryExpire_WhenAlreadyAccepted_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        session.TryExpire().Should().BeFalse();
    }

    [Fact]
    public void TryExpire_WhenAlreadyRejected_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);
        session.TryExpire().Should().BeFalse();
    }

    [Fact]
    public void TryExpire_WhenAlreadyExpired_ReturnsFalse()
    {
        var session = CreateSession();
        session.TryExpire();
        session.TryExpire().Should().BeFalse();
    }

    [Fact]
    public void TryExpire_DoesNotOverrideAcceptedStatus()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        session.TryExpire();
        session.Status.Should().Be(FastAuthStatus.Accepted);
    }

    #endregion

    #region Full Lifecycle

    [Fact]
    public void FullLifecycle_Pending_Scanned_Accepted()
    {
        var session = CreateSession();
        session.Status.Should().Be(FastAuthStatus.Pending);

        var scanOutcome = session.TryScan(userId: 42);
        scanOutcome.Should().Be(ScanOutcome.Ok);
        session.Status.Should().Be(FastAuthStatus.Scanned);
        session.UserId.Should().Be(42);
        var code = session.ConfirmationCode!;

        var acceptResult = session.TryAccept(code, 42, CreateAcceptedResult());
        acceptResult.Should().BeTrue();
        session.Status.Should().Be(FastAuthStatus.Accepted);
        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void FullLifecycle_Pending_Scanned_Rejected()
    {
        var session = CreateSession();

        session.TryScan(userId: 42);
        var code = session.ConfirmationCode!;

        var rejectResult = session.TryReject(code, 42);
        rejectResult.Should().BeTrue();
        session.Status.Should().Be(FastAuthStatus.Rejected);
        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void FullLifecycle_Pending_Expired()
    {
        var session = CreateSession();
        session.TryExpire();
        session.Status.Should().Be(FastAuthStatus.Expired);
        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void FullLifecycle_Scanned_Expired()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryExpire();
        session.Status.Should().Be(FastAuthStatus.Expired);
        session.IsFinal.Should().BeTrue();
    }

    [Fact]
    public void FullLifecycle_AcceptedCannotBeRejected()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());
        session.TryReject(session.ConfirmationCode!, 42).Should().BeFalse();
        session.Status.Should().Be(FastAuthStatus.Accepted);
    }

    [Fact]
    public void FullLifecycle_RejectedCannotBeAccepted()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult()).Should().BeFalse();
        session.Status.Should().Be(FastAuthStatus.Rejected);
    }

    #endregion

    #region Events Channel

    [Fact]
    public void Events_ScanAndAccept_WritesTwoEvents()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryAccept(session.ConfirmationCode!, 42, CreateAcceptedResult());

        var events = new List<FastAuthResult>();
        while (session.Events.TryRead(out var evt))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Status.Should().Be(FastAuthStatus.Scanned);
        events[1].Status.Should().Be(FastAuthStatus.Accepted);
    }

    [Fact]
    public void Events_ScanAndReject_WritesTwoEvents()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryReject(session.ConfirmationCode!, 42);

        var events = new List<FastAuthResult>();
        while (session.Events.TryRead(out var evt))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Status.Should().Be(FastAuthStatus.Scanned);
        events[1].Status.Should().Be(FastAuthStatus.Rejected);
    }

    [Fact]
    public void Events_ExpireFromPending_WritesOneEvent()
    {
        var session = CreateSession();
        session.TryExpire();

        var events = new List<FastAuthResult>();
        while (session.Events.TryRead(out var evt))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(1);
        events[0].Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public void Events_ScanAndExpire_WritesTwoEvents()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        session.TryExpire();

        var events = new List<FastAuthResult>();
        while (session.Events.TryRead(out var evt))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
        events[0].Status.Should().Be(FastAuthStatus.Scanned);
        events[1].Status.Should().Be(FastAuthStatus.Expired);
    }

    [Fact]
    public void Events_NoTransitions_NoEvents()
    {
        var session = CreateSession();
        session.Events.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Events_AcceptedResult_ContainsTokens()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var acceptedResult = new FastAuthResult
        {
            Status = FastAuthStatus.Accepted,
            AccessToken = "my_access_token",
            AccessTokenExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(1)),
            RefreshToken = "my_refresh_token",
            RefreshTokenExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(30))
        };
        session.TryAccept(session.ConfirmationCode!, 42, acceptedResult);

        var events = new List<FastAuthResult>();
        while (session.Events.TryRead(out var evt))
        {
            events.Add(evt);
        }

        events[1].AccessToken.Should().Be("my_access_token");
        events[1].RefreshToken.Should().Be("my_refresh_token");
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task TryScan_ConcurrentScans_OnlyOneSucceeds()
    {
        var session = CreateSession();
        const int concurrency = 50;
        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(() => session.TryScan(42))));

        outcomes.Should().ContainSingle(o => o == ScanOutcome.Ok);
        outcomes.Count(o => o == ScanOutcome.AlreadyHandled).Should().Be(concurrency - 1);
    }

    [Fact]
    public async Task TryAccept_ConcurrentAccept_OnlyOneSucceeds()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var code = session.ConfirmationCode!;
        const int concurrency = 50;
        var results = await Task.WhenAll(
            Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(() => session.TryAccept(code, 42, CreateAcceptedResult()))));

        results.Should().ContainSingle(r => r == true);
        results.Count(r => r == false).Should().Be(concurrency - 1);
    }

    [Fact]
    public async Task TryReject_ConcurrentReject_OnlyOneSucceeds()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var code = session.ConfirmationCode!;
        const int concurrency = 50;
        var results = await Task.WhenAll(
            Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(() => session.TryReject(code, 42))));

        results.Should().ContainSingle(r => r == true);
    }

    [Fact]
    public async Task TryExpire_ConcurrentExpire_OnlyOneSucceeds()
    {
        var session = CreateSession();
        const int concurrency = 50;
        var results = await Task.WhenAll(
            Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(() => session.TryExpire())));

        results.Should().ContainSingle(r => r == true);
    }

    [Fact]
    public async Task TryAttachSubscriber_ConcurrentAttach_OnlyOneSucceeds()
    {
        var session = CreateSession();
        const int concurrency = 50;
        var results = await Task.WhenAll(
            Enumerable.Range(0, concurrency)
                .Select(_ => Task.Run(() => session.TryAttachSubscriber())));

        results.Should().ContainSingle(r => r == true);
    }

    [Fact]
    public async Task MixedTransitions_AcceptAndRejectConcurrent_OnlyOneSucceeds()
    {
        var session = CreateSession();
        session.TryScan(userId: 42);
        var code = session.ConfirmationCode!;

        var acceptTask = Task.Run(() => session.TryAccept(code, 42, CreateAcceptedResult()));
        var rejectTask = Task.Run(() => session.TryReject(code, 42));
        var results = await Task.WhenAll(acceptTask, rejectTask);

        results.Should().ContainSingle(r => r == true);
        results.Should().ContainSingle(r => r == false);
    }

    #endregion
}
