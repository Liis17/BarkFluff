using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.GetOnlineStatus;

public class GetOnlineStatusQuery : IRequest<GetOnlineStatusResponse>
{
    public List<long> UserIds { get; set; }

    // remote-пользователи (этап 4.2): статус берётся из кеша remote-статусов, privacy не применяется.
    public List<Guid> UserUuids { get; set; } = [];
}