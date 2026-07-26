using BarkFluff.Federation.Services;
using BarkFluff.Federation.Tests.Infrastructure;

namespace BarkFluff.Federation.Tests.Services;

public class TypingCoalescerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private static readonly Guid Sender = Guid.NewGuid();
    private const string Chat = "chat-1";
    private const string Destination = "node-b.test";

    [Fact]
    public void ShouldSend_FirstCall_Passes()
    {
        var coalescer = new TypingCoalescer(new TestTimeProvider());

        coalescer.ShouldSend(Chat, Sender, Destination, Window, isCancellation: false).Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_WithinWindow_IsThrottled()
    {
        // Клиент шлёт heartbeat каждые 4–5с на чат; при нескольких открытых чатах это заметный
        // поток S2S-вызовов на ровном месте.
        var time = new TestTimeProvider();
        var coalescer = new TypingCoalescer(time);

        coalescer.ShouldSend(Chat, Sender, Destination, Window, false).Should().BeTrue();

        time.Advance(TimeSpan.FromMilliseconds(500));
        coalescer.ShouldSend(Chat, Sender, Destination, Window, false).Should().BeFalse();

        time.Advance(TimeSpan.FromMilliseconds(900));
        coalescer.ShouldSend(Chat, Sender, Destination, Window, false).Should().BeFalse();
    }

    [Fact]
    public void ShouldSend_AfterWindow_PassesAgain()
    {
        var time = new TestTimeProvider();
        var coalescer = new TypingCoalescer(time);

        coalescer.ShouldSend(Chat, Sender, Destination, Window, false).Should().BeTrue();
        time.Advance(TimeSpan.FromSeconds(3));

        coalescer.ShouldSend(Chat, Sender, Destination, Window, false).Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_Cancellation_AlwaysPasses()
    {
        // Иначе индикатор у собеседника гас бы только по клиентскому таймауту.
        var time = new TestTimeProvider();
        var coalescer = new TypingCoalescer(time);

        coalescer.ShouldSend(Chat, Sender, Destination, Window, false).Should().BeTrue();
        time.Advance(TimeSpan.FromMilliseconds(100));

        coalescer.ShouldSend(Chat, Sender, Destination, Window, isCancellation: true).Should().BeTrue();
    }

    [Fact]
    public void ShouldSend_KeyIncludesChatSenderAndDestination()
    {
        // Троттлинг одной пары не должен глушить остальные.
        var time = new TestTimeProvider();
        var coalescer = new TypingCoalescer(time);

        coalescer.ShouldSend(Chat, Sender, Destination, Window, false).Should().BeTrue();

        coalescer.ShouldSend("chat-2", Sender, Destination, Window, false).Should().BeTrue();
        coalescer.ShouldSend(Chat, Guid.NewGuid(), Destination, Window, false).Should().BeTrue();
        coalescer.ShouldSend(Chat, Sender, "node-c.test", Window, false).Should().BeTrue();
    }
}
