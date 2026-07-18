using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.GetUsersByUuid;

public class GetUsersByUuidQuery : IRequest<GetUsersByUuidResponse>
{
    public GetUsersByUuidRequest Request { get; set; } = new();
}
