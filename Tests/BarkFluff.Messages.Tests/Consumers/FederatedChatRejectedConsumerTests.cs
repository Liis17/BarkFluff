using BarkFluff.Messages.Consumers;
using BarkFluff.Shared.Queue.Federation;

namespace BarkFluff.Messages.Tests.Consumers;

public class FederatedChatRejectedConsumerTests
{
    private readonly TestHelper _h = new();

    private FederatedChatRejectedConsumer CreateConsumer()
        => new(_h.ChatsStorage, _h.Metrics, TestHelper.CreateLogger<FederatedChatRejectedConsumer>());

    private static ConsumeContext<FederatedChatRejectedEvent> ConsumeContextOf(FederatedChatRejectedEvent message)
    {
        var context = new Mock<ConsumeContext<FederatedChatRejectedEvent>>();
        context.Setup(c => c.Message).Returns(message);
        return context.Object;
    }

    [Fact]
    public async Task Consume_ActiveFederatedChat_MarksRejected()
    {
        var chat = await _h.SeedFederatedChat(1, Guid.NewGuid(), Guid.NewGuid(), "remote.test");
        var consumer = CreateConsumer();

        await consumer.Consume(ConsumeContextOf(new FederatedChatRejectedEvent
        {
            ChatId = chat.Id,
            Reason = "FederatedDmRejected",
        }));

        var updated = await _h.ChatsStorage.GetFederatedChatAsync(chat.Id);
        updated!.FederatedStatus.Should().Be(Domain.FederatedStatus.Rejected);
    }

    [Fact]
    public async Task Consume_UnknownChat_DoesNotThrow()
    {
        var consumer = CreateConsumer();

        var act = async () => await consumer.Consume(ConsumeContextOf(new FederatedChatRejectedEvent
        {
            ChatId = Guid.NewGuid(),
            Reason = "FederatedDmRejected",
        }));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Consume_AlreadyRejected_Idempotent()
    {
        var chat = await _h.SeedFederatedChat(
            1, Guid.NewGuid(), Guid.NewGuid(), "remote.test", Domain.FederatedStatus.Rejected);
        var consumer = CreateConsumer();

        await consumer.Consume(ConsumeContextOf(new FederatedChatRejectedEvent
        {
            ChatId = chat.Id,
            Reason = "FederatedDmRejected",
        }));

        var updated = await _h.ChatsStorage.GetFederatedChatAsync(chat.Id);
        updated!.FederatedStatus.Should().Be(Domain.FederatedStatus.Rejected);
    }
}
