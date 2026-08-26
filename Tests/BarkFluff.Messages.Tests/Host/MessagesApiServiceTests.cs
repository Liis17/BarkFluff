using BarkFluff.Messages.Features.GetPersonChatId;
using BarkFluff.Messages.Features.SendMessage;
using BarkFluff.Messages.Host;
using BarkFluff.Proto.Messages;
using BarkFluff.Shared.Exceptions.Messages;

using MediatR;

namespace BarkFluff.Messages.Tests.Host;

public class MessagesApiServiceTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly MessagesApiService _service;

    public MessagesApiServiceTests()
    {
        _service = new MessagesApiService(_mediator.Object);
        _mediator
            .Setup(m => m.Send(It.IsAny<GetPersonChatIdCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetPersonChatIdResponse { ChatId = Guid.NewGuid().ToString() });
    }

    [Fact]
    public async Task GetPersonChatId_UserIdZeroWithUuid_MapsToNullUserId()
    {
        // Баг #4: proto user_id по умолчанию 0 (не oneof/optional) — при заполненном только
        // user_uuid, UserId в команде обязан стать null, иначе federated-ветка недостижима.
        var targetUuid = Guid.NewGuid();
        var request = new GetPersonChatIdRequest { UserId = 0, UserUuid = targetUuid.ToString() };

        await _service.GetPersonChatId(request, new TestServerCallContext());

        _mediator.Verify(m => m.Send(
            It.Is<GetPersonChatIdCommand>(c => c.UserId == null && c.UserUuid == targetUuid),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPersonChatId_UserIdNonZero_MapsUserId()
    {
        var request = new GetPersonChatIdRequest { UserId = 42 };

        await _service.GetPersonChatId(request, new TestServerCallContext());

        _mediator.Verify(m => m.Send(
            It.Is<GetPersonChatIdCommand>(c => c.UserId == 42),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessage_MapsClientOperationId()
    {
        var operationId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.IsAny<SendMessageCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { Message = new Proto.Shared.Message() });
        var request = new SendMessageRequest
        {
            ChatId = Guid.NewGuid().ToString(),
            ClientOperationId = operationId.ToString(),
            Message = new BarkFluff.Proto.Messages.OutgoingMessage { Text = "hello" },
        };

        await _service.SendMessage(request, new TestServerCallContext());

        _mediator.Verify(m => m.Send(
            It.Is<SendMessageCommand>(command => command.ClientOperationId == operationId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessage_InvalidClientOperationId_IsRejected()
    {
        var request = new SendMessageRequest
        {
            ChatId = Guid.NewGuid().ToString(),
            ClientOperationId = "not-a-uuid",
            Message = new BarkFluff.Proto.Messages.OutgoingMessage { Text = "hello" },
        };

        var action = async () => await _service.SendMessage(request, new TestServerCallContext());

        await action.Should().ThrowAsync<ClientOperationIdNotValidException>();
        _mediator.Verify(
            m => m.Send(It.IsAny<SendMessageCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
