using BarkFluff.Onliner.Features.InjectRemoteTyping;
using BarkFluff.Onliner.Messages;
using BarkFluff.Proto.Onliner;

using MassTransit;

namespace BarkFluff.Onliner.Tests.Features.InjectRemoteTyping;

public class InjectRemoteTypingCommandHandlerTests
{
    private readonly TestHelper _h = new();

    private InjectRemoteTypingCommandHandler CreateHandler()
        => new(_h.PublishEndpointMock.Object, _h.Metrics);

    [Fact]
    public async Task Handle_PublishesFanOutEventWithUuid()
    {
        var chatId = Guid.NewGuid().ToString();
        var senderUuid = Guid.NewGuid();

        await CreateHandler().Handle(new InjectRemoteTypingCommand
        {
            ChatId = chatId,
            SenderUuid = senderUuid,
            Action = TypingAction.Typing,
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<TypingChangedEvent>(e =>
                e.ChatId == chatId
                && e.UserUuid == senderUuid
                && e.UserId == 0
                && e.Action == (int)TypingAction.Typing),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownAction_IsTreatedAsTyping()
    {
        await CreateHandler().Handle(new InjectRemoteTypingCommand
        {
            ChatId = Guid.NewGuid().ToString(),
            SenderUuid = Guid.NewGuid(),
            Action = TypingAction.Unknown,
        }, CancellationToken.None);

        _h.PublishEndpointMock.Verify(p => p.Publish(
            It.Is<TypingChangedEvent>(e => e.Action == (int)TypingAction.Typing),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Notifier_RelaysRemoteTypingToEveryChatSubscriber()
    {
        // Исключение «кроме отправителя» неприменимо: автор на чужой ноде, среди наших
        // подписчиков его нет — событие обязано дойти до всех, включая подписчика с любым UserId.
        var chatId = Guid.NewGuid().ToString();
        var senderUuid = Guid.NewGuid();

        var (first, firstReceived) = TestHelper.CreateTypingStream();
        var (second, secondReceived) = TestHelper.CreateTypingStream();

        _h.TypingSubscriptionsManager.RegisterSubscription(1, [chatId], first.Object);
        _h.TypingSubscriptionsManager.RegisterSubscription(2, [chatId], second.Object);

        await _h.TypingNotifier.NotifyRemoteTyping(chatId, senderUuid, TypingAction.Typing);

        firstReceived.Should().ContainSingle().Which.UserUuid.Should().Be(senderUuid.ToString());
        secondReceived.Should().ContainSingle().Which.UserUuid.Should().Be(senderUuid.ToString());
        firstReceived[0].UserId.Should().Be(0);
    }
}
