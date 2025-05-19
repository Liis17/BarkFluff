using BarkFluff.Proto.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.SendMessage;

public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, SendMessageResponse>
{
    public Task<SendMessageResponse> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}