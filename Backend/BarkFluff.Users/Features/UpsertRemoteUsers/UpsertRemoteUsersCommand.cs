using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.UpsertRemoteUsers;

public class UpsertRemoteUsersCommand : IRequest<UpsertRemoteUsersResponse>
{
    public UpsertRemoteUsersRequest Request { get; set; } = new();
}
