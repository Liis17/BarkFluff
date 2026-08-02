using BarkFluff.Client.Core.Infrastructure.Presence;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class LastSeenFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Online_IgnoresLastSeen() =>
        Assert.Equal("Messenger_StatusOnline", Format(isOnline: true, Now.AddDays(-3)));

    [Fact]
    public void UnknownLastSeen_FallsBackToOffline()
    {
        Assert.Equal("Messenger_StatusOffline", Format(isOnline: false, lastSeen: null));
        Assert.Equal("Messenger_StatusOffline", Format(isOnline: false, DateTimeOffset.MinValue));
    }

    [Fact]
    public void SeenToday_UsesTimeOfDay() =>
        Assert.Equal("Messenger_StatusLastSeenAt", Format(isOnline: false, Now.AddHours(-2)));

    [Fact]
    public void SeenEarlier_UsesDate() =>
        Assert.Equal("Messenger_StatusLastSeenOn", Format(isOnline: false, Now.AddDays(-2)));

    private static string Format(bool isOnline, DateTimeOffset? lastSeen) =>
        LastSeenFormatter.Format(new StubLocalizationService(), isOnline, lastSeen, Now);
}
