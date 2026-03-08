using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.ListByIds;

public class ListByIdsCommand : IRequest<ListByIdsResponse>
{
    public List<long> Ids { get; set; }
}