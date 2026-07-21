using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.GetPersonChatId;

public class GetPersonChatIdCommand : IRequest<GetPersonChatIdResponse>
{
    public long? UserId { get; set; }

    /// <summary>UUID remote-получателя (этап 2.3). Взаимоисключающе с UserId.</summary>
    public Guid? UserUuid { get; set; }
}
