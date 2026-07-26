using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckChatMembership;

public class CheckChatMembershipQuery : IRequest<CheckChatMembershipResponse>
{
    // Заполнен ровно один из двух идентификаторов (этап 4.1): UserId — локальный участник,
    // UserUuid — участник по UUID (remote или локальный). Валидацию делает Host.
    public long? UserId { get; init; }

    public Guid? UserUuid { get; init; }

    public required IReadOnlyList<string> ChatIds { get; init; }
}
