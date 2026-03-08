using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetPersonChatId;

public class GetPersonChatIdCommand : IRequest<GetPersonChatIdResponse>
{
    public long UserId { get; set; }
}
