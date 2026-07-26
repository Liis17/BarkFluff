using BarkFluff.Proto.Onliner;

using MediatR;

namespace BarkFluff.Onliner.Features.ChangeUsersInSubscription;

public class ChangeUsersInSubscriptionCommand : IRequest<ChangeUsersInSubscriptionResponse>
{
    public required List<long> UserIds { get; init; }

    // remote-пользователи (этап 4.2); к ним privacy не применяется — их отфильтровала origin-нода.
    public List<Guid> UserUuids { get; init; } = [];
}
